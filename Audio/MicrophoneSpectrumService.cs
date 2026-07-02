using NAudio.Wave;

using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave.SampleProviders;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace PodcastWorkbench.Audio;

public sealed class MicrophoneSpectrumService : IDisposable
{
    private const int DefaultSampleRate = 48000;
    private const int SpectrumDisplaySampleRate = 48000;
    private const int CaptureBufferMilliseconds = 8;
    private static readonly int[] CaptureBufferFallbackMilliseconds = [CaptureBufferMilliseconds, 10, 15];
    private static readonly long SpectrumAnalysisIntervalTicks = Math.Max(1, TimeSpan.FromMilliseconds(25).Ticks * System.Diagnostics.Stopwatch.Frequency / TimeSpan.TicksPerSecond);
    private const int WasapiProcessedOutputLatencyMilliseconds = 50;
    private const int WaveOutProcessedOutputLatencyMilliseconds = 90;
    private const int MediaFoundationResamplerQuality = 60;
    private const bool UseWasapiEventDrivenOutput = true;
    private const int ProcessedOutputDiscardBufferBytes = 32768;
    private static readonly int[] PreferredSampleRates = [192000, 96000, 48000, 44100];
    private static readonly TimeSpan ProcessedOutputBufferDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan InitialLiveOutputBufferedDuration = TimeSpan.FromMilliseconds(55);
    private static readonly TimeSpan TargetLiveOutputBufferedDuration = TimeSpan.FromMilliseconds(55);
    private static readonly TimeSpan MaximumLiveOutputBufferedDuration = TimeSpan.FromMilliseconds(140);
    private const double ProcessedOutputRecoveryRampMilliseconds = 6d;
    private const int MaximumCaptureRecoveryAttempts = 3;
    [ThreadStatic]
    private static bool _audioThreadSchedulingConfigured;
    [ThreadStatic]
    private static IntPtr _audioThreadCharacteristicsHandle;
    private SpectrumAnalyzer _processedAnalyzer = new(SpectrumDisplaySampleRate);
    private SpectrumAnalyzer _rawAnalyzer = new(SpectrumDisplaySampleRate);
    private SpectrumAnalyzer _input1Analyzer = new(SpectrumDisplaySampleRate);
    private SpectrumAnalyzer _input2Analyzer = new(SpectrumDisplaySampleRate);
    private IWaveIn? _capture;
    private VoiceSampleProcessor? _voiceProcessor;
    private IWavePlayer? _processedOutput;
    private BufferedWaveProvider? _processedOutputProvider;
    private IWaveProvider? _processedOutputPlaybackProvider;
    private InputChannelMode _inputChannelMode = InputChannelMode.MonoSum;
    private int _activeSampleRate = DefaultSampleRate;
    private int _processedOutputSampleRate;
    private int _processedOutputDeviceNumber = -1;
    private string? _processedOutputEndpointId;
    private int _processedOutputEnabled;
    private int _processedOutputRampSamplesRemaining;
    private int _processedOutputRampSamplesTotal;
    private uint _ditherState = 0x6D2B79F5u;
    private float[] _monoSamples = [];
    private float[] _processedSamples = [];
    private float[] _rawSpectrumResampleBuffer = [];
    private float[] _processedSpectrumResampleBuffer = [];
    private float[] _input1SpectrumResampleBuffer = [];
    private float[] _input2SpectrumResampleBuffer = [];
    private byte[] _processedOutputBytes = [];
    private byte[] _processedOutputDiscardBytes = [];
    private int _spectrumAnalysisInProgress;
    private int _spectrumAnalysisVersion;
    private long _nextSpectrumAnalysisTimestamp;
    private long _lastAudioCallbackTimestamp;
    private double _audioCallbackIntervalMs;
    private double _audioExpectedCallbackIntervalMs;
    private double _audioProcessingTimeMs;
    private double _audioBufferDurationMs = CaptureBufferMilliseconds;
    private int _audioCallbackSampleCount;
    private int _audioLateFrameCount;
    private int _audioBufferResetCount;
    private int _spectrumSkippedFrameCount;
    private int _stereoInputAnalysisEnabled;
    private int _waveformSamplesEnabled;
    private int _currentDeviceNumber;
    private VoiceProcessorSettings? _currentProcessorSettings;
    private InputChannelMode _currentInputChannelMode = InputChannelMode.MonoSum;
    private bool _autoRecoverCapture;
    private bool _isStoppingCapture;
    private bool _isDisposing;
    private int _captureRecoveryInProgress;
    private System.Runtime.GCLatencyMode _previousGcLatencyMode;
    private bool _gcLatencyModeChanged;
    private readonly VoiceProcessingTelemetry _emptyTelemetry = new();

    private sealed class SpectrumAnalysisWorkItem
    {
        public required MicrophoneSpectrumService Service { get; init; }

        public required SpectrumAnalyzer ProcessedAnalyzer { get; init; }

        public required SpectrumAnalyzer RawAnalyzer { get; init; }

        public required SpectrumAnalyzer Input1Analyzer { get; init; }

        public required SpectrumAnalyzer Input2Analyzer { get; init; }

        public required float[] RawSamples { get; init; }

        public required int RawSampleCount { get; init; }

        public required float[] ProcessedSamples { get; init; }

        public required int ProcessedSampleCount { get; init; }

        public required float[] Input1Samples { get; init; }

        public required float[] Input2Samples { get; init; }

        public required VoiceProcessingTelemetry Telemetry { get; init; }

        public required int SampleRate { get; init; }

        public required int SpectrumAnalysisVersion { get; init; }

        public required bool ReturnAnalysisSampleBuffersToPool { get; init; }

        public required bool IncludeWaveformSamples { get; init; }
    }

    public event EventHandler<SpectrumFrame>? SpectrumAvailable;

    public event EventHandler<string>? StreamStatusChanged;

    public bool IsRunning => _capture is not null;

    public bool IsProcessedOutputEnabled => IsProcessedOutputEnabledVolatile();

    private bool IsProcessedOutputEnabledVolatile()
    {
        return System.Threading.Volatile.Read(ref _processedOutputEnabled) != 0;
    }

    private void SetProcessedOutputEnabled(bool enabled)
    {
        System.Threading.Volatile.Write(ref _processedOutputEnabled, enabled ? 1 : 0);
    }

    public bool StereoInputAnalysisEnabled
    {
        get => System.Threading.Volatile.Read(ref _stereoInputAnalysisEnabled) != 0;
        set => System.Threading.Volatile.Write(ref _stereoInputAnalysisEnabled, value ? 1 : 0);
    }

