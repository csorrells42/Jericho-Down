using System.Runtime.InteropServices;
using NAudio.Wave;

namespace JerichoDown.Audio;

public sealed class AsioInputCapture : IWaveIn
{
    private readonly object _gate = new();
    private readonly int _requestedChannelCount;
    private readonly int _requestedSampleRate;
    private AsioOut? _asio;
    private float[] _sampleBuffer = [];
    private byte[] _byteBuffer = [];
    private bool _stoppedRaised;

    public AsioInputCapture(string driverName, int sampleRate, int channelCount, int inputChannelOffset = 0)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new ArgumentException("Choose a valid ASIO driver.", nameof(driverName));
        }

        DriverName = driverName.Trim();
        InputChannelOffset = Math.Max(0, inputChannelOffset);
        _requestedSampleRate = Math.Max(8000, sampleRate);
        _requestedChannelCount = Math.Max(1, channelCount);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_requestedSampleRate, _requestedChannelCount);
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public string DriverName { get; }

    public int InputChannelOffset { get; }

    public WaveFormat WaveFormat { get; set; }

    public void StartRecording()
    {
        lock (_gate)
        {
            if (_asio is not null)
            {
                return;
            }

            var asio = new AsioOut(DriverName);
            try
            {
                var availableChannels = asio.DriverInputChannelCount - InputChannelOffset;
                if (availableChannels <= 0)
                {
                    throw new InvalidOperationException($"ASIO driver '{DriverName}' has no input channels available at offset {InputChannelOffset}.");
                }

                var channelCount = Math.Clamp(_requestedChannelCount, 1, availableChannels);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_requestedSampleRate, channelCount);
                asio.InputChannelOffset = InputChannelOffset;
                asio.AudioAvailable += AsioAudioAvailable;
                asio.PlaybackStopped += AsioPlaybackStopped;
                asio.DriverResetRequest += AsioDriverResetRequested;
                asio.InitRecordAndPlayback(null, channelCount, _requestedSampleRate);
                _stoppedRaised = false;
                _asio = asio;
                asio.Play();
            }
            catch
            {
                asio.AudioAvailable -= AsioAudioAvailable;
                asio.PlaybackStopped -= AsioPlaybackStopped;
                asio.DriverResetRequest -= AsioDriverResetRequested;
                asio.Dispose();
                throw;
            }
        }
    }

    public void StopRecording()
    {
        StopRecording(null, raiseStopped: true);
    }

    public void Dispose()
    {
        StopRecording(null, raiseStopped: true);
    }

    public static int CopyInterleavedSamplesToBytes(ReadOnlySpan<float> samples, Span<byte> destination)
    {
        var bytes = MemoryMarshal.AsBytes(samples);
        if (destination.Length < bytes.Length)
        {
            throw new ArgumentException("Destination is too small for ASIO samples.", nameof(destination));
        }

        bytes.CopyTo(destination);
        return bytes.Length;
    }

    private void AsioAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        try
        {
            var sampleCount = checked(e.SamplesPerBuffer * WaveFormat.Channels);
            if (_sampleBuffer.Length < sampleCount)
            {
                _sampleBuffer = new float[sampleCount];
            }

            var samples = _sampleBuffer.AsSpan(0, sampleCount);
            e.GetAsInterleavedSamples(_sampleBuffer);
            var byteCount = checked(sampleCount * sizeof(float));
            if (_byteBuffer.Length < byteCount)
            {
                _byteBuffer = new byte[byteCount];
            }

            CopyInterleavedSamplesToBytes(samples, _byteBuffer.AsSpan(0, byteCount));
            DataAvailable?.Invoke(this, new WaveInEventArgs(_byteBuffer, byteCount));
        }
        catch (Exception ex)
        {
            RaiseRecordingStopped(ex);
        }
    }

    private void AsioPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        StopRecording(e.Exception, raiseStopped: true);
    }

    private void AsioDriverResetRequested(object? sender, EventArgs e)
    {
        ThreadPool.QueueUserWorkItem(_ => StopRecording(new InvalidOperationException("ASIO driver reset requested."), raiseStopped: true));
    }

    private void StopRecording(Exception? exception, bool raiseStopped)
    {
        AsioOut? asio;
        lock (_gate)
        {
            asio = _asio;
            _asio = null;
        }

        if (asio is not null)
        {
            asio.AudioAvailable -= AsioAudioAvailable;
            asio.PlaybackStopped -= AsioPlaybackStopped;
            asio.DriverResetRequest -= AsioDriverResetRequested;
            try
            {
                asio.Stop();
            }
            catch (Exception ex) when (exception is null)
            {
                exception = ex;
            }

            asio.Dispose();
        }

        if (raiseStopped)
        {
            RaiseRecordingStopped(exception);
        }
    }

    private void RaiseRecordingStopped(Exception? exception)
    {
        lock (_gate)
        {
            if (_stoppedRaised)
            {
                return;
            }

            _stoppedRaised = true;
        }

        RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));
    }
}

