using System.Runtime.InteropServices;
using NAudio.Wave;

namespace JerichoDown.Modules.Audio.Asio;

public sealed class AsioInputCapture : IWaveIn
{
    private readonly object _gate = new();
    private readonly int _requestedChannelCount;
    private readonly int _requestedSampleRate;
    private readonly bool _useSilentOutputClock;
    private StaThreadDispatcher? _dispatcher;
    private AsioOut? _asio;
    private float[] _sampleBuffer = [];
    private byte[] _byteBuffer = [];
    private AsioInputCaptureDiagnostics? _diagnostics;
    private long _audioCallbackCount;
    private long _lastAudioCallbackUtcTicks;
    private bool _stoppedRaised;

    public AsioInputCapture(
        string driverName,
        int sampleRate,
        int channelCount,
        int inputChannelOffset = 0,
        bool useSilentOutputClock = false)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new ArgumentException("Choose a valid ASIO driver.", nameof(driverName));
        }

        DriverName = driverName.Trim();
        InputChannelOffset = Math.Max(0, inputChannelOffset);
        _requestedSampleRate = Math.Max(8000, sampleRate);
        _requestedChannelCount = Math.Max(1, channelCount);
        _useSilentOutputClock = useSilentOutputClock;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_requestedSampleRate, _requestedChannelCount);
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public string DriverName { get; }

    public int InputChannelOffset { get; }

    public int RequestedSampleRate => _requestedSampleRate;

    public WaveFormat WaveFormat { get; set; }

    public AsioInputCaptureDiagnostics? GetDiagnosticsSnapshot()
    {
        AsioInputCaptureDiagnostics? diagnostics;
        lock (_gate)
        {
            diagnostics = _diagnostics;
        }

        if (diagnostics is null)
        {
            return null;
        }

        var lastCallbackTicks = Interlocked.Read(ref _lastAudioCallbackUtcTicks);
        return diagnostics with
        {
            AudioCallbackCount = Interlocked.Read(ref _audioCallbackCount),
            LastAudioCallbackUtc = lastCallbackTicks <= 0 ? null : new DateTimeOffset(lastCallbackTicks, TimeSpan.Zero)
        };
    }

    public void StartRecording()
    {
        StaThreadDispatcher dispatcher;
        lock (_gate)
        {
            if (_asio is not null || _dispatcher is not null)
            {
                return;
            }

            dispatcher = new StaThreadDispatcher($"Jericho ASIO Input ({DriverName})");
            _dispatcher = dispatcher;
        }

        try
        {
            dispatcher.Invoke(StartRecordingCore);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_dispatcher, dispatcher))
                {
                    _dispatcher = null;
                }
            }

            dispatcher.Dispose();
            throw;
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

    private void StartRecordingCore()
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
                var outputChannelCount = _useSilentOutputClock
                    ? Math.Min(2, Math.Max(0, asio.DriverOutputChannelCount))
                    : 0;
                var outputClockProvider = outputChannelCount > 0
                    ? new SilentAsioOutputProvider(_requestedSampleRate, outputChannelCount)
                    : null;
                _diagnostics = new AsioInputCaptureDiagnostics(
                    DriverName,
                    _requestedSampleRate,
                    _requestedChannelCount,
                    InputChannelOffset,
                    asio.DriverInputChannelCount,
                    asio.DriverOutputChannelCount,
                    channelCount,
                    outputChannelCount,
                    outputClockProvider is not null,
                    Thread.CurrentThread.GetApartmentState().ToString(),
                    SynchronizationContext.Current is not null,
                    Environment.CurrentManagedThreadId,
                    DateTimeOffset.UtcNow,
                    null,
                    0,
                    null);
                Interlocked.Exchange(ref _audioCallbackCount, 0);
                Interlocked.Exchange(ref _lastAudioCallbackUtcTicks, 0);
                if (InputChannelOffset > 0)
                {
                    asio.InputChannelOffset = InputChannelOffset;
                }
                asio.AudioAvailable += AsioAudioAvailable;
                asio.InitRecordAndPlayback(outputClockProvider, channelCount, _requestedSampleRate);
                _stoppedRaised = false;
                _asio = asio;
                asio.Play();
                _diagnostics = _diagnostics with { PlayStartedUtc = DateTimeOffset.UtcNow };
            }
            catch
            {
                asio.AudioAvailable -= AsioAudioAvailable;
                asio.Dispose();
                throw;
            }
        }
    }

    private void AsioAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        try
        {
            Interlocked.Increment(ref _audioCallbackCount);
            Interlocked.Exchange(ref _lastAudioCallbackUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
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
            StopRecording(ex, raiseStopped: true);
        }
    }

    private void StopRecording(Exception? exception, bool raiseStopped)
    {
        StaThreadDispatcher? dispatcher;
        lock (_gate)
        {
            dispatcher = _dispatcher;
            _dispatcher = null;
        }

        if (dispatcher is not null)
        {
            try
            {
                dispatcher.Invoke(() => StopRecordingCore(ref exception));
            }
            finally
            {
                dispatcher.Dispose();
            }
        }

        if (raiseStopped)
        {
            RaiseRecordingStopped(exception);
        }
    }

    private void StopRecordingCore(ref Exception? exception)
    {
        AsioOut? asio;
        lock (_gate)
        {
            asio = _asio;
            _asio = null;
        }

        if (asio is null)
        {
            return;
        }

        asio.AudioAvailable -= AsioAudioAvailable;
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

    private sealed class SilentAsioOutputProvider : IWaveProvider
    {
        public SilentAsioOutputProvider(int sampleRate, int channels)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Math.Max(8000, sampleRate), Math.Max(1, channels));
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}

public sealed record AsioInputCaptureDiagnostics(
    string DriverName,
    int RequestedSampleRate,
    int RequestedChannelCount,
    int InputChannelOffset,
    int DriverInputChannelCount,
    int DriverOutputChannelCount,
    int EffectiveInputChannelCount,
    int OutputClockChannelCount,
    bool UsesSilentOutputClock,
    string ThreadApartmentState,
    bool HasSynchronizationContext,
    int ManagedThreadId,
    DateTimeOffset InitStartedUtc,
    DateTimeOffset? PlayStartedUtc,
    long AudioCallbackCount,
    DateTimeOffset? LastAudioCallbackUtc);