    public bool WaveformSamplesEnabled
    {
        get => System.Threading.Volatile.Read(ref _waveformSamplesEnabled) != 0;
        set => System.Threading.Volatile.Write(ref _waveformSamplesEnabled, value ? 1 : 0);
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

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioOutputDevice(FindMatchingWaveOutDeviceNumber(endpoint.FriendlyName), endpoint.FriendlyName, endpoint.ID));
            }
        }
        catch
        {
            AddWaveOutDevices(devices);
        }

        if (devices.Count == 1)
        {
            AddWaveOutDevices(devices);
        }

        return devices;
    }

    public static AudioDeviceFormat? TryGetInputDeviceFormat(AudioInputDevice device)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (!DeviceNamesMatch(device.Name, endpoint.FriendlyName))
                {
                    continue;
                }

                var format = endpoint.AudioClient.MixFormat;
                return new AudioDeviceFormat(format.SampleRate, format.Channels, format.BitsPerSample);
            }
        }
        catch
        {
        }

        return null;
    }

    public static AudioDeviceFormat? TryGetOutputDeviceFormat(AudioOutputDevice device)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoint = string.IsNullOrWhiteSpace(device.EndpointId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(device.EndpointId);
            var format = endpoint.AudioClient.MixFormat;
            return new AudioDeviceFormat(format.SampleRate, format.Channels, format.BitsPerSample);
        }
        catch
        {
        }

        return null;
    }

    private static bool DeviceNamesMatch(string waveInName, string endpointName)
    {
        var normalizedWaveInName = NormalizeDeviceName(waveInName);
        var normalizedEndpointName = NormalizeDeviceName(endpointName);
        return normalizedEndpointName.Contains(normalizedWaveInName, StringComparison.OrdinalIgnoreCase)
            || normalizedWaveInName.Contains(normalizedEndpointName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDeviceName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static void AddWaveOutDevices(List<AudioOutputDevice> devices)
    {
        for (var deviceNumber = 0; deviceNumber < WaveOut.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveOut.GetCapabilities(deviceNumber);
            devices.Add(new AudioOutputDevice(deviceNumber, capabilities.ProductName));
        }
    }

    private static int FindMatchingWaveOutDeviceNumber(string endpointName)
    {
        for (var deviceNumber = 0; deviceNumber < WaveOut.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveOut.GetCapabilities(deviceNumber);
            if (endpointName.Contains(capabilities.ProductName, StringComparison.OrdinalIgnoreCase)
                || capabilities.ProductName.Contains(endpointName, StringComparison.OrdinalIgnoreCase))
            {
                return deviceNumber;
            }
        }

        return -1;
    }

    public void Start(int deviceNumber = 0, VoiceProcessorSettings? processorSettings = null, InputChannelMode inputChannelMode = InputChannelMode.MonoSum)
    {
        if (_capture is not null)
        {
            return;
        }

        _currentDeviceNumber = deviceNumber;
        _currentProcessorSettings = processorSettings;
        _currentInputChannelMode = inputChannelMode;
        _autoRecoverCapture = true;
        _isStoppingCapture = false;
        _inputChannelMode = inputChannelMode;
        EnterLowLatencyRuntimeMode();
        IWaveIn capture;
        try
        {
            capture = StartCapture(deviceNumber);
        }
        catch
        {
            RestoreRuntimeMode();
            throw;
        }

        _activeSampleRate = capture.WaveFormat.SampleRate;
        _processedAnalyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        _rawAnalyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        _input1Analyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        _input2Analyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        ResetSampleBuffers();
        ResetSpectrumResampleBuffers();
        System.Threading.Interlocked.Increment(ref _spectrumAnalysisVersion);
        System.Threading.Volatile.Write(ref _nextSpectrumAnalysisTimestamp, 0);
        ResetAudioStabilityCounters();
        _voiceProcessor = processorSettings is null ? null : new VoiceSampleProcessor(processorSettings, _activeSampleRate);
        _capture = capture;
        if (IsProcessedOutputEnabledVolatile())
        {
            RestartProcessedOutput();
        }
    }

    public void Stop()
    {
        _autoRecoverCapture = false;
        var capture = _capture;
        if (capture is null)
        {
            return;
        }

        _isStoppingCapture = true;
        try
        {
            try
            {
                capture.StopRecording();
            }
            catch
            {
            }

            ReleaseCapture();
        }
        finally
        {
            _isStoppingCapture = false;
        }
    }

    public void ConfigureProcessedOutput(bool enabled, AudioOutputDevice outputDevice)
    {
        var deviceChanged = _processedOutputDeviceNumber != outputDevice.DeviceNumber
            || !string.Equals(_processedOutputEndpointId, outputDevice.EndpointId, StringComparison.Ordinal);
        _processedOutputDeviceNumber = outputDevice.DeviceNumber;
        _processedOutputEndpointId = outputDevice.EndpointId;
        SetProcessedOutputEnabled(enabled);

        if (!enabled)
        {
            StopProcessedOutput();
            return;
        }

        if (deviceChanged)
        {
            StopProcessedOutput();
        }

        try
        {
            StartProcessedOutput();
        }
        catch
        {
            SetProcessedOutputEnabled(false);
            StopProcessedOutput();
            throw;
        }
    }

    public void Dispose()
    {
        _isDisposing = true;
        StopProcessedOutput();
        Stop();
        ReleaseCapture();
    }

    public void RestartCurrentCapture()
    {
        if (_currentProcessorSettings is null && _capture is null)
        {
            return;
        }

        Stop();
        Start(_currentDeviceNumber, _currentProcessorSettings, _currentInputChannelMode);
    }

    private void CaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        ConfigureAudioCallbackThread();
        var callbackStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var capture = _capture;
        if (capture is null || e.BytesRecorded == 0)
        {
            return;
        }

        var buffer = e.Buffer.AsSpan(0, e.BytesRecorded);
        var captureFormat = capture.WaveFormat;
        _audioBufferDurationMs = CalculateBufferDurationMs(e.BytesRecorded, captureFormat);
        UpdateAudioCallbackTiming(callbackStartTimestamp, _audioBufferDurationMs);
        var samples = ConvertToMonoSamples(buffer, captureFormat, _inputChannelMode);
        if (samples.Length == 0)
        {
            return;
        }

        var voiceProcessor = _voiceProcessor;
        var analyzerSamples = samples;
        if (voiceProcessor is not null)
        {
            EnsureProcessedSampleBuffer(samples.Length);
            voiceProcessor.Process(samples, _processedSamples.AsSpan(0, samples.Length));
            analyzerSamples = _processedSamples.AsSpan(0, samples.Length);
        }

        AddProcessedOutputSamples(analyzerSamples);
        UpdateAudioProcessingTime(callbackStartTimestamp);
        if (SpectrumAvailable is null)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now < System.Threading.Volatile.Read(ref _nextSpectrumAnalysisTimestamp))
        {
            _spectrumSkippedFrameCount++;
            return;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _spectrumAnalysisInProgress, 1, 0) != 0)
        {
            _spectrumSkippedFrameCount++;
            return;
        }

        System.Threading.Volatile.Write(ref _nextSpectrumAnalysisTimestamp, now + SpectrumAnalysisIntervalTicks);
        var includeWaveformSamples = WaveformSamplesEnabled;
        var rawSamplesSnapshot = includeWaveformSamples
            ? samples.ToArray()
            : RentAndCopySamples(samples);
        var processedSamplesSnapshot = includeWaveformSamples
            ? analyzerSamples.ToArray()
            : RentAndCopySamples(analyzerSamples);
        var includeStereoInputAnalysis = captureFormat.Channels > 1
            && System.Threading.Volatile.Read(ref _stereoInputAnalysisEnabled) != 0;
        var (input1SamplesSnapshot, input2SamplesSnapshot) = includeStereoInputAnalysis
            ? ExtractStereoChannelSamples(buffer, captureFormat)
            : ([], []);
        var telemetry = (voiceProcessor?.Telemetry ?? _emptyTelemetry).Snapshot();
        ApplyAudioStabilityTelemetry(telemetry);
        var spectrumAnalysisVersion = System.Threading.Volatile.Read(ref _spectrumAnalysisVersion);
        var processedAnalyzer = _processedAnalyzer;
        var rawAnalyzer = _rawAnalyzer;
        var input1Analyzer = _input1Analyzer;
        var input2Analyzer = _input2Analyzer;
        var sampleRate = _activeSampleRate;
        var workItem = new SpectrumAnalysisWorkItem
        {
            Service = this,
            ProcessedAnalyzer = processedAnalyzer,
            RawAnalyzer = rawAnalyzer,
            Input1Analyzer = input1Analyzer,
            Input2Analyzer = input2Analyzer,
            RawSamples = rawSamplesSnapshot,
            RawSampleCount = samples.Length,
            ProcessedSamples = processedSamplesSnapshot,
            ProcessedSampleCount = analyzerSamples.Length,
            Input1Samples = input1SamplesSnapshot,
            Input2Samples = input2SamplesSnapshot,
            Telemetry = telemetry,
            SampleRate = sampleRate,
            SpectrumAnalysisVersion = spectrumAnalysisVersion,
            ReturnAnalysisSampleBuffersToPool = !includeWaveformSamples,
            IncludeWaveformSamples = includeWaveformSamples
        };
        System.Threading.ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var workItem = (SpectrumAnalysisWorkItem)state!;
                try
                {
                    workItem.Service.PublishSpectrumFrame(
                        workItem.ProcessedAnalyzer,
                        workItem.RawAnalyzer,
                        workItem.Input1Analyzer,
                        workItem.Input2Analyzer,
                        workItem.RawSamples,
                        workItem.RawSampleCount,
                        workItem.ProcessedSamples,
                        workItem.ProcessedSampleCount,
                        workItem.Input1Samples,
                        workItem.Input2Samples,
                        workItem.Telemetry,
                        workItem.SampleRate,
                        workItem.SpectrumAnalysisVersion,
                        workItem.IncludeWaveformSamples);
                }
                finally
                {
                    if (workItem.ReturnAnalysisSampleBuffersToPool)
                    {
                        ArrayPool<float>.Shared.Return(workItem.RawSamples);
                        ArrayPool<float>.Shared.Return(workItem.ProcessedSamples);
                    }

                    System.Threading.Volatile.Write(ref workItem.Service._spectrumAnalysisInProgress, 0);
                }
            },
            workItem,
            preferLocal: false);
    }

    private void PublishSpectrumFrame(
        SpectrumAnalyzer processedAnalyzer,
        SpectrumAnalyzer rawAnalyzer,
        SpectrumAnalyzer input1Analyzer,
        SpectrumAnalyzer input2Analyzer,
        float[] rawSamples,
        int rawSampleCount,
        float[] processedSamples,
        int processedSampleCount,
        float[] input1Samples,
        float[] input2Samples,
        VoiceProcessingTelemetry telemetry,
        int sampleRate,
        int spectrumAnalysisVersion,
        bool includeWaveformSamples)
    {
        if (spectrumAnalysisVersion != System.Threading.Volatile.Read(ref _spectrumAnalysisVersion))
        {
            return;
        }

        var rawSamplesSpan = rawSamples.AsSpan(0, Math.Clamp(rawSampleCount, 0, rawSamples.Length));
        var processedSamplesSpan = processedSamples.AsSpan(0, Math.Clamp(processedSampleCount, 0, processedSamples.Length));
        var rawSpectrumSamples = ResampleForSpectrum(rawSamplesSpan, sampleRate, ref _rawSpectrumResampleBuffer);
        var processedSpectrumSamples = ResampleForSpectrum(processedSamplesSpan, sampleRate, ref _processedSpectrumResampleBuffer);
        var input1SpectrumSamples = input1Samples.Length > 0
            ? ResampleForSpectrum(input1Samples.AsSpan(), sampleRate, ref _input1SpectrumResampleBuffer)
            : ReadOnlySpan<float>.Empty;
        var input2SpectrumSamples = input2Samples.Length > 0
            ? ResampleForSpectrum(input2Samples.AsSpan(), sampleRate, ref _input2SpectrumResampleBuffer)
            : ReadOnlySpan<float>.Empty;
        var rawAnalysis = rawAnalyzer.AnalyzeSamples(rawSpectrumSamples);
        var processedAnalysis = processedAnalyzer.AnalyzeSamples(processedSpectrumSamples);
        double[]? input1Magnitudes = null;
        double[]? input2Magnitudes = null;
        if (input1SpectrumSamples.Length > 0)
        {
            input1Magnitudes = input1Analyzer.AnalyzeSamples(input1SpectrumSamples).Magnitudes;
        }

        if (input2SpectrumSamples.Length > 0)
        {
            input2Magnitudes = input2Analyzer.AnalyzeSamples(input2SpectrumSamples).Magnitudes;
        }

        var rawPeakLevel = GetPeakLevel(rawSamplesSpan);
        var processedPeakLevel = GetPeakLevel(processedSamplesSpan);
        var input1PeakLevel = GetPeakLevel(input1Samples);
        var input2PeakLevel = GetPeakLevel(input2Samples);

        if (spectrumAnalysisVersion != System.Threading.Volatile.Read(ref _spectrumAnalysisVersion))
        {
            return;
        }

        SpectrumAvailable?.Invoke(
            this,
            new SpectrumFrame(
                processedAnalysis.Magnitudes,
                rawAnalysis.Magnitudes,
                includeWaveformSamples ? processedSamples : [],
                includeWaveformSamples ? rawSamples : [],
                processedPeakLevel,
                rawPeakLevel,
                telemetry,
                sampleRate,
                input1Magnitudes,
                input2Magnitudes,
                input1PeakLevel,
                input2PeakLevel,
                input1Samples,
                input2Samples));
    }

    private static float[] RentAndCopySamples(ReadOnlySpan<float> samples)
    {
        var buffer = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(buffer);
        return buffer;
    }

    private static ReadOnlySpan<float> ResampleForSpectrum(ReadOnlySpan<float> samples, int sourceSampleRate, ref float[] outputBuffer)
    {
        if (samples.Length == 0 || sourceSampleRate <= 0 || sourceSampleRate == SpectrumDisplaySampleRate)
        {
            return samples;
        }

        var outputLength = Math.Max(1, (int)Math.Round(samples.Length * (double)SpectrumDisplaySampleRate / sourceSampleRate));
        if (outputBuffer.Length < outputLength)
        {
            outputBuffer = new float[outputLength];
        }

        var output = outputBuffer.AsSpan(0, outputLength);
        var sourceStep = sourceSampleRate / (double)SpectrumDisplaySampleRate;
        for (var i = 0; i < outputLength; i++)
        {
            var sourcePosition = i * sourceStep;
            var sourceIndex = (int)sourcePosition;
            if (sourceIndex >= samples.Length - 1)
            {
                output[i] = samples[^1];
                continue;
            }

            var fraction = sourcePosition - sourceIndex;
            var a = samples[sourceIndex];
            var b = samples[sourceIndex + 1];
            output[i] = (float)(a + (b - a) * fraction);
        }

        return output;
    }

    private void ResetSpectrumResampleBuffers()
    {
        _rawSpectrumResampleBuffer = [];
        _processedSpectrumResampleBuffer = [];
        _input1SpectrumResampleBuffer = [];
        _input2SpectrumResampleBuffer = [];
    }

    private static double GetPeakLevel(ReadOnlySpan<float> samples)
    {
        var peak = 0d;
        foreach (var sample in samples)
        {
            if (float.IsFinite(sample))
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }
        }

        return peak;
    }

    private IWaveIn StartCapture(int deviceNumber)
    {
        Exception? startException = null;
        if (TryStartWasapiCapture(deviceNumber, out var wasapiCapture, out var wasapiException))
        {
            return wasapiCapture;
        }

        startException = wasapiException;
        foreach (var sampleRate in PreferredSampleRates)
        {
            foreach (var channelCount in new[] { 2, 1 })
            {
                foreach (var waveFormat in CreatePreferredCaptureFormats(sampleRate, channelCount))
                {
                    foreach (var bufferMilliseconds in CaptureBufferFallbackMilliseconds)
                    {
                        var capture = CreateWaveInCapture(deviceNumber, waveFormat, bufferMilliseconds);
                        try
                        {
                            capture.StartRecording();
                            return capture;
                        }
                        catch (Exception ex)
                        {
                            startException ??= ex;
                            DetachCaptureEvents(capture);
                            capture.Dispose();
                        }
                    }
                }
            }
        }

        throw startException ?? new InvalidOperationException("Could not start microphone capture.");
    }

    private bool TryStartWasapiCapture(int deviceNumber, out IWaveIn capture, out Exception? startException)
    {
        capture = null!;
        startException = null;
        var endpoint = FindMatchingCaptureEndpoint(deviceNumber);
        if (endpoint is null)
        {
            return false;
        }

        var mixFormat = endpoint.AudioClient.MixFormat;
        foreach (var bufferMilliseconds in CaptureBufferFallbackMilliseconds)
        {
            var wasapiCapture = new WasapiCapture(endpoint, useEventSync: true, audioBufferMillisecondsLength: bufferMilliseconds);
            wasapiCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, Math.Max(1, mixFormat.Channels));
            AttachCaptureEvents(wasapiCapture);
            try
            {
                wasapiCapture.StartRecording();
                capture = wasapiCapture;
                return true;
            }
            catch (Exception ex)
            {
                startException ??= ex;
                DetachCaptureEvents(wasapiCapture);
                wasapiCapture.Dispose();
            }
        }

        return false;
    }

    private static MMDevice? FindMatchingCaptureEndpoint(int deviceNumber)
    {
        AudioInputDevice waveInDevice;
        try
        {
            var capabilities = WaveIn.GetCapabilities(deviceNumber);
            waveInDevice = new AudioInputDevice(deviceNumber, capabilities.ProductName);
        }
        catch
        {
            return null;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(endpoint => DeviceNamesMatch(waveInDevice.Name, endpoint.FriendlyName));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<WaveFormat> CreatePreferredCaptureFormats(int sampleRate, int channelCount)
    {
        yield return WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
        yield return new WaveFormat(sampleRate, 32, channelCount);
        yield return new WaveFormat(sampleRate, 24, channelCount);
        yield return new WaveFormat(sampleRate, 16, channelCount);
    }

    private WaveInEvent CreateWaveInCapture(int deviceNumber, WaveFormat waveFormat, int bufferMilliseconds)
    {
        var capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = waveFormat,
            BufferMilliseconds = bufferMilliseconds
        };
        AttachCaptureEvents(capture);
        return capture;
    }

    private void AttachCaptureEvents(IWaveIn capture)
    {
        capture.DataAvailable += CaptureDataAvailable;
        capture.RecordingStopped += CaptureRecordingStopped;
    }

    private void DetachCaptureEvents(IWaveIn capture)
    {
        capture.DataAvailable -= CaptureDataAvailable;
        capture.RecordingStopped -= CaptureRecordingStopped;
    }

    private void StartProcessedOutput()
    {
        if (_processedOutput is not null || _capture is null)
        {
            return;
        }

        _processedOutputSampleRate = _activeSampleRate;
        var pcmProvider = CreateProcessedOutputProvider(new WaveFormat(_activeSampleRate, 16, 2));
        if (TryStartWaveOutProcessedOutput(pcmProvider, out var pcmWaveOut))
        {
            _processedOutputProvider = pcmProvider;
            _processedOutput = pcmWaveOut;
            ArmProcessedOutputRecoveryRamp();
            return;
        }

        if (TryStartWasapiProcessedOutput(pcmProvider, out var pcmWasapiOutput, out var pcmPlaybackProvider))
        {
            _processedOutputProvider = pcmProvider;
            _processedOutputPlaybackProvider = pcmPlaybackProvider;
            _processedOutput = pcmWasapiOutput;
            ArmProcessedOutputRecoveryRamp();
            return;
        }

        var floatProvider = CreateProcessedOutputProvider(WaveFormat.CreateIeeeFloatWaveFormat(_activeSampleRate, 2));
        if (TryStartWaveOutProcessedOutput(floatProvider, out var floatWaveOut))
        {
            _processedOutputProvider = floatProvider;
            _processedOutput = floatWaveOut;
            ArmProcessedOutputRecoveryRamp();
            return;
        }

        if (TryStartWasapiProcessedOutput(floatProvider, out var floatOutput, out var floatPlaybackProvider))
        {
            _processedOutputProvider = floatProvider;
            _processedOutputPlaybackProvider = floatPlaybackProvider;
            _processedOutput = floatOutput;
            ArmProcessedOutputRecoveryRamp();
            return;
        }

        StopProcessedOutput();
        throw new InvalidOperationException("Could not start the selected processed output device.");
    }

    private bool CanUseWaveOutProcessedOutputFallback()
    {
        return string.IsNullOrWhiteSpace(_processedOutputEndpointId) || _processedOutputDeviceNumber >= 0;
    }

    private static BufferedWaveProvider CreateProcessedOutputProvider(WaveFormat waveFormat)
    {
        var provider = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = ProcessedOutputBufferDuration,
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        PrimeProcessedOutputProvider(provider);
        return provider;
    }

    private static void PrimeProcessedOutputProvider(BufferedWaveProvider provider)
    {
        var byteCount = DurationToAlignedByteCount(InitialLiveOutputBufferedDuration, provider.WaveFormat);
        var silence = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            Array.Clear(silence, 0, byteCount);
            provider.AddSamples(silence, 0, byteCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(silence);
        }
    }

    private bool TryStartWasapiProcessedOutput(IWaveProvider provider, out IWavePlayer? player, out IWaveProvider? playbackProvider)
    {
        player = null;
        playbackProvider = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoint = string.IsNullOrWhiteSpace(_processedOutputEndpointId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(_processedOutputEndpointId);
            playbackProvider = CreateWasapiOutputProvider(provider, endpoint);
            player = new WasapiOut(endpoint, AudioClientShareMode.Shared, UseWasapiEventDrivenOutput, WasapiProcessedOutputLatencyMilliseconds);
            player.Init(playbackProvider);
            player.Play();
            return true;
        }
        catch
        {
            player?.Dispose();
            DisposePlaybackProvider(playbackProvider, provider);
            player = null;
            playbackProvider = null;
            return false;
        }
    }

    private static IWaveProvider CreateWasapiOutputProvider(IWaveProvider provider, MMDevice endpoint)
    {
        var endpointSampleRate = endpoint.AudioClient.MixFormat.SampleRate;
        if (endpointSampleRate == provider.WaveFormat.SampleRate)
        {
            return provider;
        }

        if (TryCreateHighQualityOutputResampler(provider, endpointSampleRate, out var mediaFoundationResampler))
        {
            return mediaFoundationResampler;
        }

        var resampler = new WdlResamplingSampleProvider(provider.ToSampleProvider(), endpointSampleRate);
        return resampler.ToWaveProvider();
    }

    private static bool TryCreateHighQualityOutputResampler(IWaveProvider provider, int outputSampleRate, out IWaveProvider resampler)
    {
        resampler = null!;
        try
        {
            var outputFormat = provider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                ? WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, provider.WaveFormat.Channels)
                : new WaveFormat(outputSampleRate, provider.WaveFormat.BitsPerSample, provider.WaveFormat.Channels);
            resampler = new MediaFoundationResampler(provider, outputFormat)
            {
                ResamplerQuality = MediaFoundationResamplerQuality
            };
            return true;
        }
        catch
        {
            resampler = null!;
            return false;
        }
    }

    private bool TryStartWaveOutProcessedOutput(IWaveProvider provider, out IWavePlayer? player)
    {
        player = null;
        if (!CanUseWaveOutProcessedOutputFallback())
        {
            return false;
        }

        try
        {
            player = StartWaveOutProcessedOutput(provider);
            return true;
        }
        catch
        {
            player?.Dispose();
            player = null;
            return false;
        }
    }

    private IWavePlayer StartWaveOutProcessedOutput(IWaveProvider provider)
    {
        if (!CanUseWaveOutProcessedOutputFallback())
        {
            throw new InvalidOperationException("The selected output endpoint is unavailable.");
        }

        var waveOut = new WaveOutEvent
        {
            DeviceNumber = _processedOutputDeviceNumber,
            DesiredLatency = WaveOutProcessedOutputLatencyMilliseconds
        };
        waveOut.Init(provider);
        waveOut.Play();
        return waveOut;
    }

    private void RestartProcessedOutput()
    {
        StopProcessedOutput();
        StartProcessedOutput();
    }

    private void StopProcessedOutput()
    {
        try
        {
            _processedOutput?.Stop();
        }
        catch
        {
        }

        try
        {
            _processedOutput?.Dispose();
        }
        catch
        {
        }

        try
        {
            DisposePlaybackProvider(_processedOutputPlaybackProvider, _processedOutputProvider);
        }
        finally
        {
            _processedOutput = null;
            _processedOutputPlaybackProvider = null;
            _processedOutputProvider = null;
            _processedOutputSampleRate = 0;
            _processedOutputRampSamplesRemaining = 0;
            _processedOutputRampSamplesTotal = 0;
        }
    }

    private static void DisposePlaybackProvider(IWaveProvider? playbackProvider, IWaveProvider? sourceProvider)
    {
        if (playbackProvider is IDisposable disposable && !ReferenceEquals(playbackProvider, sourceProvider))
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
            }
        }
    }

    private void AddProcessedOutputSamples(ReadOnlySpan<float> samples)
    {
        var provider = _processedOutputProvider;
        if (!IsProcessedOutputEnabledVolatile() || provider is null || samples.IsEmpty)
        {
            return;
        }

        var upcomingByteCount = GetProcessedOutputByteCount(provider, samples.Length);
        if (upcomingByteCount <= 0)
        {
            return;
        }

        TrimProcessedOutputBufferIfNeeded(provider, upcomingByteCount);
        ArmProcessedOutputRecoveryRampIfUnderrun(provider, upcomingByteCount);

        var byteCount = provider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
            ? ConvertFloatSamplesToStereoFloat32(samples)
            : ConvertFloatSamplesToStereoPcm16(samples);
        provider.AddSamples(_processedOutputBytes, 0, byteCount);
        TrimProcessedOutputBufferIfNeeded(provider);
    }

    private static int GetProcessedOutputByteCount(BufferedWaveProvider provider, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return 0;
        }

        var blockAlign = Math.Max(1, provider.WaveFormat.BlockAlign);
        return (int)Math.Min(int.MaxValue, (long)sampleCount * blockAlign);
    }

    private void ArmProcessedOutputRecoveryRampIfUnderrun(BufferedWaveProvider provider, int incomingBytes)
    {
        if (incomingBytes <= 0
            || _processedOutputRampSamplesRemaining > 0
            || provider.BufferedBytes > 0)
        {
            return;
        }

        ArmProcessedOutputRecoveryRamp();
    }

    private void TrimProcessedOutputBufferIfNeeded(BufferedWaveProvider provider, int incomingBytes = 0)
    {
        var maximumBytes = DurationToAlignedByteCount(MaximumLiveOutputBufferedDuration, provider.WaveFormat);
        if (provider.BufferedBytes + Math.Max(0, incomingBytes) <= maximumBytes)
        {
            return;
        }

        TrimProcessedOutputBuffer(provider);
        _audioBufferResetCount++;
    }

    private void TrimProcessedOutputBuffer(BufferedWaveProvider provider)
    {
        var waveFormat = provider.WaveFormat;
        var blockAlign = Math.Max(1, waveFormat.BlockAlign);
        var targetBytes = DurationToAlignedByteCount(TargetLiveOutputBufferedDuration, waveFormat);
        var bytesToDiscard = Math.Max(0, provider.BufferedBytes - targetBytes);
        if (bytesToDiscard <= 0)
        {
            return;
        }

        var discardBufferSize = Math.Min(bytesToDiscard, ProcessedOutputDiscardBufferBytes);
        discardBufferSize = Math.Max(blockAlign, discardBufferSize / blockAlign * blockAlign);
        if (_processedOutputDiscardBytes.Length < discardBufferSize)
        {
            _processedOutputDiscardBytes = new byte[discardBufferSize];
        }

        var discardedBytes = 0;
        var remainingBytes = bytesToDiscard;
        while (remainingBytes > 0)
        {
            var chunkBytes = Math.Min(remainingBytes, _processedOutputDiscardBytes.Length);
            chunkBytes = Math.Max(blockAlign, chunkBytes / blockAlign * blockAlign);
            if (chunkBytes > remainingBytes)
            {
                break;
            }

            var readBytes = provider.Read(_processedOutputDiscardBytes, 0, chunkBytes);
            discardedBytes += readBytes;
            remainingBytes -= readBytes;
            if (readBytes < chunkBytes)
            {
                break;
            }
        }

        if (discardedBytes < bytesToDiscard / 2)
        {
            provider.ClearBuffer();
            ArmProcessedOutputRecoveryRamp();
            return;
        }

    }

    private static int DurationToAlignedByteCount(TimeSpan duration, WaveFormat waveFormat)
    {
        var blockAlign = Math.Max(1, waveFormat.BlockAlign);
        var averageBytesPerSecond = Math.Max(blockAlign, waveFormat.AverageBytesPerSecond);
        var durationSeconds = Math.Max(0d, duration.TotalSeconds);
        var requestedBytes = (long)Math.Round(averageBytesPerSecond * durationSeconds, MidpointRounding.AwayFromZero);
        requestedBytes = Math.Clamp(requestedBytes, blockAlign, int.MaxValue);
        var alignedBytes = requestedBytes / blockAlign * blockAlign;
        return (int)Math.Max(blockAlign, alignedBytes);
    }

    private int ConvertFloatSamplesToStereoFloat32(ReadOnlySpan<float> samples)
    {
        const int channelCount = 2;
        var byteCount = samples.Length * sizeof(float) * channelCount;
        if (_processedOutputBytes.Length < byteCount)
        {
            _processedOutputBytes = new byte[byteCount];
        }

        var outputSamples = MemoryMarshal.Cast<byte, float>(_processedOutputBytes.AsSpan(0, byteCount));
        var offset = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = PrepareProcessedOutputSample(samples[i]);
            outputSamples[offset] = sample;
            outputSamples[offset + 1] = sample;
            offset += channelCount;
        }

        return byteCount;
    }

    private int ConvertFloatSamplesToStereoPcm16(ReadOnlySpan<float> samples)
    {
        const int channelCount = 2;
        const double ditherScale = 1d / 32768d;
        var byteCount = samples.Length * sizeof(short) * channelCount;
        if (_processedOutputBytes.Length < byteCount)
        {
            _processedOutputBytes = new byte[byteCount];
        }

        var outputSamples = MemoryMarshal.Cast<byte, short>(_processedOutputBytes.AsSpan(0, byteCount));
        var offset = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var inputSample = PrepareProcessedOutputSample(samples[i]);
            var dither = (NextDitherRandom() - NextDitherRandom()) * ditherScale;
            var sample = QuantizeToPcm16(inputSample, dither);
            outputSamples[offset] = sample;
            outputSamples[offset + 1] = sample;
            offset += channelCount;
        }

        return byteCount;
    }

    private static short QuantizeToPcm16(float inputSample, double dither)
    {
        var sample = Math.Clamp((double)inputSample + dither, -1d, 1d);
        var scaled = sample < 0d
            ? sample * -short.MinValue
            : sample * short.MaxValue;
        var quantized = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(quantized, short.MinValue, short.MaxValue);
    }

    private void ArmProcessedOutputRecoveryRamp()
    {
        var rampSamples = Math.Clamp((int)(_activeSampleRate * ProcessedOutputRecoveryRampMilliseconds / 1000d), 1, 256);
        _processedOutputRampSamplesTotal = rampSamples;
        _processedOutputRampSamplesRemaining = rampSamples;
    }

    private float PrepareProcessedOutputSample(float inputSample)
    {
        var sample = Math.Clamp(float.IsFinite(inputSample) ? inputSample : 0f, -1f, 1f);
        if (_processedOutputRampSamplesRemaining <= 0)
        {
            return sample;
        }

        var completedRampSamples = _processedOutputRampSamplesTotal - _processedOutputRampSamplesRemaining;
        var progress = (completedRampSamples + 1d) / Math.Max(1d, _processedOutputRampSamplesTotal);
        var gain = progress * progress * (3d - 2d * progress);
        _processedOutputRampSamplesRemaining--;
        return (float)(sample * gain);
    }

    private double NextDitherRandom()
    {
        _ditherState ^= _ditherState << 13;
        _ditherState ^= _ditherState >> 17;
        _ditherState ^= _ditherState << 5;
        return _ditherState / ((double)uint.MaxValue + 1d);
    }

    private static double CalculateBufferDurationMs(int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0 || format.AverageBytesPerSecond <= 0)
        {
            return CaptureBufferMilliseconds;
        }

        var durationMs = bytesRecorded * 1000d / format.AverageBytesPerSecond;
        return Math.Clamp(durationMs, 1d, 250d);
    }

    private void UpdateAudioCallbackTiming(long callbackStartTimestamp, double bufferDurationMs)
    {
        var previousTimestamp = System.Threading.Interlocked.Exchange(ref _lastAudioCallbackTimestamp, callbackStartTimestamp);
        if (previousTimestamp <= 0)
        {
            return;
        }

        var intervalMs = (callbackStartTimestamp - previousTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        _audioCallbackIntervalMs = intervalMs;
        var minimumExpectedMs = Math.Max(1d, bufferDurationMs);
        if (_audioExpectedCallbackIntervalMs <= 0d)
        {
            _audioExpectedCallbackIntervalMs = Math.Max(minimumExpectedMs, intervalMs);
            _audioCallbackSampleCount = 1;
            return;
        }

        _audioCallbackSampleCount++;
        var previousExpectedMs = Math.Max(minimumExpectedMs, _audioExpectedCallbackIntervalMs);
        if (_audioCallbackSampleCount > 8 && intervalMs > previousExpectedMs * 1.6d)
        {
            _audioLateFrameCount++;
        }

        if (intervalMs > minimumExpectedMs * 0.5d && intervalMs < previousExpectedMs * 3d)
        {
            var learningRate = _audioCallbackSampleCount < 20 ? 0.35d : 0.08d;
            _audioExpectedCallbackIntervalMs += (intervalMs - _audioExpectedCallbackIntervalMs) * learningRate;
        }
    }

    private void UpdateAudioProcessingTime(long callbackStartTimestamp)
    {
        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - callbackStartTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        _audioProcessingTimeMs = elapsedMs;
        var expectedMs = Math.Max(1d, _audioExpectedCallbackIntervalMs);
        if (elapsedMs > expectedMs * 0.8d)
        {
            _audioLateFrameCount++;
        }
    }

    private void ApplyAudioStabilityTelemetry(VoiceProcessingTelemetry telemetry)
    {
        telemetry.AudioCallbackIntervalMs = _audioCallbackIntervalMs;
        telemetry.AudioProcessingTimeMs = _audioProcessingTimeMs;
        telemetry.AudioBufferDurationMs = _audioBufferDurationMs;
        telemetry.AudioExpectedCallbackIntervalMs = _audioExpectedCallbackIntervalMs;
        telemetry.AudioLateFrameCount = _audioLateFrameCount;
        telemetry.AudioBufferResetCount = _audioBufferResetCount;
        telemetry.SpectrumSkippedFrameCount = _spectrumSkippedFrameCount;
    }

    private void ResetAudioStabilityCounters()
    {
        _lastAudioCallbackTimestamp = 0;
        _audioCallbackIntervalMs = 0;
        _audioExpectedCallbackIntervalMs = 0;
        _audioProcessingTimeMs = 0;
        _audioBufferDurationMs = CaptureBufferMilliseconds;
        _audioCallbackSampleCount = 0;
        _audioLateFrameCount = 0;
        _audioBufferResetCount = 0;
        _spectrumSkippedFrameCount = 0;
    }

    private ReadOnlySpan<float> ConvertToMonoSamples(ReadOnlySpan<byte> buffer, WaveFormat format, InputChannelMode channelMode)
    {
        var channels = Math.Max(1, format.Channels);

        if (IsFloatCaptureFormat(format) && format.BitsPerSample == 32)
        {
            var frameCount = GetWholeFrameCount(buffer.Length, channels, sizeof(float));
            EnsureMonoSampleBuffer(frameCount);
            var samples = _monoSamples.AsSpan(0, frameCount);
            FillFloatMonoSamples(buffer, samples, channels, channelMode);

            return samples;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 16)
        {
            var frameCount = GetWholeFrameCount(buffer.Length, channels, sizeof(short));
            EnsureMonoSampleBuffer(frameCount);
            var samples = _monoSamples.AsSpan(0, frameCount);
            FillPcm16MonoSamples(buffer, samples, channels, channelMode);

            return samples;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 32)
        {
            var frameCount = GetWholeFrameCount(buffer.Length, channels, sizeof(int));
            EnsureMonoSampleBuffer(frameCount);
            var samples = _monoSamples.AsSpan(0, frameCount);
            FillPcm32MonoSamples(buffer, samples, channels, channelMode);

            return samples;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 24)
        {
            const int bytesPerSample = 3;
            var frameCount = GetWholeFrameCount(buffer.Length, channels, bytesPerSample);
            EnsureMonoSampleBuffer(frameCount);
            var samples = _monoSamples.AsSpan(0, frameCount);
            FillPcm24MonoSamples(buffer, samples, channels, channelMode);

            return samples;
        }

        return [];
    }

    private static int GetWholeFrameCount(int bufferLength, int channels, int bytesPerSample)
    {
        var frameBytes = Math.Max(1, channels) * Math.Max(1, bytesPerSample);
        return Math.Max(0, bufferLength / frameBytes);
    }

    private void EnsureMonoSampleBuffer(int length)
    {
        if (_monoSamples.Length < length)
        {
            _monoSamples = new float[length];
        }
    }

    private void EnsureProcessedSampleBuffer(int length)
    {
        if (_processedSamples.Length < length)
        {
            _processedSamples = new float[length];
        }
    }

    private void ResetSampleBuffers()
    {
        _monoSamples = [];
        _processedSamples = [];
    }

    private static (float[] Input1Samples, float[] Input2Samples) ExtractStereoChannelSamples(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var rightChannel = Math.Min(1, channels - 1);

        if (IsFloatCaptureFormat(format) && format.BitsPerSample == 32)
        {
            var frameCount = GetWholeFrameCount(buffer.Length, channels, sizeof(float));
            var floatSamples = MemoryMarshal.Cast<byte, float>(buffer);
            var input1Samples = new float[frameCount];
            var input2Samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameOffset = frame * channels;
                input1Samples[frame] = SanitizeCaptureSample(floatSamples[frameOffset]);
                input2Samples[frame] = SanitizeCaptureSample(floatSamples[frameOffset + rightChannel]);
            }

            return (input1Samples, input2Samples);
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 16)
        {
            var frameCount = GetWholeFrameCount(buffer.Length, channels, sizeof(short));
            var pcmSamples = MemoryMarshal.Cast<byte, short>(buffer);
            var input1Samples = new float[frameCount];
            var input2Samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameOffset = frame * channels;
                input1Samples[frame] = SanitizeCaptureSample(pcmSamples[frameOffset] / 32768f);
                input2Samples[frame] = SanitizeCaptureSample(pcmSamples[frameOffset + rightChannel] / 32768f);
            }

            return (input1Samples, input2Samples);
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 32)
        {
            var frameCount = GetWholeFrameCount(buffer.Length, channels, sizeof(int));
            var pcmSamples = MemoryMarshal.Cast<byte, int>(buffer);
            var input1Samples = new float[frameCount];
            var input2Samples = new float[frameCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameOffset = frame * channels;
                input1Samples[frame] = SanitizeCaptureSample(Pcm32ToFloat(pcmSamples[frameOffset]));
                input2Samples[frame] = SanitizeCaptureSample(Pcm32ToFloat(pcmSamples[frameOffset + rightChannel]));
            }

            return (input1Samples, input2Samples);
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 24)
        {
            const int bytesPerSample = 3;
            var frameCount = GetWholeFrameCount(buffer.Length, channels, bytesPerSample);
            var frameStride = channels * bytesPerSample;
            var input1Samples = new float[frameCount];
            var input2Samples = new float[frameCount];
            var input1Offset = 0;
            var input2Offset = rightChannel * bytesPerSample;

            for (var frame = 0; frame < frameCount; frame++)
            {
                input1Samples[frame] = SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, input1Offset));
                input2Samples[frame] = SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, input2Offset));
                input1Offset += frameStride;
                input2Offset += frameStride;
            }

            return (input1Samples, input2Samples);
        }

        return ([], []);
    }

    private static void FillFloatMonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var floatSamples = MemoryMarshal.Cast<byte, float>(buffer);
        if (channelMode == InputChannelMode.Input1Left || channels == 1)
        {
            FillSelectedFloatChannel(floatSamples, samples, channels, 0);
            return;
        }

        if (channelMode == InputChannelMode.Input2Right)
        {
            FillSelectedFloatChannel(floatSamples, samples, channels, Math.Min(1, channels - 1));
            return;
        }

        var summedChannelCount = GetSummedInputChannelCount(channels);
        var baseOffset = 0;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < summedChannelCount; channel++)
            {
                sum += SanitizeCaptureSample(floatSamples[baseOffset + channel]);
            }

            samples[frame] = sum / summedChannelCount;
            baseOffset += channels;
        }
    }

    private static bool IsFloatCaptureFormat(WaveFormat format)
    {
        return format.Encoding == WaveFormatEncoding.IeeeFloat
            || format is WaveFormatExtensible extensible
            && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
    }

    private static bool IsPcmCaptureFormat(WaveFormat format)
    {
        return format.Encoding == WaveFormatEncoding.Pcm
            || format is WaveFormatExtensible extensible
            && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM;
    }

    private static void FillSelectedFloatChannel(ReadOnlySpan<float> inputSamples, Span<float> samples, int channels, int selectedChannel)
    {
        var offset = selectedChannel;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = SanitizeCaptureSample(inputSamples[offset]);
            offset += channels;
        }
    }

    private static void FillPcm16MonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var pcmSamples = MemoryMarshal.Cast<byte, short>(buffer);
        if (channelMode == InputChannelMode.Input1Left || channels == 1)
        {
            FillSelectedPcm16Channel(pcmSamples, samples, channels, 0);
            return;
        }

        if (channelMode == InputChannelMode.Input2Right)
        {
            FillSelectedPcm16Channel(pcmSamples, samples, channels, Math.Min(1, channels - 1));
            return;
        }

        var summedChannelCount = GetSummedInputChannelCount(channels);
        var baseOffset = 0;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < summedChannelCount; channel++)
            {
                sum += SanitizeCaptureSample(pcmSamples[baseOffset + channel] / 32768f);
            }

            samples[frame] = sum / summedChannelCount;
            baseOffset += channels;
        }
    }

    private static void FillSelectedPcm16Channel(ReadOnlySpan<short> inputSamples, Span<float> samples, int channels, int selectedChannel)
    {
        var offset = selectedChannel;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = SanitizeCaptureSample(inputSamples[offset] / 32768f);
            offset += channels;
        }
    }

    private static void FillPcm32MonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var pcmSamples = MemoryMarshal.Cast<byte, int>(buffer);
        if (channelMode == InputChannelMode.Input1Left || channels == 1)
        {
            FillSelectedPcm32Channel(pcmSamples, samples, channels, 0);
            return;
        }

        if (channelMode == InputChannelMode.Input2Right)
        {
            FillSelectedPcm32Channel(pcmSamples, samples, channels, Math.Min(1, channels - 1));
            return;
        }

        var summedChannelCount = GetSummedInputChannelCount(channels);
        var baseOffset = 0;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < summedChannelCount; channel++)
            {
                sum += SanitizeCaptureSample(Pcm32ToFloat(pcmSamples[baseOffset + channel]));
            }

            samples[frame] = sum / summedChannelCount;
            baseOffset += channels;
        }
    }

    private static void FillSelectedPcm32Channel(ReadOnlySpan<int> inputSamples, Span<float> samples, int channels, int selectedChannel)
    {
        var offset = selectedChannel;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = SanitizeCaptureSample(Pcm32ToFloat(inputSamples[offset]));
            offset += channels;
        }
    }

    private static void FillPcm24MonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        const int bytesPerSample = 3;
        if (channelMode == InputChannelMode.Input1Left || channels == 1)
        {
            FillSelectedPcm24Channel(buffer, samples, channels, 0);
            return;
        }

        if (channelMode == InputChannelMode.Input2Right)
        {
            FillSelectedPcm24Channel(buffer, samples, channels, Math.Min(1, channels - 1));
            return;
        }

        var frameStride = channels * bytesPerSample;
        var summedChannelCount = GetSummedInputChannelCount(channels);
        var baseOffset = 0;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            var sum = 0f;
            var channelOffset = baseOffset;
            for (var channel = 0; channel < summedChannelCount; channel++)
            {
                sum += SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, channelOffset));
                channelOffset += bytesPerSample;
            }

            samples[frame] = sum / summedChannelCount;
            baseOffset += frameStride;
        }
    }

    private static void FillSelectedPcm24Channel(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, int selectedChannel)
    {
        const int bytesPerSample = 3;
        var offset = selectedChannel * bytesPerSample;
        var frameStride = channels * bytesPerSample;
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, offset));
            offset += frameStride;
        }
    }

    private static int GetSummedInputChannelCount(int channels)
    {
        return Math.Clamp(channels, 1, 2);
    }

    private static float Pcm32ToFloat(int sample)
    {
        return sample / 2147483648f;
    }

    private static float ReadPcm24SampleRaw(ReadOnlySpan<byte> buffer, int offset)
    {
        var sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return sample / 8388608f;
    }

    private static float SanitizeCaptureSample(float sample)
    {
        return Math.Clamp(float.IsFinite(sample) ? sample : 0f, -1f, 1f);
    }

    private void CaptureRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var shouldRecover = _autoRecoverCapture && !_isStoppingCapture && !_isDisposing;
        ReleaseCapture();
        if (shouldRecover)
        {
            BeginCaptureRecovery(e.Exception);
        }
    }

    private void BeginCaptureRecovery(Exception? exception)
    {
        if (System.Threading.Interlocked.Exchange(ref _captureRecoveryInProgress, 1) != 0)
        {
            return;
        }

        StreamStatusChanged?.Invoke(this, exception is null
            ? "Audio stream stopped; reopening mic stream."
            : $"Audio stream interrupted: {exception.Message}. Reopening mic stream.");

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                for (var attempt = 1; attempt <= MaximumCaptureRecoveryAttempts; attempt++)
                {
                    if (!_autoRecoverCapture || _isDisposing)
                    {
                        return;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(250 * attempt));
                    try
                    {
                        Start(_currentDeviceNumber, _currentProcessorSettings, _currentInputChannelMode);
                        StreamStatusChanged?.Invoke(this, "Audio stream recovered.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        StreamStatusChanged?.Invoke(this, attempt == MaximumCaptureRecoveryAttempts
                            ? $"Audio stream could not recover: {ex.Message}"
                            : $"Audio stream recovery attempt {attempt} failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _captureRecoveryInProgress, 0);
            }
        });
    }

    private void ReleaseCapture()
    {
        var capture = _capture;
        if (capture is null)
        {
            RestoreRuntimeMode();
            return;
        }

        try
        {
            try
            {
                DetachCaptureEvents(capture);
            }
            catch
            {
            }

            try
            {
                capture.Dispose();
            }
            catch
            {
            }
        }
        finally
        {
            if (ReferenceEquals(_capture, capture))
            {
                _capture = null;
            }

            _voiceProcessor = null;
            System.Threading.Interlocked.Increment(ref _spectrumAnalysisVersion);
            RestoreRuntimeMode();
        }
    }

    private void EnterLowLatencyRuntimeMode()
    {
        if (_gcLatencyModeChanged)
        {
            return;
        }

        try
        {
            _previousGcLatencyMode = System.Runtime.GCSettings.LatencyMode;
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
            _gcLatencyModeChanged = true;
        }
        catch
        {
            _gcLatencyModeChanged = false;
        }
    }

    private void RestoreRuntimeMode()
    {
        if (!_gcLatencyModeChanged)
        {
            return;
        }

        try
        {
            System.Runtime.GCSettings.LatencyMode = _previousGcLatencyMode;
        }
        catch
        {
        }
        finally
        {
            _gcLatencyModeChanged = false;
        }
    }

    private static void ConfigureAudioCallbackThread()
    {
        if (_audioThreadSchedulingConfigured)
        {
            return;
        }

        _audioThreadSchedulingConfigured = true;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        }
        catch
        {
        }

        try
        {
            var taskIndex = 0u;
            _audioThreadCharacteristicsHandle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
            if (_audioThreadCharacteristicsHandle == IntPtr.Zero)
            {
                _audioThreadCharacteristicsHandle = AvSetMmThreadCharacteristics("Audio", ref taskIndex);
            }

            if (_audioThreadCharacteristicsHandle != IntPtr.Zero)
            {
                _ = AvSetMmThreadPriority(_audioThreadCharacteristicsHandle, AvrtPriority.High);
            }
        }
        catch
        {
            _audioThreadCharacteristicsHandle = IntPtr.Zero;
        }
    }

    private enum AvrtPriority
    {
        VeryLow = -2,
        Low = -1,
        Normal = 0,
        High = 1,
        Critical = 2
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, AvrtPriority priority);
}


