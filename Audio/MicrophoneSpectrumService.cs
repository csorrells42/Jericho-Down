using NAudio.Wave;

namespace VoiceWorkbench.Audio;

public sealed class MicrophoneSpectrumService : IDisposable
{
    private const int SampleRate = 44100;
    private const int TestClipSeconds = 10;
    private const int PlaybackChunkMilliseconds = 15;
    private const int PlaybackChunkSamples = SampleRate * PlaybackChunkMilliseconds / 1000;
    private readonly SpectrumAnalyzer _processedAnalyzer = new();
    private readonly SpectrumAnalyzer _rawAnalyzer = new();
    private readonly SpectrumAnalyzer _input1Analyzer = new();
    private readonly SpectrumAnalyzer _input2Analyzer = new();
    private readonly object _testClipLock = new();
    private readonly List<float> _testClipSamples = [];
    private WaveInEvent? _capture;
    private VoiceSampleProcessor? _voiceProcessor;
    private WaveOutEvent? _testClipOutput;
    private BufferedWaveProvider? _testClipProvider;
    private WaveOutEvent? _processedOutput;
    private BufferedWaveProvider? _processedOutputProvider;
    private Timer? _testClipTimer;
    private VoiceSampleProcessor? _testClipProcessor;
    private VoiceProcessorSettings? _testClipSettings;
    private InputChannelMode _inputChannelMode = InputChannelMode.MonoSum;
    private int _testClipPlaybackIndex;
    private int _processedOutputDeviceNumber = -1;
    private bool _isRecordingTestClip;
    private bool _isPlayingTestClip;
    private bool _processedOutputEnabled;

    public event EventHandler<SpectrumFrame>? SpectrumAvailable;
    public event EventHandler? TestClipStateChanged;

    public bool IsRunning => _capture is not null;

    public bool IsRecordingTestClip => _isRecordingTestClip;

    public bool IsPlayingTestClip => _isPlayingTestClip;

    public bool IsProcessedOutputEnabled => _processedOutputEnabled;

    public bool HasTestClip
    {
        get
        {
            lock (_testClipLock)
            {
                return _testClipSamples.Count > 0;
            }
        }
    }

    public double TestClipDurationSeconds
    {
        get
        {
            lock (_testClipLock)
            {
                return _testClipSamples.Count / (double)SampleRate;
            }
        }
    }

    public static IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>();
        for (var deviceNumber = 0; deviceNumber < WaveIn.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveIn.GetCapabilities(deviceNumber);
            devices.Add(new AudioInputDevice(deviceNumber, capabilities.ProductName));
        }

