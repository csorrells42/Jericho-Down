using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JerichoDown.Modules.Audio.Capture;

public sealed class SignalGeneratorCapture : IWaveIn, IDisposable
{
    private const int SampleRate = 48000;
    private const int ChannelCount = 2;
    private const int BlockMilliseconds = 20;
    private const int BlockFrames = SampleRate * BlockMilliseconds / 1000;
    private readonly SignalGenerator _generator;
    private readonly float[] _sampleBuffer = new float[BlockFrames * ChannelCount];
    private readonly byte[] _byteBuffer = new byte[BlockFrames * ChannelCount * sizeof(float)];
    private Thread? _captureThread;
    private bool _stopRequested;
    private bool _isRecording;
    private bool _disposed;
    private long _framesGenerated;

    public SignalGeneratorCapture()
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount);
        _generator = new SignalGenerator(SampleRate, ChannelCount)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 440d,
            Gain = 0.18d
        };
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat { get; set; }

    public void StartRecording()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SignalGeneratorCapture));
        }

        if (_isRecording)
        {
            return;
        }

        _stopRequested = false;
        _isRecording = true;
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "NAudio stereo test tone"
        };
        _captureThread.Start();
    }

    public void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _stopRequested = true;
        if (_captureThread is not null && !ReferenceEquals(Thread.CurrentThread, _captureThread))
        {
            _captureThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRecording();
    }

    public static void ApplyAlternatingStereoGate(
        Span<float> samples,
        int channelCount,
        int sampleRate,
        long startingFrame)
    {
        if (channelCount < 2 || sampleRate <= 0)
        {
            return;
        }

        var frameCount = samples.Length / channelCount;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var absoluteFrame = startingFrame + frame;
            var second = absoluteFrame / sampleRate;
            var leftActive = second % 2 == 0;
            var frameOffset = frame * channelCount;
            if (leftActive)
            {
                samples[frameOffset + 1] = 0f;
            }
            else
            {
                samples[frameOffset] = 0f;
            }
        }
    }

    private void CaptureLoop()
    {
        Exception? stopException = null;
        try
        {
            while (!Volatile.Read(ref _stopRequested))
            {
                Array.Clear(_sampleBuffer);
                var read = _generator.Read(_sampleBuffer, 0, _sampleBuffer.Length);
                if (read > 0)
                {
                    var samples = _sampleBuffer.AsSpan(0, read);
                    ApplyAlternatingStereoGate(samples, ChannelCount, SampleRate, _framesGenerated);
                    _framesGenerated += read / ChannelCount;
                    MemoryMarshal.AsBytes(samples).CopyTo(_byteBuffer);
                    DataAvailable?.Invoke(this, new WaveInEventArgs(_byteBuffer, read * sizeof(float)));
                }

                Thread.Sleep(BlockMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stopException = ex;
        }
        finally
        {
            _isRecording = false;
            _captureThread = null;
            RecordingStopped?.Invoke(this, new StoppedEventArgs(stopException));
        }
    }
}