        return devices;
    }

    public static IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var devices = new List<AudioOutputDevice>
        {
            new(-1, "Default playback device")
        };

        for (var deviceNumber = 0; deviceNumber < WaveOut.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveOut.GetCapabilities(deviceNumber);
            devices.Add(new AudioOutputDevice(deviceNumber, capabilities.ProductName));
        }

        return devices;
    }

    public void Start(int deviceNumber = 0, VoiceProcessorSettings? processorSettings = null, InputChannelMode inputChannelMode = InputChannelMode.MonoSum)
    {
        if (_capture is not null)
        {
            return;
        }

        _inputChannelMode = inputChannelMode;
        _voiceProcessor = processorSettings is null ? null : new VoiceSampleProcessor(processorSettings);
        _capture = StartCapture(deviceNumber);
    }

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.StopRecording();
        ReleaseCapture();
    }

    public void ConfigureProcessedOutput(bool enabled, int outputDeviceNumber)
    {
        var deviceChanged = _processedOutputDeviceNumber != outputDeviceNumber;
        _processedOutputDeviceNumber = outputDeviceNumber;
        _processedOutputEnabled = enabled;

        if (!enabled)
        {
            StopProcessedOutput();
            return;
        }

        if (deviceChanged)
        {
            StopProcessedOutput();
        }

        StartProcessedOutput();
    }

    public void BeginTestClipRecording()
    {
        StopTestClipPlayback();
        lock (_testClipLock)
        {
            _testClipSamples.Clear();
            _isRecordingTestClip = true;
        }

        TestClipStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopTestClipRecording()
    {
        if (!_isRecordingTestClip)
        {
            return;
        }

        _isRecordingTestClip = false;
        TestClipStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartTestClipPlayback(VoiceProcessorSettings settings)
    {
        float[] clip;
        lock (_testClipLock)
        {
            if (_testClipSamples.Count == 0)
            {
                return;
            }

            clip = [.. _testClipSamples];
        }

        Stop();
        StopTestClipRecording();
        StopTestClipPlayback();

        _testClipSettings = settings;
        _testClipProcessor = new VoiceSampleProcessor(settings);
        _testClipProvider = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromMilliseconds(250),
            DiscardOnBufferOverflow = true
        };
        _testClipOutput = new WaveOutEvent
        {
            DeviceNumber = _processedOutputDeviceNumber
        };
        _testClipOutput.Init(_testClipProvider);
        _testClipOutput.Play();
        _testClipPlaybackIndex = 0;
        _isPlayingTestClip = true;
        _testClipTimer = new Timer(_ => FeedTestClipPlayback(clip), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(PlaybackChunkMilliseconds));
        TestClipStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopTestClipPlayback()
    {
        _testClipTimer?.Dispose();
        _testClipTimer = null;
        _testClipOutput?.Stop();
        _testClipOutput?.Dispose();
        _testClipOutput = null;
        _testClipProvider = null;
        _testClipProcessor = null;
        _testClipSettings = null;
        _testClipPlaybackIndex = 0;

        if (_isPlayingTestClip)
        {
            _isPlayingTestClip = false;
            TestClipStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        StopTestClipPlayback();
        StopProcessedOutput();
        Stop();
        ReleaseCapture();
    }

    private void CaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded == 0)
        {
            return;
        }

        var buffer = e.Buffer.AsSpan(0, e.BytesRecorded);
        var captureFormat = _capture.WaveFormat;
        var samples = ConvertToMonoSamples(buffer, captureFormat, _inputChannelMode);
        if (samples.Length == 0)
        {
            return;
        }

        CaptureTestClipSamples(samples);

        var analyzerSamples = _voiceProcessor?.Process(samples) ?? samples;
        AddProcessedOutputSamples(analyzerSamples);

        var rawFrame = _rawAnalyzer.AddSamples(samples);
        var processedFrame = _processedAnalyzer.AddSamples(analyzerSamples);
        var input1Samples = captureFormat.Channels > 1 ? ExtractChannelSamples(buffer, captureFormat, 0) : [];
        var input2Samples = captureFormat.Channels > 1 ? ExtractChannelSamples(buffer, captureFormat, 1) : [];
        var input1Frame = input1Samples.Length > 0 ? _input1Analyzer.AddSamples(input1Samples) : null;
        var input2Frame = input2Samples.Length > 0 ? _input2Analyzer.AddSamples(input2Samples) : null;
        SpectrumAvailable?.Invoke(
            this,
            new SpectrumFrame(
                processedFrame.Magnitudes,
                rawFrame.Magnitudes,
                analyzerSamples,
                samples,
                processedFrame.PeakLevel,
                rawFrame.PeakLevel,
                _voiceProcessor?.Telemetry ?? new VoiceProcessingTelemetry(),
                input1Frame?.Magnitudes,
                input2Frame?.Magnitudes,
                input1Frame?.PeakLevel ?? 0d,
                input2Frame?.PeakLevel ?? 0d,
                input1Samples,
                input2Samples));
    }

    private WaveInEvent StartCapture(int deviceNumber)
    {
        Exception? stereoException = null;
        foreach (var channelCount in new[] { 2, 1 })
        {
            var capture = CreateCapture(deviceNumber, channelCount);
            try
            {
                capture.StartRecording();
                return capture;
            }
            catch (Exception ex)
            {
                stereoException ??= ex;
                capture.DataAvailable -= CaptureDataAvailable;
                capture.RecordingStopped -= CaptureRecordingStopped;
                capture.Dispose();
            }
        }

        throw stereoException ?? new InvalidOperationException("Could not start microphone capture.");
    }

    private WaveInEvent CreateCapture(int deviceNumber, int channelCount)
    {
        var capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(SampleRate, 16, channelCount),
            BufferMilliseconds = 15
        };
        capture.DataAvailable += CaptureDataAvailable;
        capture.RecordingStopped += CaptureRecordingStopped;
        return capture;
    }

    private void CaptureTestClipSamples(float[] samples)
    {
        if (!_isRecordingTestClip)
        {
            return;
        }

        var changed = false;
        lock (_testClipLock)
        {
            var remainingSamples = TestClipSeconds * SampleRate - _testClipSamples.Count;
            if (remainingSamples <= 0)
            {
                _isRecordingTestClip = false;
                changed = true;
            }
            else
            {
                _testClipSamples.AddRange(samples.Take(remainingSamples));
                changed = true;
                if (_testClipSamples.Count >= TestClipSeconds * SampleRate)
                {
                    _isRecordingTestClip = false;
                }
            }
        }

        if (changed)
        {
            TestClipStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartProcessedOutput()
    {
        if (_processedOutput is not null)
        {
            return;
        }

        _processedOutputProvider = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromMilliseconds(180),
            DiscardOnBufferOverflow = true
        };
        _processedOutput = new WaveOutEvent
        {
            DeviceNumber = _processedOutputDeviceNumber,
            DesiredLatency = 80
        };
        _processedOutput.Init(_processedOutputProvider);
        _processedOutput.Play();
    }

    private void StopProcessedOutput()
    {
        _processedOutput?.Stop();
        _processedOutput?.Dispose();
        _processedOutput = null;
        _processedOutputProvider = null;
    }

    private void AddProcessedOutputSamples(ReadOnlySpan<float> samples)
    {
        if (!_processedOutputEnabled || _processedOutputProvider is null)
        {
            return;
        }

        if (_processedOutputProvider.BufferedDuration > TimeSpan.FromMilliseconds(140))
        {
            _processedOutputProvider.ClearBuffer();
        }

        var bytes = ConvertFloatSamplesToPcm16(samples);
        _processedOutputProvider.AddSamples(bytes, 0, bytes.Length);
    }

    private void FeedTestClipPlayback(float[] clip)
    {
        if (!_isPlayingTestClip || _testClipProvider is null || _testClipProcessor is null)
        {
            return;
        }

        if (_testClipPlaybackIndex >= clip.Length)
        {
            _testClipPlaybackIndex = 0;
            _testClipProcessor = _testClipSettings is null ? null : new VoiceSampleProcessor(_testClipSettings);
        }

        var count = Math.Min(PlaybackChunkSamples, clip.Length - _testClipPlaybackIndex);
        if (count <= 0)
        {
            return;
        }

        var rawSamples = new float[count];
        Array.Copy(clip, _testClipPlaybackIndex, rawSamples, 0, count);
        _testClipPlaybackIndex += count;

        var processedSamples = _testClipProcessor?.Process(rawSamples) ?? rawSamples;
        _testClipProvider.AddSamples(ConvertFloatSamplesToPcm16(processedSamples), 0, processedSamples.Length * sizeof(short));

        var rawFrame = _rawAnalyzer.AddSamples(rawSamples);
        var processedFrame = _processedAnalyzer.AddSamples(processedSamples);
        SpectrumAvailable?.Invoke(
            this,
            new SpectrumFrame(
                processedFrame.Magnitudes,
                rawFrame.Magnitudes,
                processedSamples,
                rawSamples,
                processedFrame.PeakLevel,
                rawFrame.PeakLevel,
                _testClipProcessor?.Telemetry ?? new VoiceProcessingTelemetry()));
    }

    private static byte[] ConvertFloatSamplesToPcm16(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * sizeof(short)];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(short), sizeof(short)), sample);
        }

        return bytes;
    }

    private static float[] ConvertToMonoSamples(ReadOnlySpan<byte> buffer, WaveFormat format, InputChannelMode channelMode)
    {
        var channels = Math.Max(1, format.Channels);

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var sampleCount = buffer.Length / sizeof(float);
            var frameCount = sampleCount / channels;
            var samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[frame] = PickFloatChannel(buffer, frame, channels, channelMode);
            }

            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var sampleCount = buffer.Length / sizeof(short);
            var frameCount = sampleCount / channels;
            var samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[frame] = PickPcm16Channel(buffer, frame, channels, channelMode);
            }

            return samples;
        }

        return [];
    }

    private static float[] ExtractChannelSamples(ReadOnlySpan<byte> buffer, WaveFormat format, int channelIndex)
    {
        var channels = Math.Max(1, format.Channels);
        var selectedChannel = Math.Clamp(channelIndex, 0, channels - 1);

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var sampleCount = buffer.Length / sizeof(float);
            var frameCount = sampleCount / channels;
            var samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[frame] = BitConverter.ToSingle(buffer.Slice((frame * channels + selectedChannel) * sizeof(float), sizeof(float)));
            }

            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var sampleCount = buffer.Length / sizeof(short);
            var frameCount = sampleCount / channels;
            var samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[frame] = BitConverter.ToInt16(buffer.Slice((frame * channels + selectedChannel) * sizeof(short), sizeof(short))) / 32768f;
            }

            return samples;
        }

        return [];
    }

    private static float PickFloatChannel(ReadOnlySpan<byte> buffer, int frame, int channels, InputChannelMode channelMode)
    {
        if (channelMode == InputChannelMode.Input1Left || channels == 1)
        {
            return BitConverter.ToSingle(buffer.Slice(frame * channels * sizeof(float), sizeof(float)));
        }

        if (channelMode == InputChannelMode.Input2Right)
        {
            var channel = Math.Min(1, channels - 1);
            return BitConverter.ToSingle(buffer.Slice((frame * channels + channel) * sizeof(float), sizeof(float)));
        }

        var sum = 0f;
        for (var channel = 0; channel < channels; channel++)
        {
            sum += BitConverter.ToSingle(buffer.Slice((frame * channels + channel) * sizeof(float), sizeof(float)));
        }

        return sum / channels;
    }

    private static float PickPcm16Channel(ReadOnlySpan<byte> buffer, int frame, int channels, InputChannelMode channelMode)
    {
        if (channelMode == InputChannelMode.Input1Left || channels == 1)
        {
            return BitConverter.ToInt16(buffer.Slice(frame * channels * sizeof(short), sizeof(short))) / 32768f;
        }

        if (channelMode == InputChannelMode.Input2Right)
        {
            var channel = Math.Min(1, channels - 1);
            return BitConverter.ToInt16(buffer.Slice((frame * channels + channel) * sizeof(short), sizeof(short))) / 32768f;
        }

        var sum = 0f;
        for (var channel = 0; channel < channels; channel++)
        {
            sum += BitConverter.ToInt16(buffer.Slice((frame * channels + channel) * sizeof(short), sizeof(short))) / 32768f;
        }

        return sum / channels;
    }

    private void CaptureRecordingStopped(object? sender, StoppedEventArgs e)
    {
        ReleaseCapture();
    }

    private void ReleaseCapture()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= CaptureDataAvailable;
        _capture.RecordingStopped -= CaptureRecordingStopped;
        _capture.Dispose();
        _capture = null;
        _voiceProcessor = null;
    }
}

