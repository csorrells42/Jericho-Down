using NAudio.Wave;

using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave.SampleProviders;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace JerichoDown.Audio;

public sealed class MicrophoneSpectrumService : IDisposable
{
    private const int DefaultSampleRate = 48000;
    private const int SpectrumDisplaySampleRate = 48000;
    private const int CaptureBufferMilliseconds = 8;
    private static readonly int[] CaptureBufferFallbackMilliseconds = [CaptureBufferMilliseconds, 10, 15];
    private const int SpectrumAnalysisIntervalMilliseconds = 24;
    private static readonly long SpectrumAnalysisIntervalTicks = Math.Max(1, TimeSpan.FromMilliseconds(SpectrumAnalysisIntervalMilliseconds).Ticks * System.Diagnostics.Stopwatch.Frequency / TimeSpan.TicksPerSecond);
    private const int WasapiProcessedOutputLatencyMilliseconds = WasapiOutputSettings.StabilityLatencyMilliseconds;
    private const int WaveOutProcessedOutputLatencyMilliseconds = 160;
    private const int MediaFoundationResamplerQuality = 60;
    private const bool UseWasapiEventDrivenOutput = true;
    private const string AsioEndpointPrefix = "asio:";
    private const int MaximumAsioInputChannels = 10;
    private const int ProcessedOutputDiscardBufferBytes = 32768;
    private static readonly int[] PreferredSampleRates = [192000, 96000, 48000, 44100];
    private static readonly int[] PreferredAsioSampleRates = [DefaultSampleRate, 44100, 96000, 192000];
    private static readonly TimeSpan ProcessedOutputBufferDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InitialLiveOutputBufferedDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan TargetLiveOutputBufferedDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MaximumLiveOutputBufferedDuration = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AuxiliaryCaptureTargetLatency = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan AuxiliaryCaptureMaximumLatency = TimeSpan.FromMilliseconds(140);
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
    private readonly object _liveMixLock = new();
    private readonly MixBusProcessor _mixBusProcessor = new();
    private MicrophoneLiveChannelSettings[] _configuredLiveMixChannels = [];
    private LiveMicChannelRuntime[] _liveMixChannels = [];
    private LiveProgramMixBus? _programMixer;
    private MixBusSettings _mixBusSettings = MixBusSettings.Default;
    private readonly object _additionalCaptureLock = new();
    private readonly Dictionary<string, AdditionalCaptureRuntime> _additionalCaptures = [];
    private IWavePlayer? _processedOutput;
    private BufferedWaveProvider? _processedOutputProvider;
    private IWaveProvider? _processedOutputPlaybackProvider;
    private WasapiOutputSettings _wasapiOutputSettings = WasapiOutputSettings.Default;
    private readonly object _processedRecordingLock = new();
    private WaveFileWriter? _processedRecordingWriter;
    private string? _processedRecordingPath;
    private bool _isProcessedRecordingPaused;
    private ProcessedRecordingSource _processedRecordingSource = ProcessedRecordingSource.ProgramMix;
    private int _processedRecordingSelectedChannelNumber = 1;
    private byte[] _processedRecordingBytes = [];
    private float[] _processedRecordingFloatSamples = [];
    private InputChannelMode _inputChannelMode = InputChannelMode.MonoSum;
    private int _activeSampleRate = DefaultSampleRate;
    private int _processedOutputSampleRate;
    private int _processedOutputDeviceNumber = -1;
    private string? _processedOutputEndpointId;
    private string? _processedOutputDeviceName;
    private string _processedOutputBackendDescription = "not routed";
    private int _processedOutputEnabled;
    private int _processedOutputRampSamplesRemaining;
    private int _processedOutputRampSamplesTotal;
    private uint _ditherState = 0x6D2B79F5u;
    private float[] _monoSamples = [];
    private float[] _processedSamples = [];
    private float[] _mixBusSamples = [];
    private float[] _mixOutputSamples = [];
    private float[] _mixOutputMonoSamples = [];
    private float[] _mixChannelInputSamples = [];
    private float[] _mixChannelStereoInputSamples = [];
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
    private long _audioStreamStartedTimestamp;
    private double _audioCallbackIntervalMs;
    private double _audioExpectedCallbackIntervalMs;
    private double _audioProcessingTimeMs;
    private double _audioBufferDurationMs = CaptureBufferMilliseconds;
    private int _audioCallbackSampleCount;
    private int _audioLateFrameCount;
    private int _audioBufferResetCount;
    private int _spectrumSkippedFrameCount;
    private int _stereoInputAnalysisEnabled;
    private int _preferWaveInCapture;
    private int _currentDeviceNumber;
    private string? _currentInputEndpointId;
    private AudioInputBackend _currentInputBackend = AudioInputBackend.Windows;
    private VoiceProcessorSettings? _currentProcessorSettings;
    private InputChannelMode _currentInputChannelMode = InputChannelMode.MonoSum;
    private string _captureBackendDescription = "not open";
    private bool _autoRecoverCapture;
    private bool _isStoppingCapture;
    private bool _isDisposing;
    private int _captureRecoveryInProgress;
    private System.Runtime.GCLatencyMode _previousGcLatencyMode;
    private bool _gcLatencyModeChanged;
    private readonly VoiceProcessingTelemetry _emptyTelemetry = new();

    private sealed class LiveMicChannelRuntime
    {
        public required int ChannelNumber { get; init; }

        public required int DeviceNumber { get; init; }

        public string? EndpointId { get; init; }

        public AudioInputBackend Backend { get; init; }

        public required string DeviceKey { get; init; }

        public required InputChannelMode InputChannelMode { get; init; }

        public required VoiceProcessorSettings ProcessorSettings { get; init; }

        public required double VolumeLinear { get; set; }

        public required double InputGainLinear { get; set; }

        public required bool IsEnabled { get; init; }

        public required bool IsMuted { get; set; }

        public required bool IsSoloed { get; set; }

        public required bool IsPolarityInverted { get; set; }

        public required double DelayMilliseconds { get; set; }

        public required VoiceSampleProcessor Processor { get; init; }

        public required bool PreservesStereo { get; init; }

        public LiveMicBlockSampleProvider? SourceProvider { get; init; }

        public LiveStereoBlockSampleProvider? StereoSourceProvider { get; init; }

        public VoiceProcessorSampleProvider? DspProvider { get; init; }

        public StereoVoiceProcessorSampleProvider? StereoDspProvider { get; init; }

        public required VolumeSampleProvider VolumeProvider { get; init; }

        public StereoPanSampleProvider? PanProvider { get; init; }

        public StereoBalanceSampleProvider? StereoBalanceProvider { get; init; }

        public AudioDelayLine? DelayLine { get; init; }

        public AudioStereoDelayLine? StereoDelayLine { get; init; }

        public required ISampleProvider ProgramProvider { get; init; }

        public required NaudioPeakMeterSampleProvider ProgramMeterProvider { get; init; }

        public required SpectrumAnalyzer Analyzer { get; init; }

        public required SpectrumAnalyzer RawAnalyzer { get; init; }

        public AudioSyncBuffer? SyncBuffer { get; init; }

        public float[] LastRawSamples { get; set; } = [];

        public int LastRawSampleCount { get; set; }

        public float[] LastProcessedSamples { get; set; } = [];

        public int LastProcessedSampleCount { get; set; }
    }

    private sealed class AdditionalCaptureRuntime : IDisposable
    {
        private readonly MicrophoneSpectrumService _service;
        private bool _disposed;

        public AdditionalCaptureRuntime(
            MicrophoneSpectrumService service,
            int deviceNumber,
            string? endpointId,
            AudioInputBackend backend,
            string deviceKey)
        {
            _service = service;
            DeviceNumber = deviceNumber;
            EndpointId = endpointId;
            Backend = backend;
            DeviceKey = deviceKey;
        }
        public AdditionalCaptureRuntime(MicrophoneSpectrumService service, int deviceNumber)
            : this(
                service,
                deviceNumber,
                null,
                AudioInputBackend.Windows,
                MicrophoneSpectrumService.CreateInputDeviceKey(deviceNumber, null, AudioInputBackend.Windows))
        {
        }

        public int DeviceNumber { get; }

        public string? EndpointId { get; }

        public AudioInputBackend Backend { get; }

        public string DeviceKey { get; }

        public IWaveIn? Capture { get; set; }

        public string BackendDescription { get; set; } = "not open";

        public float[] ScratchSamples { get; set; } = [];

        public void DataAvailable(object? sender, WaveInEventArgs e)
        {
            _service.AdditionalCaptureDataAvailable(this, e);
        }

        public void RecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (!_disposed && e.Exception is not null)
            {
                _service.ReportStreamStatus($"{DescribeInputDevice()} stopped: {e.Exception.Message}");
            }
        }

        private string DescribeInputDevice()
        {
            return MicrophoneSpectrumService.DescribeInputDevice(DeviceNumber, EndpointId, Backend);
        }

        public void Dispose()
        {
            _disposed = true;
            var capture = Capture;
            Capture = null;
            if (capture is null)
            {
                return;
            }

            capture.DataAvailable -= DataAvailable;
            capture.RecordingStopped -= RecordingStopped;
            try
            {
                capture.StopRecording();
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
    }

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

        public required MicrophoneSpectrumSampleSnapshot[] MicrophoneSamples { get; init; }

        public required VoiceProcessingTelemetry Telemetry { get; init; }

        public required int SampleRate { get; init; }

        public required int SpectrumAnalysisVersion { get; init; }

        public required bool ReturnAnalysisSampleBuffersToPool { get; init; }

        public required bool IncludeWaveformSamples { get; init; }
    }

    private sealed class MicrophoneSpectrumSampleSnapshot
    {
        public required int ChannelNumber { get; init; }

        public required SpectrumAnalyzer Analyzer { get; init; }

        public required SpectrumAnalyzer RawAnalyzer { get; init; }

        public required float[] RawSamples { get; init; }

        public required int RawSampleCount { get; init; }

        public required float[] ProcessedSamples { get; init; }

        public required int ProcessedSampleCount { get; init; }

        public required int SyncBufferedSamples { get; init; }

        public required int SyncTargetLatencySamples { get; init; }

        public required int SyncUnderflowCount { get; init; }

        public required int SyncDriftTrimCount { get; init; }

        public required int SampleRate { get; init; }

        public required double MeteredPeakLevel { get; init; }
    }

    public event EventHandler<SpectrumFrame>? SpectrumAvailable;

    public event EventHandler<string>? StreamStatusChanged;

    public bool IsRunning => _capture is not null;

    public bool IsProcessedOutputEnabled => IsProcessedOutputEnabledVolatile();

    public bool IsProcessedAudioRecording
    {
        get
        {
            lock (_processedRecordingLock)
            {
                return _processedRecordingWriter is not null;
            }
        }
    }

    public bool IsProcessedAudioRecordingPaused
    {
        get
        {
            lock (_processedRecordingLock)
            {
                return _processedRecordingWriter is not null && _isProcessedRecordingPaused;
            }
        }
    }

    public bool AreAudioCallbacksStale(TimeSpan startupGrace, TimeSpan staleDuration)
    {
        if (_capture is null)
        {
            return false;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var lastCallbackTimestamp = System.Threading.Volatile.Read(ref _lastAudioCallbackTimestamp);
        if (lastCallbackTimestamp == 0)
        {
            var startedTimestamp = System.Threading.Volatile.Read(ref _audioStreamStartedTimestamp);
            return startedTimestamp != 0 && ElapsedSince(startedTimestamp, now) > startupGrace;
        }

        return ElapsedSince(lastCallbackTimestamp, now) > staleDuration;
    }

    public string ActiveInputFormatStatus
    {
        get
        {
            var capture = _capture;
            return capture is null
                ? "not open"
                : $"{DescribeWaveFormat(capture.WaveFormat)} via {_captureBackendDescription}{GetAdditionalCaptureSummary()}";
        }
    }

    public bool IsWasapiCaptureActive => _capture is WasapiCapture;

    public void PreferWaveInCaptureForCurrentDevice()
    {
        System.Threading.Volatile.Write(ref _preferWaveInCapture, 1);
    }

    public string TargetProcessedOutputFormatStatus
    {
        get
        {
            return _capture is null ? "not ready" : DescribeTargetProcessedOutputFormat();
        }
    }

    public string ActualProcessedOutputFormatStatus
    {
        get
        {
            var provider = GetActualProcessedOutputProvider();
            return IsProcessedOutputEnabledVolatile() && provider is not null
                ? DescribeWaveFormat(provider.WaveFormat)
                : "not routed";
        }
    }

    public bool IsProcessedOutputFormatConstrained
    {
        get
        {
            var provider = GetActualProcessedOutputProvider();
            return IsProcessedOutputEnabledVolatile() && provider is not null && !IsTargetProcessedOutputFormat(provider.WaveFormat);
        }
    }

    public string WasapiOutputModeStatus => _wasapiOutputSettings.DisplayText;

    public string ProcessedOutputFormatStatus
    {
        get
        {
            var sourceProvider = _processedOutputProvider;
            if (!IsProcessedOutputEnabledVolatile() || sourceProvider is null)
            {
                return "Live route is not open.";
            }

            var playbackProvider = GetActualProcessedOutputProvider();
            if (playbackProvider is not null && !WaveFormatsMatch(sourceProvider.WaveFormat, playbackProvider.WaveFormat))
            {
                return $"Live route opened via {_processedOutputBackendDescription}; writing {DescribeWaveFormat(sourceProvider.WaveFormat)}, playback {DescribeWaveFormat(playbackProvider.WaveFormat)}.";
            }

            return $"Live route opened via {_processedOutputBackendDescription} as {DescribeWaveFormat(sourceProvider.WaveFormat)}.";
        }
    }

    private IWaveProvider? GetActualProcessedOutputProvider()
    {
        return _processedOutputPlaybackProvider ?? _processedOutputProvider;
    }

    private bool IsProcessedOutputEnabledVolatile()
    {
        return System.Threading.Volatile.Read(ref _processedOutputEnabled) != 0;
    }

    private string GetAdditionalCaptureSummary()
    {
        lock (_additionalCaptureLock)
        {
            return _additionalCaptures.Count == 0
                ? string.Empty
                : $" + {_additionalCaptures.Count} aux";
        }
    }

    private void SetProcessedOutputEnabled(bool enabled)
    {
        System.Threading.Volatile.Write(ref _processedOutputEnabled, enabled ? 1 : 0);
    }

    private void ReportStreamStatus(string message)
    {
        StreamStatusChanged?.Invoke(this, message);
    }

    public bool StereoInputAnalysisEnabled
    {
        get => System.Threading.Volatile.Read(ref _stereoInputAnalysisEnabled) != 0;
        set
        {
            var nextValue = value ? 1 : 0;
            var previousValue = System.Threading.Interlocked.Exchange(ref _stereoInputAnalysisEnabled, nextValue);
            if (previousValue != nextValue)
            {
                RequestImmediateSpectrumAnalysis();
            }
        }
    }

    public void RequestImmediateSpectrumAnalysis()
    {
        System.Threading.Interlocked.Increment(ref _spectrumAnalysisVersion);
        System.Threading.Volatile.Write(ref _nextSpectrumAnalysisTimestamp, 0);
    }

    public void ConfigureLiveMix(IReadOnlyList<MicrophoneLiveChannelSettings> channels, MixBusSettings mixBusSettings)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var configuredChannels = channels
            .Where(channel => channel.ChannelNumber > 0 && channel.ProcessorSettings is not null)
            .OrderBy(channel => channel.ChannelNumber)
            .ToArray();

        lock (_liveMixLock)
        {
            _configuredLiveMixChannels = configuredChannels;
            _mixBusSettings = mixBusSettings;
            if (CanUpdateLiveMixControlsLocked(configuredChannels))
            {
                UpdateLiveMixControlsLocked(configuredChannels);
            }
            else
            {
                RebuildLiveMixProcessorsLocked();
            }
        }

        if (_capture is not null)
        {
            StartAdditionalCaptures();
        }
    }

    public bool TryUpdateLiveMixControls(IReadOnlyList<MicrophoneLiveChannelSettings> channels, MixBusSettings mixBusSettings)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var configuredChannels = channels
            .Where(channel => channel.ChannelNumber > 0 && channel.ProcessorSettings is not null)
            .OrderBy(channel => channel.ChannelNumber)
            .ToArray();

        lock (_liveMixLock)
        {
            if (!CanUpdateLiveMixControlsLocked(configuredChannels))
            {
                return false;
            }

            _configuredLiveMixChannels = configuredChannels;
            _mixBusSettings = mixBusSettings;
            UpdateLiveMixControlsLocked(configuredChannels);
            return true;
        }
    }

    private bool CanUpdateLiveMixControlsLocked(IReadOnlyList<MicrophoneLiveChannelSettings> channels)
    {
        if (channels.Count != _liveMixChannels.Length)
        {
            return false;
        }

        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            var runtime = _liveMixChannels[i];
            if (runtime.ChannelNumber != channel.ChannelNumber
                || runtime.DeviceNumber != channel.DeviceNumber
                || !string.Equals(runtime.EndpointId, channel.EndpointId, StringComparison.Ordinal)
                || runtime.Backend != channel.Backend
                || runtime.InputChannelMode != channel.InputChannelMode
                || runtime.IsEnabled != channel.IsEnabled
                || !ReferenceEquals(runtime.ProcessorSettings, channel.ProcessorSettings))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateLiveMixControlsLocked(IReadOnlyList<MicrophoneLiveChannelSettings> channels)
    {
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            var runtime = _liveMixChannels[i];
            runtime.VolumeLinear = MixBusProcessor.PercentToLinear(channel.VolumePercent);
            runtime.InputGainLinear = DbToLinear(Math.Clamp(channel.InputGainDb, -24d, 24d));
            runtime.IsMuted = channel.IsMuted;
            runtime.IsSoloed = channel.IsSoloed;
            runtime.IsPolarityInverted = channel.IsPolarityInverted;
            if (runtime.PanProvider is not null)
            {
                runtime.PanProvider.Pan = Math.Clamp(channel.Pan / 100d, -1d, 1d);
            }

            if (runtime.StereoBalanceProvider is not null)
            {
                runtime.StereoBalanceProvider.Balance = Math.Clamp(channel.Pan / 100d, -1d, 1d);
            }

            var delayMilliseconds = Math.Clamp(channel.DelayMilliseconds, 0d, 250d);
            if (Math.Abs(runtime.DelayMilliseconds - delayMilliseconds) > 0.01d
                && delayMilliseconds <= 0.01d)
            {
                runtime.DelayLine?.Reset();
                runtime.StereoDelayLine?.Reset();
            }

            runtime.DelayMilliseconds = delayMilliseconds;
        }

        ApplyLiveMixAudibilityLocked();
    }

    private void RebuildLiveMixProcessorsLocked()
    {
        var sampleRate = Math.Max(8000, _activeSampleRate);
        _liveMixChannels = _configuredLiveMixChannels
            .Select(channel =>
            {
                var processor = new VoiceSampleProcessor(channel.ProcessorSettings, sampleRate);
                var preserveStereo = channel.InputChannelMode == InputChannelMode.StereoPair;
                var deviceKey = CreateInputDeviceKey(channel.DeviceNumber, channel.EndpointId, channel.Backend);
                var volumeLinear = MixBusProcessor.PercentToLinear(channel.VolumePercent);
                LiveMicBlockSampleProvider? sourceProvider = null;
                LiveStereoBlockSampleProvider? stereoSourceProvider = null;
                VoiceProcessorSampleProvider? dspProvider = null;
                StereoVoiceProcessorSampleProvider? stereoDspProvider = null;
                StereoPanSampleProvider? panProvider = null;
                StereoBalanceSampleProvider? stereoBalanceProvider = null;
                AudioDelayLine? delayLine = null;
                AudioStereoDelayLine? stereoDelayLine = null;
                ISampleProvider programProvider;
                VolumeSampleProvider volumeProvider;

                if (preserveStereo)
                {
                    stereoSourceProvider = new LiveStereoBlockSampleProvider(sampleRate);
                    stereoDspProvider = new StereoVoiceProcessorSampleProvider(
                        stereoSourceProvider,
                        processor,
                        new VoiceSampleProcessor(channel.ProcessorSettings, sampleRate));
                    volumeProvider = new VolumeSampleProvider(stereoDspProvider)
                    {
                        Volume = 0f
                    };
                    stereoBalanceProvider = new StereoBalanceSampleProvider(volumeProvider)
                    {
                        Balance = Math.Clamp(channel.Pan / 100d, -1d, 1d)
                    };
                    stereoDelayLine = new AudioStereoDelayLine(sampleRate, 250d);
                    programProvider = stereoBalanceProvider;
                }
                else
                {
                    sourceProvider = new LiveMicBlockSampleProvider(sampleRate);
                    dspProvider = new VoiceProcessorSampleProvider(sourceProvider, processor);
                    volumeProvider = new VolumeSampleProvider(dspProvider)
                    {
                        Volume = 0f
                    };
                    panProvider = new StereoPanSampleProvider(volumeProvider)
                    {
                        Pan = Math.Clamp(channel.Pan / 100d, -1d, 1d)
                    };
                    delayLine = new AudioDelayLine(sampleRate, 250d);
                    programProvider = panProvider;
                }

                var programMeterProvider = new NaudioPeakMeterSampleProvider(
                    programProvider,
                    GetProgramMeterSamplesPerNotification(sampleRate));

                return new LiveMicChannelRuntime
                {
                    ChannelNumber = channel.ChannelNumber,
                    DeviceNumber = channel.DeviceNumber,
                    EndpointId = channel.EndpointId,
                    Backend = channel.Backend,
                    DeviceKey = deviceKey,
                    InputChannelMode = channel.InputChannelMode,
                    ProcessorSettings = channel.ProcessorSettings,
                    VolumeLinear = volumeLinear,
                    InputGainLinear = DbToLinear(Math.Clamp(channel.InputGainDb, -24d, 24d)),
                    IsEnabled = channel.IsEnabled,
                    IsMuted = channel.IsMuted,
                    IsSoloed = channel.IsSoloed,
                    IsPolarityInverted = channel.IsPolarityInverted,
                    DelayMilliseconds = Math.Clamp(channel.DelayMilliseconds, 0d, 250d),
                    Processor = processor,
                    PreservesStereo = preserveStereo,
                    SourceProvider = sourceProvider,
                    StereoSourceProvider = stereoSourceProvider,
                    DspProvider = dspProvider,
                    StereoDspProvider = stereoDspProvider,
                    VolumeProvider = volumeProvider,
                    PanProvider = panProvider,
                    StereoBalanceProvider = stereoBalanceProvider,
                    DelayLine = delayLine,
                    StereoDelayLine = stereoDelayLine,
                    ProgramProvider = programMeterProvider,
                    ProgramMeterProvider = programMeterProvider,
                    Analyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate),
                    RawAnalyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate),
                    SyncBuffer = deviceKey == CreateCurrentInputDeviceKey()
                        ? null
                        : new AudioSyncBuffer(sampleRate, AuxiliaryCaptureTargetLatency, AuxiliaryCaptureMaximumLatency, preserveStereo ? 2 : 1)
                };
            })
            .ToArray();
        var programMixer = new LiveProgramMixBus(sampleRate);
        foreach (var channel in _liveMixChannels.Where(channel => channel.IsEnabled))
        {
            programMixer.AddMicInput(channel.ProgramProvider);
        }

        _programMixer = programMixer;
        ApplyLiveMixAudibilityLocked();
        _mixBusProcessor.Reset();
    }

    private void ApplyLiveMixAudibilityLocked()
    {
        var hasSolo = _liveMixChannels.Any(channel => channel.IsEnabled && channel.IsSoloed);
        foreach (var channel in _liveMixChannels)
        {
            channel.VolumeProvider.Volume = (float)LiveMixAudibility.ResolveVolume(
                channel.VolumeLinear,
                channel.IsEnabled,
                channel.IsMuted,
                channel.IsSoloed,
                hasSolo);
        }
    }

    public static IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>();
        for (var deviceNumber = 0; deviceNumber < WaveIn.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveIn.GetCapabilities(deviceNumber);
            devices.Add(new AudioInputDevice(deviceNumber, capabilities.ProductName, capabilities.Channels));
        }

        AddAsioInputDevices(devices);
        devices.Add(AudioInputDevice.CreateSystemAudioLoopback());
        devices.Add(AudioInputDevice.CreateStereoTestTone());
        devices.AddRange(CoreAudioSessionCatalog.GetProcessLoopbackInputDevices());
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

        AddAsioOutputDevices(devices);
        return devices;
    }

    public static string? GetDefaultPlaybackEndpointId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return endpoint.ID;
        }
        catch
        {
            return null;
        }
    }

    public static AudioDeviceFormat? TryGetInputDeviceFormat(AudioInputDevice device)
    {
        if (device.IsSystemAudioLoopback)
        {
            return TryGetSystemAudioLoopbackFormat();
        }

        if (device.IsStereoTestTone)
        {
            return new AudioDeviceFormat(48000, 2, 32);
        }

        if (device.IsAsio)
        {
            return TryGetAsioInputDeviceFormat(device);
        }

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

    private static AudioDeviceFormat? TryGetSystemAudioLoopbackFormat()
    {
        try
        {
            using var endpoint = WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
            var format = endpoint.AudioClient.MixFormat;
            return new AudioDeviceFormat(format.SampleRate, format.Channels, format.BitsPerSample);
        }
        catch
        {
        }

        return null;
    }

    public static AudioDeviceFormat? TryGetOutputDeviceFormat(AudioOutputDevice device)
    {
        if (device.IsAsio)
        {
            return null;
        }

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

    public static IReadOnlyList<CoreAudioSessionSnapshot> GetOutputAudioSessions(AudioOutputDevice? device)
    {
        return CoreAudioSessionCatalog.GetRenderSessions(device);
    }

    public static bool TrySetOutputAudioSessionControls(
        AudioOutputDevice? device,
        string sessionInstanceIdentifier,
        string sessionIdentifier,
        float? volume,
        bool? isMuted,
        out string status)
    {
        return CoreAudioSessionCatalog.TrySetRenderSessionControls(
            device,
            sessionInstanceIdentifier,
            sessionIdentifier,
            volume,
            isMuted,
            out status);
    }

    public static bool TrySetOutputAudioSessionControls(
        AudioOutputDevice? device,
        IReadOnlyList<CoreAudioSessionControlTarget> targets,
        float? volume,
        bool? isMuted,
        out string status)
    {
        return CoreAudioSessionCatalog.TrySetRenderSessionControls(
            device,
            targets,
            volume,
            isMuted,
            out status);
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

    private static void AddAsioInputDevices(List<AudioInputDevice> devices)
    {
        try
        {
            if (!AsioOut.isSupported())
            {
                return;
            }

            foreach (var driverName in AsioOut.GetDriverNames())
            {
                if (string.IsNullOrWhiteSpace(driverName))
                {
                    continue;
                }

                var inputChannels = TryGetAsioInputChannelCount(driverName, out var channelCount)
                    ? channelCount
                    : 2;
                if (inputChannels <= 0)
                {
                    continue;
                }

                devices.Add(new AudioInputDevice(
                    AudioInputDevice.AsioInputDeviceNumber,
                    $"ASIO: {driverName}",
                    Math.Clamp(inputChannels, 1, MaximumAsioInputChannels),
                    CreateAsioEndpointId(driverName),
                    AudioInputBackend.Asio));
            }
        }
        catch
        {
        }
    }

    private static bool TryGetAsioInputChannelCount(string driverName, out int channelCount)
    {
        channelCount = 0;
        try
        {
            using var asio = new AsioOut(driverName);
            channelCount = Math.Clamp(asio.DriverInputChannelCount, 0, MaximumAsioInputChannels);
            return channelCount > 0;
        }
        catch
        {
            return false;
        }
    }

    private static AudioDeviceFormat? TryGetAsioInputDeviceFormat(AudioInputDevice device)
    {
        if (!TryGetAsioDriverName(device.EndpointId, out var driverName))
        {
            return null;
        }

        try
        {
            using var asio = new AsioOut(driverName);
            var sampleRate = PreferredSampleRates.FirstOrDefault(rate => IsAsioSampleRateSupported(asio, rate));
            if (sampleRate <= 0)
            {
                sampleRate = DefaultSampleRate;
            }

            var channels = Math.Clamp(asio.DriverInputChannelCount, 1, MaximumAsioInputChannels);
            return new AudioDeviceFormat(sampleRate, channels, 32);
        }
        catch
        {
            var fallbackChannels = Math.Clamp(device.MaximumInputChannels, 1, MaximumAsioInputChannels);
            return new AudioDeviceFormat(DefaultSampleRate, fallbackChannels, 32);
        }
    }

    private static bool IsAsioSampleRateSupported(AsioOut asio, int sampleRate)
    {
        try
        {
            return asio.IsSampleRateSupported(sampleRate);
        }
        catch
        {
            return false;
        }
    }

    private static string CreateInputDeviceKey(int deviceNumber, string? endpointId, AudioInputBackend backend)
    {
        return !string.IsNullOrWhiteSpace(endpointId)
            ? $"{backend}:{endpointId}"
            : $"{backend}:device:{deviceNumber}";
    }

    private string CreateCurrentInputDeviceKey()
    {
        return CreateInputDeviceKey(_currentDeviceNumber, _currentInputEndpointId, _currentInputBackend);
    }
    private static void AddWaveOutDevices(List<AudioOutputDevice> devices)
    {
        for (var deviceNumber = 0; deviceNumber < WaveOut.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveOut.GetCapabilities(deviceNumber);
            devices.Add(new AudioOutputDevice(deviceNumber, capabilities.ProductName));
        }
    }

    private static void AddAsioOutputDevices(List<AudioOutputDevice> devices)
    {
        try
        {
            if (!AsioOut.isSupported())
            {
                return;
            }

            foreach (var driverName in AsioOut.GetDriverNames())
            {
                if (string.IsNullOrWhiteSpace(driverName))
                {
                    continue;
                }

                devices.Add(new AudioOutputDevice(
                    -1,
                    $"ASIO: {driverName}",
                    CreateAsioEndpointId(driverName),
                    AudioOutputBackend.Asio));
            }
        }
        catch
        {
        }
    }

    public static string CreateAsioEndpointId(string driverName)
    {
        return AsioEndpointPrefix + (driverName ?? string.Empty);
    }

    public static bool TryGetAsioDriverName(string? endpointId, out string driverName)
    {
        driverName = string.Empty;
        if (string.IsNullOrWhiteSpace(endpointId)
            || !endpointId.StartsWith(AsioEndpointPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        driverName = endpointId[AsioEndpointPrefix.Length..];
        return !string.IsNullOrWhiteSpace(driverName);
    }

    public static bool TryShowAsioControlPanel(string? endpointId, out string status)
    {
        if (!TryGetAsioDriverName(endpointId, out var driverName))
        {
            status = "Select an ASIO device first.";
            return false;
        }

        using var dispatcher = new StaThreadDispatcher($"Jericho ASIO Control Panel ({driverName})");
        try
        {
            dispatcher.Invoke(() =>
            {
                using var asio = new AsioOut(driverName);
                asio.ShowControlPanel();
            });
            status = $"Opened ASIO control panel for {driverName}.";
            return true;
        }
        catch (Exception ex)
        {
            status = $"ASIO control panel unavailable for {driverName}: {ex.Message}";
            return false;
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
        Start(deviceNumber, null, AudioInputBackend.Windows, processorSettings, inputChannelMode);
    }

    public void Start(AudioInputDevice device, VoiceProcessorSettings? processorSettings = null, InputChannelMode inputChannelMode = InputChannelMode.MonoSum)
    {
        ArgumentNullException.ThrowIfNull(device);
        Start(device.DeviceNumber, device.EndpointId, device.Backend, processorSettings, inputChannelMode);
    }

    private void Start(
        int deviceNumber,
        string? endpointId,
        AudioInputBackend backend,
        VoiceProcessorSettings? processorSettings,
        InputChannelMode inputChannelMode)
    {
        if (_capture is not null)
        {
            return;
        }

        var currentDeviceKey = CreateCurrentInputDeviceKey();
        var requestedDeviceKey = CreateInputDeviceKey(deviceNumber, endpointId, backend);
        if (!string.Equals(currentDeviceKey, requestedDeviceKey, StringComparison.Ordinal))
        {
            System.Threading.Volatile.Write(ref _preferWaveInCapture, 0);
        }

        _currentDeviceNumber = deviceNumber;
        _currentInputEndpointId = endpointId;
        _currentInputBackend = backend;
        _currentProcessorSettings = processorSettings;
        _currentInputChannelMode = inputChannelMode;
        _autoRecoverCapture = true;
        _isStoppingCapture = false;
        _inputChannelMode = inputChannelMode;
        EnterLowLatencyRuntimeMode();
        IWaveIn capture;
        try
        {
            capture = StartCapture(deviceNumber, endpointId, backend, CaptureDataAvailable, CaptureRecordingStopped);
        }
        catch
        {
            RestoreRuntimeMode();
            throw;
        }

        _activeSampleRate = capture.WaveFormat.SampleRate;
        _captureBackendDescription = DescribeCaptureBackend(capture);
        _processedAnalyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        _rawAnalyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        _input1Analyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        _input2Analyzer = new SpectrumAnalyzer(SpectrumDisplaySampleRate);
        lock (_liveMixLock)
        {
            RebuildLiveMixProcessorsLocked();
        }

        ResetSampleBuffers();
        ResetSpectrumResampleBuffers();
        System.Threading.Interlocked.Increment(ref _spectrumAnalysisVersion);
        System.Threading.Volatile.Write(ref _nextSpectrumAnalysisTimestamp, 0);
        ResetAudioStabilityCounters();
        _voiceProcessor = processorSettings is null ? null : new VoiceSampleProcessor(processorSettings, _activeSampleRate);
        System.Threading.Volatile.Write(ref _audioStreamStartedTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
        _capture = capture;
        StartAdditionalCaptures();
        if (IsProcessedOutputEnabledVolatile())
        {
            RestartProcessedOutput();
        }
    }
    public void Stop()
    {
        _autoRecoverCapture = false;
        StopProcessedAudioRecording();
        StopAdditionalCaptures();
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

            ReleaseCapture(capture);
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
        _processedOutputDeviceName = outputDevice.Name;
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

    public void ConfigureWasapiOutput(WasapiOutputSettings settings)
    {
        var normalized = settings with
        {
            CustomLatencyMilliseconds = WasapiOutputSettings.ClampCustomLatency(settings.CustomLatencyMilliseconds)
        };
        if (normalized == _wasapiOutputSettings)
        {
            return;
        }

        _wasapiOutputSettings = normalized;
        if (!IsProcessedOutputEnabledVolatile() || _processedOutput is null || IsProcessedOutputAsio())
        {
            return;
        }

        try
        {
            RestartProcessedOutput();
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
        StopProcessedAudioRecording();
        StopProcessedOutput();
        Stop();
        ReleaseCapture();
    }

    public void StartProcessedAudioRecording(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a valid audio recording path.", nameof(path));
        }

        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        lock (_processedRecordingLock)
        {
            if (_processedRecordingWriter is not null)
            {
                throw new InvalidOperationException("Processed audio recording is already running.");
            }

            var sampleRate = Math.Max(8000, _activeSampleRate);
            var recordingChannelCount = GetProcessedRecordingChannelCount(_processedRecordingSource);
            _processedRecordingWriter = new WaveFileWriter(
                path,
                WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, recordingChannelCount));
            _processedRecordingPath = path;
            _isProcessedRecordingPaused = false;
        }
    }

    public void PauseProcessedAudioRecording()
    {
        lock (_processedRecordingLock)
        {
            if (_processedRecordingWriter is not null)
            {
                _isProcessedRecordingPaused = true;
            }
        }
    }

    public void ResumeProcessedAudioRecording()
    {
        lock (_processedRecordingLock)
        {
            if (_processedRecordingWriter is not null)
            {
                _isProcessedRecordingPaused = false;
            }
        }
    }

    public string? StopProcessedAudioRecording()
    {
        lock (_processedRecordingLock)
        {
            return StopProcessedAudioRecordingLocked();
        }
    }

    public void RestartCurrentCapture()
    {
        RestartCurrentCapture(TimeSpan.FromSeconds(2));
    }

    public void RestartCurrentCapture(TimeSpan stopTimeout)
    {
        RestartCapture(
            _currentDeviceNumber,
            _currentInputEndpointId,
            _currentInputBackend,
            _currentProcessorSettings,
            _currentInputChannelMode,
            stopTimeout);
    }

    public void RestartCapture(int deviceNumber, VoiceProcessorSettings? processorSettings, InputChannelMode inputChannelMode, TimeSpan stopTimeout)
    {
        RestartCapture(deviceNumber, null, AudioInputBackend.Windows, processorSettings, inputChannelMode, stopTimeout);
    }

    public void RestartCapture(AudioInputDevice device, VoiceProcessorSettings? processorSettings, InputChannelMode inputChannelMode, TimeSpan stopTimeout)
    {
        ArgumentNullException.ThrowIfNull(device);

        RestartCapture(device.DeviceNumber, device.EndpointId, device.Backend, processorSettings, inputChannelMode, stopTimeout);
    }

    private void RestartCapture(
        int deviceNumber,
        string? endpointId,
        AudioInputBackend backend,
        VoiceProcessorSettings? processorSettings,
        InputChannelMode inputChannelMode,
        TimeSpan stopTimeout)
    {
        var stopTask = Task.Run(Stop);
        var stoppedCleanly = stopTask.Wait(stopTimeout);
        if (!stoppedCleanly)
        {
            AbandonCurrentCaptureForDriverReset();
            StreamStatusChanged?.Invoke(this, "Audio driver is still releasing the old stream; opening a fresh stream.");
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaximumCaptureRecoveryAttempts + 2; attempt++)
        {
            try
            {
                Start(deviceNumber, endpointId, backend, processorSettings, inputChannelMode);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        throw lastException ?? new InvalidOperationException("Could not reopen microphone capture.");
    }

    private void StartAdditionalCaptures()
    {
        LiveMicChannelRuntime[] liveMixChannels;
        lock (_liveMixLock)
        {
            liveMixChannels = _liveMixChannels;
        }

        var currentDeviceKey = CreateCurrentInputDeviceKey();
        var captureDevices = liveMixChannels
            .Where(channel => channel.IsEnabled
                && !string.Equals(channel.DeviceKey, currentDeviceKey, StringComparison.Ordinal)
                && channel.SyncBuffer is not null)
            .GroupBy(channel => channel.DeviceKey)
            .Select(group => group.First())
            .OrderBy(channel => channel.DeviceKey, StringComparer.Ordinal)
            .ToArray();
        var deviceKeys = captureDevices.Select(channel => channel.DeviceKey).ToArray();

        lock (_additionalCaptureLock)
        {
            var currentDeviceKeys = _additionalCaptures.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            if (currentDeviceKeys.SequenceEqual(deviceKeys, StringComparer.Ordinal))
            {
                return;
            }
        }

        StopAdditionalCaptures();
        foreach (var device in captureDevices)
        {
            var runtime = new AdditionalCaptureRuntime(
                this,
                device.DeviceNumber,
                device.EndpointId,
                device.Backend,
                device.DeviceKey);
            try
            {
                var capture = StartCapture(device.DeviceNumber, device.EndpointId, device.Backend, runtime.DataAvailable, runtime.RecordingStopped);
                runtime.Capture = capture;
                runtime.BackendDescription = DescribeCaptureBackend(capture);
                lock (_additionalCaptureLock)
                {
                    _additionalCaptures[device.DeviceKey] = runtime;
                }

                ReportStreamStatus($"{DescribeInputDevice(device.DeviceNumber, device.EndpointId, device.Backend)} listening via {runtime.BackendDescription}.");
            }
            catch (Exception ex)
            {
                runtime.Dispose();
                ReportStreamStatus($"{DescribeInputDevice(device.DeviceNumber, device.EndpointId, device.Backend)} unavailable: {ex.Message}");
            }
        }
    }

    private static string DescribeInputDevice(int deviceNumber, string? endpointId, AudioInputBackend backend)
    {
        if (backend == AudioInputBackend.Asio && TryGetAsioDriverName(endpointId, out var driverName))
        {
            return $"ASIO input {driverName}";
        }

        if (backend == AudioInputBackend.SignalGenerator
            || deviceNumber == AudioInputDevice.StereoTestToneDeviceNumber)
        {
            return "NAudio stereo test tone";
        }

        var isProcessLoopback = AudioInputDevice.TryGetProcessLoopbackTargetProcessId(deviceNumber, endpointId, out var processId);
        if (backend == AudioInputBackend.ProcessLoopback || isProcessLoopback)
        {
            return $"App audio PID {processId}";
        }

        return IsSystemAudioLoopbackDeviceNumber(deviceNumber)
            ? "Computer audio loopback"
            : $"Aux mic device {deviceNumber + 1}";
    }
    public void ConfigureProcessedRecordingSource(ProcessedRecordingSource source, int selectedChannelNumber)
    {
        lock (_processedRecordingLock)
        {
            _processedRecordingSource = source;
            _processedRecordingSelectedChannelNumber = Math.Max(1, selectedChannelNumber);
        }
    }

    private void StopAdditionalCaptures()
    {
        AdditionalCaptureRuntime[] captures;
        lock (_additionalCaptureLock)
        {
            captures = _additionalCaptures.Values.ToArray();
            _additionalCaptures.Clear();
        }

        foreach (var capture in captures)
        {
            capture.Dispose();
        }
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

        ProcessCaptureSamples(
            buffer,
            captureFormat,
            samples,
            out var rawSamples,
            out var analyzerSamples,
            out var programOutputSamples,
            out var programOutputChannelCount,
            out var telemetry);

        AddProcessedOutputSamples(programOutputSamples, programOutputChannelCount);
        var recordingSamples = ResolveProcessedRecordingSamples(programOutputSamples, programOutputChannelCount, out var recordingChannelCount);
        WriteProcessedRecordingSamples(recordingSamples, recordingChannelCount);
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
        const bool includeWaveformSamples = false;
        var rawSamplesSnapshot = includeWaveformSamples
            ? rawSamples.ToArray()
            : RentAndCopySamples(rawSamples);
        var processedSamplesSnapshot = includeWaveformSamples
            ? analyzerSamples.ToArray()
            : RentAndCopySamples(analyzerSamples);
        var includeStereoInputAnalysis = captureFormat.Channels > 1
            && System.Threading.Volatile.Read(ref _stereoInputAnalysisEnabled) != 0;
        var (input1SamplesSnapshot, input2SamplesSnapshot) = includeStereoInputAnalysis
            ? ExtractStereoChannelSamples(buffer, captureFormat)
            : ([], []);
        var microphoneSamplesSnapshot = CreateMicrophoneSpectrumSampleSnapshots();
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
            MicrophoneSamples = microphoneSamplesSnapshot,
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
                        workItem.MicrophoneSamples,
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

                    foreach (var microphoneSample in workItem.MicrophoneSamples)
                    {
                        ArrayPool<float>.Shared.Return(microphoneSample.RawSamples);
                        ArrayPool<float>.Shared.Return(microphoneSample.ProcessedSamples);
                    }

                    System.Threading.Volatile.Write(ref workItem.Service._spectrumAnalysisInProgress, 0);
                }
            },
            workItem,
            preferLocal: false);
    }

    private void AdditionalCaptureDataAvailable(AdditionalCaptureRuntime runtime, WaveInEventArgs e)
    {
        ConfigureAudioCallbackThread();
        var capture = runtime.Capture;
        if (capture is null || e.BytesRecorded == 0)
        {
            return;
        }

        LiveMicChannelRuntime[] liveMixChannels;
        lock (_liveMixLock)
        {
            liveMixChannels = _liveMixChannels;
        }

        var channels = liveMixChannels
            .Where(channel => channel.IsEnabled
                && string.Equals(channel.DeviceKey, runtime.DeviceKey, StringComparison.Ordinal)
                && channel.SyncBuffer is not null)
            .ToArray();
        if (channels.Length == 0)
        {
            return;
        }

        var buffer = e.Buffer.AsSpan(0, e.BytesRecorded);
        var captureFormat = capture.WaveFormat;
        var frameCount = GetCaptureFrameCount(buffer.Length, captureFormat);
        if (frameCount <= 0)
        {
            return;
        }

        var needsStereoScratch = channels.Any(channel => channel.PreservesStereo);
        var scratchSampleCount = needsStereoScratch ? frameCount * 2 : frameCount;
        if (runtime.ScratchSamples.Length < scratchSampleCount)
        {
            runtime.ScratchSamples = new float[scratchSampleCount];
        }

        foreach (var channel in channels)
        {
            if (channel.PreservesStereo)
            {
                var stereoScratch = runtime.ScratchSamples.AsSpan(0, frameCount * 2);
                FillStereoSamples(buffer, captureFormat, channel.InputChannelMode, stereoScratch);
                channel.SyncBuffer!.Write(stereoScratch, captureFormat.SampleRate);
            }
            else
            {
                var monoScratch = runtime.ScratchSamples.AsSpan(0, frameCount);
                FillMonoSamples(buffer, captureFormat, channel.InputChannelMode, monoScratch);
                channel.SyncBuffer!.Write(monoScratch, captureFormat.SampleRate);
            }
        }
    }

    private void ProcessCaptureSamples(
        ReadOnlySpan<byte> buffer,
        WaveFormat captureFormat,
        ReadOnlySpan<float> fallbackRawSamples,
        out ReadOnlySpan<float> rawSamples,
        out ReadOnlySpan<float> processedSamples,
        out ReadOnlySpan<float> programOutputSamples,
        out int programOutputChannelCount,
        out VoiceProcessingTelemetry telemetry)
    {
        LiveMicChannelRuntime[] liveMixChannels;
        LiveProgramMixBus? programMixer;
        MixBusSettings mixBusSettings;
        lock (_liveMixLock)
        {
            liveMixChannels = _liveMixChannels;
            programMixer = _programMixer;
            mixBusSettings = _mixBusSettings;
        }

        if (liveMixChannels.Length == 0)
        {
            var voiceProcessor = _voiceProcessor;
            processedSamples = fallbackRawSamples;
            if (voiceProcessor is not null)
            {
                EnsureProcessedSampleBuffer(fallbackRawSamples.Length);
                voiceProcessor.Process(fallbackRawSamples, _processedSamples.AsSpan(0, fallbackRawSamples.Length));
                processedSamples = _processedSamples.AsSpan(0, fallbackRawSamples.Length);
                telemetry = voiceProcessor.Telemetry.Snapshot();
            }
            else
            {
                telemetry = _emptyTelemetry.Snapshot();
            }

            telemetry.ProgramPeakLevel = GetPeakLevel(processedSamples);
            telemetry.ProgramRmsLevel = GetRmsLevel(processedSamples);
            telemetry.MasterLimiterReductionDb = 0d;
            telemetry.MasterNormalizeGain = 1d;
            rawSamples = fallbackRawSamples;
            programOutputSamples = processedSamples;
            programOutputChannelCount = 1;
            return;
        }

        EnsureMixBuffers(fallbackRawSamples.Length);
        var stereoSampleCount = checked(fallbackRawSamples.Length * 2);
        var mixSamples = _mixBusSamples.AsSpan(0, stereoSampleCount);
        mixSamples.Clear();
        var rawPrimarySamples = _monoSamples.AsSpan(0, fallbackRawSamples.Length);
        var hasRawPrimarySamples = false;
        var includedChannelCount = 0;

        foreach (var channel in liveMixChannels)
        {
            if (!channel.IsEnabled)
            {
                continue;
            }

            var channelInputSamples = _mixChannelInputSamples.AsSpan(0, fallbackRawSamples.Length);
            if (channel.PreservesStereo)
            {
                var channelStereoSamples = _mixChannelStereoInputSamples.AsSpan(0, stereoSampleCount);
                if (string.Equals(channel.DeviceKey, CreateCurrentInputDeviceKey(), StringComparison.Ordinal))
                {
                    FillStereoSamples(buffer, captureFormat, channel.InputChannelMode, channelStereoSamples);
                }
                else if (channel.SyncBuffer is not null)
                {
                    channel.SyncBuffer.ReadAligned(channelStereoSamples);
                }
                else
                {
                    channelStereoSamples.Clear();
                }

                DownmixStereoToMono(channelStereoSamples, channelInputSamples);
                if (!hasRawPrimarySamples)
                {
                    channelInputSamples.CopyTo(rawPrimarySamples);
                    hasRawPrimarySamples = true;
                }

                CaptureLastMicSamples(channel, channelInputSamples, ReadOnlySpan<float>.Empty);
                ApplyPreDspLiveStereoChannelControls(channel, channelStereoSamples);
                channel.StereoSourceProvider?.SetBlock(channelStereoSamples);
                includedChannelCount++;
                continue;
            }

            if (string.Equals(channel.DeviceKey, CreateCurrentInputDeviceKey(), StringComparison.Ordinal))
            {
                FillMonoSamples(buffer, captureFormat, channel.InputChannelMode, channelInputSamples);
            }
            else if (channel.SyncBuffer is not null)
            {
                channel.SyncBuffer.ReadAligned(channelInputSamples);
            }
            else
            {
                channelInputSamples.Clear();
            }

            if (!hasRawPrimarySamples)
            {
                channelInputSamples.CopyTo(rawPrimarySamples);
                hasRawPrimarySamples = true;
            }

            CaptureLastMicSamples(channel, channelInputSamples, ReadOnlySpan<float>.Empty);
            ApplyPreDspLiveChannelControls(channel, channelInputSamples);
            channel.SourceProvider?.SetBlock(channelInputSamples);
            includedChannelCount++;
        }

        var mixedRead = includedChannelCount > 0 && programMixer is not null
            ? programMixer.Read(_mixBusSamples, 0, stereoSampleCount)
            : 0;
        var validMixedRead = Math.Clamp(mixedRead, 0, stereoSampleCount);
        if (validMixedRead < stereoSampleCount)
        {
            _mixBusSamples.AsSpan(validMixedRead, stereoSampleCount - validMixedRead).Clear();
        }

        VoiceProcessingTelemetry? primaryTelemetry = null;
        foreach (var channel in liveMixChannels)
        {
            if (!channel.IsEnabled)
            {
                continue;
            }

            if (channel.DspProvider is not null)
            {
                CaptureLastMicSamples(channel, ReadOnlySpan<float>.Empty, channel.DspProvider.LastProcessedSamples);
                primaryTelemetry ??= channel.DspProvider.Processor.Telemetry.Snapshot();
            }
            else if (channel.StereoDspProvider is not null)
            {
                var stereoProcessedSamples = channel.StereoDspProvider.LastProcessedSamples;
                var processedSampleCount = Math.Min(fallbackRawSamples.Length, stereoProcessedSamples.Length / 2);
                var processedMonoSamples = _mixChannelInputSamples.AsSpan(0, processedSampleCount);
                DownmixStereoToMono(stereoProcessedSamples, processedMonoSamples);
                CaptureLastMicSamples(channel, ReadOnlySpan<float>.Empty, processedMonoSamples);
                primaryTelemetry ??= channel.StereoDspProvider.LeftProcessor.Telemetry.Snapshot();
            }
        }

        if (includedChannelCount == 0)
        {
            fallbackRawSamples.CopyTo(rawPrimarySamples);
            hasRawPrimarySamples = true;
        }

        var outputSamples = _mixOutputSamples.AsSpan(0, stereoSampleCount);
        _mixBusProcessor.Process(mixSamples, outputSamples, mixBusSettings);
        var mixBusTelemetry = _mixBusProcessor.LastTelemetry;
        var analyzerOutputSamples = _mixOutputMonoSamples.AsSpan(0, fallbackRawSamples.Length);
        DownmixStereoToMono(outputSamples, analyzerOutputSamples);
        rawSamples = hasRawPrimarySamples ? rawPrimarySamples : fallbackRawSamples;
        processedSamples = analyzerOutputSamples;
        programOutputSamples = outputSamples;
        programOutputChannelCount = 2;
        telemetry = (primaryTelemetry ?? _emptyTelemetry).Snapshot();
        telemetry.ProgramPeakLevel = mixBusTelemetry.PeakLevel;
        telemetry.ProgramRmsLevel = mixBusTelemetry.RmsLevel;
        telemetry.MasterLimiterReductionDb = mixBusTelemetry.LimiterReductionDb;
        telemetry.MasterNormalizeGain = mixBusTelemetry.NormalizeGain;
    }

    private static void CaptureLastMicSamples(
        LiveMicChannelRuntime channel,
        ReadOnlySpan<float> rawSamples,
        ReadOnlySpan<float> processedSamples)
    {
        if (!rawSamples.IsEmpty)
        {
            if (channel.LastRawSamples.Length < rawSamples.Length)
            {
                channel.LastRawSamples = new float[rawSamples.Length];
            }

            rawSamples.CopyTo(channel.LastRawSamples);
            channel.LastRawSampleCount = rawSamples.Length;
        }

        if (!processedSamples.IsEmpty)
        {
            if (channel.LastProcessedSamples.Length < processedSamples.Length)
            {
                channel.LastProcessedSamples = new float[processedSamples.Length];
            }

            processedSamples.CopyTo(channel.LastProcessedSamples);
            channel.LastProcessedSampleCount = processedSamples.Length;
        }
    }

    private static void ApplyPreDspLiveChannelControls(LiveMicChannelRuntime channel, Span<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        channel.DelayLine?.Process(samples, channel.DelayMilliseconds);
        var gain = channel.InputGainLinear * (channel.IsPolarityInverted ? -1d : 1d);
        if (Math.Abs(gain - 1d) < 0.0001d)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Clamp(samples[i] * gain, -1d, 1d);
        }
    }

    private static void ApplyPreDspLiveStereoChannelControls(LiveMicChannelRuntime channel, Span<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        channel.StereoDelayLine?.Process(samples, channel.DelayMilliseconds);
        var gain = channel.InputGainLinear * (channel.IsPolarityInverted ? -1d : 1d);
        if (Math.Abs(gain - 1d) < 0.0001d)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Clamp(samples[i] * gain, -1d, 1d);
        }
    }

    private static void DownmixStereoToMono(ReadOnlySpan<float> stereoSamples, Span<float> monoSamples)
    {
        var frameCount = Math.Min(monoSamples.Length, stereoSamples.Length / 2);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var left = stereoSamples[frame * 2];
            var right = stereoSamples[frame * 2 + 1];
            monoSamples[frame] = (left + right) * 0.5f;
        }

        if (frameCount < monoSamples.Length)
        {
            monoSamples[frameCount..].Clear();
        }
    }

    private ReadOnlySpan<float> ResolveProcessedRecordingSamples(
        ReadOnlySpan<float> programOutputSamples,
        int programOutputChannelCount,
        out int recordingChannelCount)
    {
        ProcessedRecordingSource source;
        int selectedChannelNumber;
        lock (_processedRecordingLock)
        {
            source = _processedRecordingSource;
            selectedChannelNumber = _processedRecordingSelectedChannelNumber;
        }

        recordingChannelCount = GetProcessedRecordingChannelCount(source);
        if (source == ProcessedRecordingSource.ProgramMix)
        {
            return programOutputSamples;
        }

        LiveMicChannelRuntime? selectedChannel;
        lock (_liveMixLock)
        {
            selectedChannel = _liveMixChannels.FirstOrDefault(channel => channel.ChannelNumber == selectedChannelNumber && channel.IsEnabled);
        }

        if (selectedChannel is null)
        {
            return [];
        }

        var samples = source == ProcessedRecordingSource.SelectedMicRawBackup
            ? selectedChannel.LastRawSamples.AsSpan(0, Math.Clamp(selectedChannel.LastRawSampleCount, 0, selectedChannel.LastRawSamples.Length))
            : selectedChannel.LastProcessedSamples.AsSpan(0, Math.Clamp(selectedChannel.LastProcessedSampleCount, 0, selectedChannel.LastProcessedSamples.Length));
        if (samples.IsEmpty)
        {
            return [];
        }

        return samples;
    }

    private static int GetProcessedRecordingChannelCount(ProcessedRecordingSource source)
    {
        return source == ProcessedRecordingSource.ProgramMix ? 2 : 1;
    }

    private MicrophoneSpectrumSampleSnapshot[] CreateMicrophoneSpectrumSampleSnapshots()
    {
        LiveMicChannelRuntime[] liveMixChannels;
        lock (_liveMixLock)
        {
            liveMixChannels = _liveMixChannels;
        }

        if (liveMixChannels.Length == 0)
        {
            return [];
        }

        var snapshots = new List<MicrophoneSpectrumSampleSnapshot>(liveMixChannels.Length);
        foreach (var channel in liveMixChannels)
        {
            var rawSampleCount = Math.Clamp(channel.LastRawSampleCount, 0, channel.LastRawSamples.Length);
            var processedSampleCount = Math.Clamp(channel.LastProcessedSampleCount, 0, channel.LastProcessedSamples.Length);
            if (!channel.IsEnabled || rawSampleCount == 0 || processedSampleCount == 0)
            {
                continue;
            }

            snapshots.Add(new MicrophoneSpectrumSampleSnapshot
            {
                ChannelNumber = channel.ChannelNumber,
                Analyzer = channel.Analyzer,
                RawAnalyzer = channel.RawAnalyzer,
                RawSamples = RentAndCopySamples(channel.LastRawSamples.AsSpan(0, rawSampleCount)),
                RawSampleCount = rawSampleCount,
                ProcessedSamples = RentAndCopySamples(channel.LastProcessedSamples.AsSpan(0, processedSampleCount)),
                ProcessedSampleCount = processedSampleCount,
                SyncBufferedSamples = GetSyncFrameCount(channel, channel.SyncBuffer?.BufferedSamples ?? 0),
                SyncTargetLatencySamples = GetSyncFrameCount(channel, channel.SyncBuffer?.TargetLatencySamples ?? 0),
                SyncUnderflowCount = channel.SyncBuffer?.UnderflowCount ?? 0,
                SyncDriftTrimCount = channel.SyncBuffer?.DriftTrimCount ?? 0,
                SampleRate = Math.Max(8000, _activeSampleRate),
                MeteredPeakLevel = channel.ProgramMeterProvider.PeakLevel
            });
        }

        return snapshots.ToArray();
    }

    private static int GetProgramMeterSamplesPerNotification(int sampleRate)
    {
        var framesPerNotification = Math.Max(1, sampleRate / 50);
        return framesPerNotification * 2;
    }

    private static int GetSyncFrameCount(LiveMicChannelRuntime channel, int bufferedSamples)
    {
        return channel.PreservesStereo
            ? bufferedSamples / 2
            : bufferedSamples;
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
        MicrophoneSpectrumSampleSnapshot[] microphoneSamples,
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

        IReadOnlyList<MicrophoneSpectrumLine> microphoneLines = [];
        if (microphoneSamples.Length > 0)
        {
            var lines = new List<MicrophoneSpectrumLine>(microphoneSamples.Length);
            var microphoneSpectrumResampleBuffer = Array.Empty<float>();
            var microphoneRawSpectrumResampleBuffer = Array.Empty<float>();
            foreach (var microphoneSample in microphoneSamples.OrderBy(sample => sample.ChannelNumber))
            {
                var rawSampleSpan = microphoneSample.RawSamples.AsSpan(0, Math.Clamp(microphoneSample.RawSampleCount, 0, microphoneSample.RawSamples.Length));
                var processedSampleSpan = microphoneSample.ProcessedSamples.AsSpan(0, Math.Clamp(microphoneSample.ProcessedSampleCount, 0, microphoneSample.ProcessedSamples.Length));
                if (rawSampleSpan.Length == 0 || processedSampleSpan.Length == 0)
                {
                    continue;
                }

                var microphoneRawSpectrumSamples = ResampleForSpectrum(rawSampleSpan, sampleRate, ref microphoneRawSpectrumResampleBuffer);
                var microphoneProcessedSpectrumSamples = ResampleForSpectrum(processedSampleSpan, sampleRate, ref microphoneSpectrumResampleBuffer);
                var rawMicrophoneAnalysis = microphoneSample.RawAnalyzer.AnalyzeSamples(microphoneRawSpectrumSamples);
                var processedMicrophoneAnalysis = microphoneSample.Analyzer.AnalyzeSamples(microphoneProcessedSpectrumSamples);
                lines.Add(new MicrophoneSpectrumLine(
                    microphoneSample.ChannelNumber,
                    processedMicrophoneAnalysis.Magnitudes,
                    GetPeakLevel(processedSampleSpan),
                    rawMicrophoneAnalysis.Magnitudes,
                    GetPeakLevel(rawSampleSpan),
                    includeWaveformSamples ? processedSampleSpan.ToArray() : [],
                    includeWaveformSamples ? rawSampleSpan.ToArray() : [],
                    GetRmsLevel(processedSampleSpan),
                    GetRmsLevel(rawSampleSpan),
                    SamplesToMilliseconds(microphoneSample.SyncBufferedSamples, microphoneSample.SampleRate),
                    SamplesToMilliseconds(microphoneSample.SyncTargetLatencySamples, microphoneSample.SampleRate),
                    microphoneSample.SyncUnderflowCount,
                    microphoneSample.SyncDriftTrimCount,
                    microphoneSample.MeteredPeakLevel));
            }

            microphoneLines = lines;
        }

        var rawPeakLevel = GetPeakLevel(rawSamplesSpan);
        var rawRmsLevel = GetRmsLevel(rawSamplesSpan);
        var processedPeakLevel = telemetry.ProgramPeakLevel > 0d
            ? telemetry.ProgramPeakLevel
            : GetPeakLevel(processedSamplesSpan);
        var processedRmsLevel = telemetry.ProgramRmsLevel > 0d
            ? telemetry.ProgramRmsLevel
            : GetRmsLevel(processedSamplesSpan);
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
                input2Samples,
                microphoneLines,
                processedRmsLevel,
                rawRmsLevel));
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

    private static double GetRmsLevel(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0d;
        }

        var sumSquares = 0d;
        var count = 0;
        foreach (var sample in samples)
        {
            var value = Math.Clamp(float.IsFinite(sample) ? sample : 0f, -1f, 1f);
            sumSquares += value * value;
            count++;
        }

        return count == 0 ? 0d : Math.Sqrt(sumSquares / count);
    }

    private static double DbToLinear(double decibels)
    {
        return Math.Pow(10d, decibels / 20d);
    }

    private static double SamplesToMilliseconds(int samples, int sampleRate)
    {
        return Math.Max(0, samples) * 1000d / Math.Max(1, sampleRate);
    }

    private IWaveIn StartCapture(int deviceNumber)
    {
        return StartCapture(deviceNumber, null, AudioInputBackend.Windows, CaptureDataAvailable, CaptureRecordingStopped);
    }

    private IWaveIn StartCapture(
        int deviceNumber,
        string? endpointId,
        AudioInputBackend backend,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        if (backend == AudioInputBackend.Asio)
        {
            return StartAsioInputCapture(endpointId, dataAvailable, recordingStopped);
        }

        if (backend == AudioInputBackend.SignalGenerator
            || deviceNumber == AudioInputDevice.StereoTestToneDeviceNumber)
        {
            return StartSignalGeneratorCapture(dataAvailable, recordingStopped);
        }

        if (backend == AudioInputBackend.ProcessLoopback
            || AudioInputDevice.TryGetProcessLoopbackTargetProcessId(deviceNumber, endpointId, out _))
        {
            return StartProcessLoopbackCapture(deviceNumber, endpointId, dataAvailable, recordingStopped);
        }

        if (IsSystemAudioLoopbackDeviceNumber(deviceNumber))
        {
            return StartSystemAudioLoopbackCapture(dataAvailable, recordingStopped);
        }

        Exception? startException = null;
        Exception? wasapiException = null;
        if (!ShouldPreferWaveInCapture()
            && TryStartWasapiCapture(deviceNumber, dataAvailable, recordingStopped, out var wasapiCapture, out wasapiException))
        {
            return wasapiCapture;
        }

        startException = ShouldPreferWaveInCapture() ? null : wasapiException;
        foreach (var sampleRate in PreferredSampleRates)
        {
            foreach (var channelCount in new[] { 2, 1 })
            {
                foreach (var waveFormat in CreatePreferredCaptureFormats(sampleRate, channelCount))
                {
                    foreach (var bufferMilliseconds in CaptureBufferFallbackMilliseconds)
                    {
                        var capture = CreateWaveInCapture(deviceNumber, waveFormat, bufferMilliseconds, dataAvailable, recordingStopped);
                        try
                        {
                            capture.StartRecording();
                            return capture;
                        }
                        catch (Exception ex)
                        {
                            startException ??= ex;
                            DetachCaptureEvents(capture, dataAvailable, recordingStopped);
                            capture.Dispose();
                        }
                    }
                }
            }
        }

        throw startException ?? new InvalidOperationException("Could not start microphone capture.");
    }

    private IWaveIn StartSignalGeneratorCapture(
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        var capture = new SignalGeneratorCapture();
        AttachCaptureEvents(capture, dataAvailable, recordingStopped);
        try
        {
            capture.StartRecording();
            return capture;
        }
        catch
        {
            DetachCaptureEvents(capture, dataAvailable, recordingStopped);
            capture.Dispose();
            throw;
        }
    }

    private IWaveIn StartProcessLoopbackCapture(
        int deviceNumber,
        string? endpointId,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        if (!AudioInputDevice.TryGetProcessLoopbackTargetProcessId(deviceNumber, endpointId, out var processId))
        {
            throw new InvalidOperationException("Could not identify the selected app audio process.");
        }

        var capture = new ProcessLoopbackCapture(processId);
        AttachCaptureEvents(capture, dataAvailable, recordingStopped);
        try
        {
            capture.StartRecording();
            return capture;
        }
        catch
        {
            DetachCaptureEvents(capture, dataAvailable, recordingStopped);
            capture.Dispose();
            throw;
        }
    }

    private static bool IsSystemAudioLoopbackDeviceNumber(int deviceNumber)
    {
        return deviceNumber == AudioInputDevice.SystemAudioLoopbackDeviceNumber;
    }

    private IWaveIn StartSystemAudioLoopbackCapture(
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        var capture = new WasapiLoopbackCapture();
        AttachCaptureEvents(capture, dataAvailable, recordingStopped);
        try
        {
            capture.StartRecording();
            return capture;
        }
        catch
        {
            DetachCaptureEvents(capture, dataAvailable, recordingStopped);
            capture.Dispose();
            throw;
        }
    }

    private IWaveIn StartAsioInputCapture(
        string? endpointId,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        if (!TryGetAsioDriverName(endpointId, out var driverName))
        {
            throw new InvalidOperationException("Could not identify the selected ASIO input driver.");
        }

        var channelCount = TryGetAsioInputChannelCount(driverName, out var detectedChannels)
            ? detectedChannels
            : MaximumAsioInputChannels;
        channelCount = Math.Clamp(channelCount, 1, MaximumAsioInputChannels);
        Exception? startException = null;
        foreach (var sampleRate in PreferredAsioSampleRates)
        {
            var capture = new AsioInputCapture(driverName, sampleRate, channelCount);
            AttachCaptureEvents(capture, dataAvailable, recordingStopped);
            try
            {
                capture.StartRecording();
                return capture;
            }
            catch (Exception ex)
            {
                startException ??= ex;
                DetachCaptureEvents(capture, dataAvailable, recordingStopped);
                capture.Dispose();
            }
        }

        throw startException ?? new InvalidOperationException("Could not start ASIO input capture.");
    }
    private bool TryStartWasapiCapture(
        int deviceNumber,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped,
        out IWaveIn capture,
        out Exception? startException)
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
            AttachCaptureEvents(wasapiCapture, dataAvailable, recordingStopped);
            try
            {
                wasapiCapture.StartRecording();
                capture = wasapiCapture;
                return true;
            }
            catch (Exception ex)
            {
                startException ??= ex;
                DetachCaptureEvents(wasapiCapture, dataAvailable, recordingStopped);
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
            waveInDevice = new AudioInputDevice(deviceNumber, capabilities.ProductName, capabilities.Channels);
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

    private WaveInEvent CreateWaveInCapture(
        int deviceNumber,
        WaveFormat waveFormat,
        int bufferMilliseconds,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        var capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = waveFormat,
            BufferMilliseconds = bufferMilliseconds
        };
        AttachCaptureEvents(capture, dataAvailable, recordingStopped);
        return capture;
    }

    private void AttachCaptureEvents(IWaveIn capture)
    {
        AttachCaptureEvents(capture, CaptureDataAvailable, CaptureRecordingStopped);
    }

    private static void AttachCaptureEvents(
        IWaveIn capture,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        capture.DataAvailable += dataAvailable;
        capture.RecordingStopped += recordingStopped;
    }

    private void DetachCaptureEvents(IWaveIn capture)
    {
        DetachCaptureEvents(capture, CaptureDataAvailable, CaptureRecordingStopped);
    }

    private static void DetachCaptureEvents(
        IWaveIn capture,
        EventHandler<WaveInEventArgs> dataAvailable,
        EventHandler<StoppedEventArgs> recordingStopped)
    {
        capture.DataAvailable -= dataAvailable;
        capture.RecordingStopped -= recordingStopped;
    }

    private void StartProcessedOutput()
    {
        if (_processedOutput is not null || _capture is null)
        {
            return;
        }

        _processedOutputSampleRate = _activeSampleRate;
        var floatProvider = CreateProcessedOutputProvider(WaveFormat.CreateIeeeFloatWaveFormat(_activeSampleRate, 2));
        var pcmProvider = CreateProcessedOutputProvider(new WaveFormat(_activeSampleRate, 16, 2));
        foreach (var backend in ProcessedOutputRoutePlanner.CreateAttemptOrder(
            CanUseWaveOutProcessedOutputFallback(),
            IsProcessedOutputAsio()))
        {
            if (TryStartProcessedOutputBackend(
                backend,
                floatProvider,
                pcmProvider,
                out var provider,
                out var player,
                out var playbackProvider))
            {
                _processedOutputProvider = provider;
                _processedOutputPlaybackProvider = playbackProvider;
                _processedOutput = player;
                _processedOutputBackendDescription = DescribeProcessedOutputBackend(backend);
                ArmProcessedOutputRecoveryRamp();
                return;
            }
        }

        StopProcessedOutput();
        throw new InvalidOperationException("Could not start the selected processed output device.");
    }

    private bool TryStartProcessedOutputBackend(
        ProcessedOutputRouteBackend backend,
        BufferedWaveProvider floatProvider,
        BufferedWaveProvider pcmProvider,
        out BufferedWaveProvider provider,
        out IWavePlayer? player,
        out IWaveProvider? playbackProvider)
    {
        player = null;
        playbackProvider = null;
        provider = backend is ProcessedOutputRouteBackend.WasapiPcm
            or ProcessedOutputRouteBackend.WaveOutPcm
            or ProcessedOutputRouteBackend.DirectSoundPcm
            ? pcmProvider
            : floatProvider;
        return backend switch
        {
            ProcessedOutputRouteBackend.AsioFloat or ProcessedOutputRouteBackend.AsioPcm
                => TryStartAsioProcessedOutput(provider, out player, out playbackProvider),
            ProcessedOutputRouteBackend.WasapiFloat or ProcessedOutputRouteBackend.WasapiPcm
                => TryStartWasapiProcessedOutput(provider, out player, out playbackProvider),
            ProcessedOutputRouteBackend.WaveOutFloat or ProcessedOutputRouteBackend.WaveOutPcm
                => TryStartWaveOutProcessedOutput(provider, out player),
            ProcessedOutputRouteBackend.DirectSoundFloat or ProcessedOutputRouteBackend.DirectSoundPcm
                => TryStartDirectSoundProcessedOutput(provider, out player),
            _ => false
        };
    }

    private string DescribeProcessedOutputBackend(ProcessedOutputRouteBackend backend)
    {
        var wasapiName = CanUseWasapiProcessedOutput()
            ? $"{_wasapiOutputSettings.DisplayText} WASAPI endpoint"
            : $"{_wasapiOutputSettings.DisplayText} default WASAPI";
        return backend switch
        {
            ProcessedOutputRouteBackend.AsioFloat => "ASIO",
            ProcessedOutputRouteBackend.AsioPcm => "ASIO PCM",
            ProcessedOutputRouteBackend.WasapiFloat => wasapiName,
            ProcessedOutputRouteBackend.WasapiPcm => $"{wasapiName} PCM",
            ProcessedOutputRouteBackend.WaveOutFloat => "WaveOut",
            ProcessedOutputRouteBackend.WaveOutPcm => "WaveOut PCM",
            ProcessedOutputRouteBackend.DirectSoundFloat => "DirectSound",
            ProcessedOutputRouteBackend.DirectSoundPcm => "DirectSound PCM",
            _ => "unknown output"
        };
    }

    private bool CanUseWasapiProcessedOutput()
    {
        return !string.IsNullOrWhiteSpace(_processedOutputEndpointId) && !IsProcessedOutputAsio();
    }

    private bool CanUseWaveOutProcessedOutputFallback()
    {
        return !IsProcessedOutputAsio()
            && (string.IsNullOrWhiteSpace(_processedOutputEndpointId) || _processedOutputDeviceNumber >= 0);
    }

    private bool IsProcessedOutputAsio()
    {
        return TryGetAsioDriverName(_processedOutputEndpointId, out _);
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
            var shareMode = _wasapiOutputSettings.ExclusiveMode
                ? AudioClientShareMode.Exclusive
                : AudioClientShareMode.Shared;
            var latencyMilliseconds = _wasapiOutputSettings.EffectiveLatencyMilliseconds;
            player = new WasapiOut(endpoint, shareMode, _wasapiOutputSettings.UseEventDrivenOutput, latencyMilliseconds);
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

    private bool TryStartAsioProcessedOutput(IWaveProvider provider, out IWavePlayer? player, out IWaveProvider? playbackProvider)
    {
        player = null;
        playbackProvider = null;
        if (!TryGetAsioDriverName(_processedOutputEndpointId, out var driverName))
        {
            return false;
        }

        if (TryStartAsioProcessedOutputProvider(driverName, provider, out player))
        {
            playbackProvider = provider;
            return true;
        }

        foreach (var sampleRate in PreferredAsioSampleRates.Where(rate => rate != provider.WaveFormat.SampleRate))
        {
            var resampledProvider = CreateBestOutputResampler(provider, sampleRate);
            if (ReferenceEquals(resampledProvider, provider))
            {
                continue;
            }

            if (TryStartAsioProcessedOutputProvider(driverName, resampledProvider, out player))
            {
                playbackProvider = resampledProvider;
                return true;
            }

            DisposePlaybackProvider(resampledProvider, provider);
        }

        return false;
    }

    private static bool TryStartAsioProcessedOutputProvider(string driverName, IWaveProvider playbackProvider, out IWavePlayer? player)
    {
        player = null;
        try
        {
            player = new AsioOutputPlayer(driverName);
            player.Init(playbackProvider);
            player.Play();
            return true;
        }
        catch
        {
            player?.Dispose();
            player = null;
            return false;
        }
    }

    private static IWaveProvider CreateWasapiOutputProvider(IWaveProvider provider, MMDevice endpoint)
    {
        return CreateBestOutputResampler(provider, endpoint.AudioClient.MixFormat.SampleRate);
    }

    private static IWaveProvider CreateBestOutputResampler(IWaveProvider provider, int outputSampleRate)
    {
        if (outputSampleRate <= 0 || outputSampleRate == provider.WaveFormat.SampleRate)
        {
            return provider;
        }

        if (TryCreateHighQualityOutputResampler(provider, outputSampleRate, out var mediaFoundationResampler))
        {
            return mediaFoundationResampler;
        }

        return CreateWdlOutputResampler(provider, outputSampleRate);
    }

    private static IWaveProvider CreateWdlOutputResampler(IWaveProvider provider, int outputSampleRate)
    {
        var resampler = new WdlResamplingSampleProvider(provider.ToSampleProvider(), outputSampleRate);
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

    private bool TryStartDirectSoundProcessedOutput(IWaveProvider provider, out IWavePlayer? player)
    {
        player = null;
        if (!CanUseWaveOutProcessedOutputFallback())
        {
            return false;
        }

        try
        {
            player = StartDirectSoundProcessedOutput(provider);
            return true;
        }
        catch
        {
            player?.Dispose();
            player = null;
            return false;
        }
    }

    private static IWavePlayer StartDirectSoundProcessedOutput(IWaveProvider provider)
    {
        var directSound = new DirectSoundOut(WaveOutProcessedOutputLatencyMilliseconds);
        directSound.Init(provider);
        directSound.Play();
        return directSound;
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
            _processedOutputBackendDescription = "not routed";
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

    private void AddProcessedOutputSamples(ReadOnlySpan<float> samples, int sourceChannelCount)
    {
        var provider = _processedOutputProvider;
        if (!IsProcessedOutputEnabledVolatile() || provider is null || samples.IsEmpty)
        {
            return;
        }

        var upcomingByteCount = GetProcessedOutputByteCount(provider, samples.Length, sourceChannelCount);
        if (upcomingByteCount <= 0)
        {
            return;
        }

        TrimProcessedOutputBufferIfNeeded(provider, upcomingByteCount);
        ArmProcessedOutputRecoveryRampIfUnderrun(provider, upcomingByteCount);

        var byteCount = provider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
            ? ConvertFloatSamplesToStereoFloat32(samples, sourceChannelCount)
            : ConvertFloatSamplesToStereoPcm16(samples, sourceChannelCount);
        provider.AddSamples(_processedOutputBytes, 0, byteCount);
        TrimProcessedOutputBufferIfNeeded(provider);
    }

    private void WriteProcessedRecordingSamples(ReadOnlySpan<float> samples, int sourceChannelCount)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        Exception? writeException = null;
        lock (_processedRecordingLock)
        {
            var writer = _processedRecordingWriter;
            if (writer is null || _isProcessedRecordingPaused)
            {
                return;
            }

            try
            {
                var recordingSamples = EnsureRecordingChannelCount(samples, sourceChannelCount, writer.WaveFormat.Channels);
                var byteCount = checked(recordingSamples.Length * sizeof(float));
                if (_processedRecordingBytes.Length < byteCount)
                {
                    _processedRecordingBytes = new byte[byteCount];
                }

                MemoryMarshal.AsBytes(recordingSamples).CopyTo(_processedRecordingBytes.AsSpan(0, byteCount));
                writer.Write(_processedRecordingBytes, 0, byteCount);
            }
            catch (Exception ex)
            {
                writeException = ex;
                StopProcessedAudioRecordingLocked();
            }
        }

        if (writeException is not null)
        {
            StreamStatusChanged?.Invoke(this, $"Audio recording stopped: {writeException.Message}");
        }
    }

    private string? StopProcessedAudioRecordingLocked()
    {
        var writer = _processedRecordingWriter;
        var path = _processedRecordingPath;
        if (writer is null)
        {
            _processedRecordingPath = null;
            _isProcessedRecordingPaused = false;
            return path;
        }

        try
        {
            writer.Flush();
        }
        catch
        {
        }

        try
        {
            writer.Dispose();
        }
        catch
        {
        }
        finally
        {
            _processedRecordingWriter = null;
            _processedRecordingPath = null;
            _isProcessedRecordingPaused = false;
        }

        return path;
    }

    private static int GetProcessedOutputByteCount(BufferedWaveProvider provider, int sampleCount, int sourceChannelCount)
    {
        if (sampleCount <= 0)
        {
            return 0;
        }

        sourceChannelCount = Math.Max(1, sourceChannelCount);
        var frameCount = sampleCount / sourceChannelCount;
        var blockAlign = Math.Max(1, provider.WaveFormat.BlockAlign);
        return (int)Math.Min(int.MaxValue, (long)frameCount * blockAlign);
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

    private static string DescribeWaveFormat(WaveFormat format)
    {
        var isFloat = IsFloatCaptureFormat(format);
        var encoding = isFloat ? "float" : "PCM";
        var bitsPerSample = isFloat ? 32 : format.BitsPerSample;
        return $"{format.SampleRate / 1000d:0.#} kHz, {format.Channels} ch, {bitsPerSample}-bit {encoding}";
    }

    private static string DescribeCaptureBackend(IWaveIn capture)
    {
        return capture switch
        {
            ProcessLoopbackCapture => "WASAPI process loopback",
            SignalGeneratorCapture => "NAudio signal generator",
            WasapiLoopbackCapture => "WASAPI loopback",
            WasapiCapture => "WASAPI",
            WaveInEvent => "WaveIn",
            _ => capture.GetType().Name
        };
    }

    private bool ShouldPreferWaveInCapture()
    {
        return System.Threading.Volatile.Read(ref _preferWaveInCapture) != 0;
    }

    private string DescribeTargetProcessedOutputFormat()
    {
        return $"{_activeSampleRate / 1000d:0.#} kHz, 2 ch, 32-bit float";
    }

    private bool IsTargetProcessedOutputFormat(WaveFormat format)
    {
        return format.SampleRate == _activeSampleRate
            && format.Channels == 2
            && IsFloatCaptureFormat(format);
    }

    private static bool WaveFormatsMatch(WaveFormat first, WaveFormat second)
    {
        return first.SampleRate == second.SampleRate
            && first.Channels == second.Channels
            && first.BitsPerSample == second.BitsPerSample
            && first.Encoding == second.Encoding
            && IsFloatCaptureFormat(first) == IsFloatCaptureFormat(second)
            && IsPcmCaptureFormat(first) == IsPcmCaptureFormat(second);
    }

    private int ConvertFloatSamplesToStereoFloat32(ReadOnlySpan<float> samples, int sourceChannelCount)
    {
        var byteCount = ProcessedAudioSampleConverter.GetStereoFloat32ByteCount(samples.Length, sourceChannelCount);
        if (_processedOutputBytes.Length < byteCount)
        {
            _processedOutputBytes = new byte[byteCount];
        }

        return ProcessedAudioSampleConverter.WriteStereoFloat32(
            samples,
            sourceChannelCount,
            _processedOutputBytes.AsSpan(0, byteCount),
            PrepareProcessedOutputSample);
    }

    private int ConvertFloatSamplesToStereoPcm16(ReadOnlySpan<float> samples, int sourceChannelCount)
    {
        var byteCount = ProcessedAudioSampleConverter.GetStereoPcm16ByteCount(samples.Length, sourceChannelCount);
        if (_processedOutputBytes.Length < byteCount)
        {
            _processedOutputBytes = new byte[byteCount];
        }

        return ProcessedAudioSampleConverter.WriteStereoPcm16(
            samples,
            sourceChannelCount,
            _processedOutputBytes.AsSpan(0, byteCount),
            ref _ditherState,
            PrepareProcessedOutputSample);
    }

    private ReadOnlySpan<float> EnsureRecordingChannelCount(
        ReadOnlySpan<float> samples,
        int sourceChannelCount,
        int recordingChannelCount)
    {
        sourceChannelCount = Math.Max(1, sourceChannelCount);
        recordingChannelCount = Math.Max(1, recordingChannelCount);
        if (sourceChannelCount == recordingChannelCount)
        {
            return samples;
        }

        var requiredSamples = ProcessedAudioSampleConverter.GetConvertedSampleCount(
            samples.Length,
            sourceChannelCount,
            recordingChannelCount);
        if (_processedRecordingFloatSamples.Length < requiredSamples)
        {
            _processedRecordingFloatSamples = new float[requiredSamples];
        }

        var destination = _processedRecordingFloatSamples.AsSpan(0, requiredSamples);
        ProcessedAudioSampleConverter.WriteChannelCount(
            samples,
            sourceChannelCount,
            recordingChannelCount,
            destination);
        return destination;
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
        _audioStreamStartedTimestamp = 0;
        _audioCallbackIntervalMs = 0;
        _audioExpectedCallbackIntervalMs = 0;
        _audioProcessingTimeMs = 0;
        _audioBufferDurationMs = CaptureBufferMilliseconds;
        _audioCallbackSampleCount = 0;
        _audioLateFrameCount = 0;
        _audioBufferResetCount = 0;
        _spectrumSkippedFrameCount = 0;
    }

    private static TimeSpan ElapsedSince(long startTimestamp, long endTimestamp)
    {
        var elapsedSeconds = Math.Max(0d, (endTimestamp - startTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency);
        return TimeSpan.FromSeconds(elapsedSeconds);
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

    private static void FillMonoSamples(ReadOnlySpan<byte> buffer, WaveFormat format, InputChannelMode channelMode, Span<float> samples)
    {
        var channels = Math.Max(1, format.Channels);
        if (IsFloatCaptureFormat(format) && format.BitsPerSample == 32)
        {
            FillFloatMonoSamples(buffer, samples, channels, channelMode);
            return;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 16)
        {
            FillPcm16MonoSamples(buffer, samples, channels, channelMode);
            return;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 32)
        {
            FillPcm32MonoSamples(buffer, samples, channels, channelMode);
            return;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 24)
        {
            FillPcm24MonoSamples(buffer, samples, channels, channelMode);
            return;
        }

        samples.Clear();
    }

    private static void FillStereoSamples(ReadOnlySpan<byte> buffer, WaveFormat format, InputChannelMode channelMode, Span<float> samples)
    {
        var channels = Math.Max(1, format.Channels);
        if (IsFloatCaptureFormat(format) && format.BitsPerSample == 32)
        {
            FillFloatStereoSamples(buffer, samples, channels, channelMode);
            return;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 16)
        {
            FillPcm16StereoSamples(buffer, samples, channels, channelMode);
            return;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 32)
        {
            FillPcm32StereoSamples(buffer, samples, channels, channelMode);
            return;
        }

        if (IsPcmCaptureFormat(format) && format.BitsPerSample == 24)
        {
            FillPcm24StereoSamples(buffer, samples, channels, channelMode);
            return;
        }

        samples.Clear();
    }

    private static int GetWholeFrameCount(int bufferLength, int channels, int bytesPerSample)
    {
        var frameBytes = Math.Max(1, channels) * Math.Max(1, bytesPerSample);
        return Math.Max(0, bufferLength / frameBytes);
    }

    private static int GetCaptureFrameCount(int bufferLength, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        return GetWholeFrameCount(bufferLength, channels, bytesPerSample);
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

    private void EnsureMixBuffers(int length)
    {
        var stereoLength = checked(length * 2);
        if (_mixBusSamples.Length < stereoLength)
        {
            _mixBusSamples = new float[stereoLength];
        }

        if (_mixOutputSamples.Length < stereoLength)
        {
            _mixOutputSamples = new float[stereoLength];
        }

        if (_mixOutputMonoSamples.Length < length)
        {
            _mixOutputMonoSamples = new float[length];
        }

        if (_mixChannelInputSamples.Length < length)
        {
            _mixChannelInputSamples = new float[length];
        }

        if (_mixChannelStereoInputSamples.Length < stereoLength)
        {
            _mixChannelStereoInputSamples = new float[stereoLength];
        }
    }

    private void ResetSampleBuffers()
    {
        _monoSamples = [];
        _processedSamples = [];
        _mixBusSamples = [];
        _mixOutputSamples = [];
        _mixOutputMonoSamples = [];
        _mixChannelInputSamples = [];
        _mixChannelStereoInputSamples = [];
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
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedFloatChannel(floatSamples, samples, channels, selectedChannel);
            return;
        }

        if (channels == 1)
        {
            FillSelectedFloatChannel(floatSamples, samples, channels, 0);
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

    private static void FillFloatStereoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var floatSamples = MemoryMarshal.Cast<byte, float>(buffer);
        var frameCount = Math.Min(samples.Length / 2, floatSamples.Length / channels);
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedFloatChannelAsStereo(floatSamples, samples, channels, selectedChannel, frameCount);
        }
        else if (channels == 1)
        {
            FillSelectedFloatChannelAsStereo(floatSamples, samples, channels, 0, frameCount);
        }
        else
        {
            var inputOffset = 0;
            var outputOffset = 0;
            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[outputOffset] = SanitizeCaptureSample(floatSamples[inputOffset]);
                samples[outputOffset + 1] = SanitizeCaptureSample(floatSamples[inputOffset + 1]);
                inputOffset += channels;
                outputOffset += 2;
            }
        }

        ClearUnusedStereoSamples(samples, frameCount);
    }

    private static void FillSelectedFloatChannelAsStereo(
        ReadOnlySpan<float> inputSamples,
        Span<float> samples,
        int channels,
        int selectedChannel,
        int frameCount)
    {
        var inputOffset = selectedChannel;
        var outputOffset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = SanitizeCaptureSample(inputSamples[inputOffset]);
            samples[outputOffset] = sample;
            samples[outputOffset + 1] = sample;
            inputOffset += channels;
            outputOffset += 2;
        }
    }

    private static void FillPcm16MonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var pcmSamples = MemoryMarshal.Cast<byte, short>(buffer);
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedPcm16Channel(pcmSamples, samples, channels, selectedChannel);
            return;
        }

        if (channels == 1)
        {
            FillSelectedPcm16Channel(pcmSamples, samples, channels, 0);
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

    private static void FillPcm16StereoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var pcmSamples = MemoryMarshal.Cast<byte, short>(buffer);
        var frameCount = Math.Min(samples.Length / 2, pcmSamples.Length / channels);
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedPcm16ChannelAsStereo(pcmSamples, samples, channels, selectedChannel, frameCount);
        }
        else if (channels == 1)
        {
            FillSelectedPcm16ChannelAsStereo(pcmSamples, samples, channels, 0, frameCount);
        }
        else
        {
            var inputOffset = 0;
            var outputOffset = 0;
            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[outputOffset] = SanitizeCaptureSample(pcmSamples[inputOffset] / 32768f);
                samples[outputOffset + 1] = SanitizeCaptureSample(pcmSamples[inputOffset + 1] / 32768f);
                inputOffset += channels;
                outputOffset += 2;
            }
        }

        ClearUnusedStereoSamples(samples, frameCount);
    }

    private static void FillSelectedPcm16ChannelAsStereo(
        ReadOnlySpan<short> inputSamples,
        Span<float> samples,
        int channels,
        int selectedChannel,
        int frameCount)
    {
        var inputOffset = selectedChannel;
        var outputOffset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = SanitizeCaptureSample(inputSamples[inputOffset] / 32768f);
            samples[outputOffset] = sample;
            samples[outputOffset + 1] = sample;
            inputOffset += channels;
            outputOffset += 2;
        }
    }

    private static void FillPcm32MonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var pcmSamples = MemoryMarshal.Cast<byte, int>(buffer);
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedPcm32Channel(pcmSamples, samples, channels, selectedChannel);
            return;
        }

        if (channels == 1)
        {
            FillSelectedPcm32Channel(pcmSamples, samples, channels, 0);
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

    private static void FillPcm32StereoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        var pcmSamples = MemoryMarshal.Cast<byte, int>(buffer);
        var frameCount = Math.Min(samples.Length / 2, pcmSamples.Length / channels);
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedPcm32ChannelAsStereo(pcmSamples, samples, channels, selectedChannel, frameCount);
        }
        else if (channels == 1)
        {
            FillSelectedPcm32ChannelAsStereo(pcmSamples, samples, channels, 0, frameCount);
        }
        else
        {
            var inputOffset = 0;
            var outputOffset = 0;
            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[outputOffset] = SanitizeCaptureSample(Pcm32ToFloat(pcmSamples[inputOffset]));
                samples[outputOffset + 1] = SanitizeCaptureSample(Pcm32ToFloat(pcmSamples[inputOffset + 1]));
                inputOffset += channels;
                outputOffset += 2;
            }
        }

        ClearUnusedStereoSamples(samples, frameCount);
    }

    private static void FillSelectedPcm32ChannelAsStereo(
        ReadOnlySpan<int> inputSamples,
        Span<float> samples,
        int channels,
        int selectedChannel,
        int frameCount)
    {
        var inputOffset = selectedChannel;
        var outputOffset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = SanitizeCaptureSample(Pcm32ToFloat(inputSamples[inputOffset]));
            samples[outputOffset] = sample;
            samples[outputOffset + 1] = sample;
            inputOffset += channels;
            outputOffset += 2;
        }
    }

    private static void FillPcm24MonoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        const int bytesPerSample = 3;
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedPcm24Channel(buffer, samples, channels, selectedChannel);
            return;
        }

        if (channels == 1)
        {
            FillSelectedPcm24Channel(buffer, samples, channels, 0);
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

    private static void FillPcm24StereoSamples(ReadOnlySpan<byte> buffer, Span<float> samples, int channels, InputChannelMode channelMode)
    {
        const int bytesPerSample = 3;
        var frameStride = channels * bytesPerSample;
        var frameCount = Math.Min(samples.Length / 2, buffer.Length / frameStride);
        if (TryGetSelectedChannel(channelMode, channels, out var selectedChannel))
        {
            FillSelectedPcm24ChannelAsStereo(buffer, samples, channels, selectedChannel, frameCount);
        }
        else if (channels == 1)
        {
            FillSelectedPcm24ChannelAsStereo(buffer, samples, channels, 0, frameCount);
        }
        else
        {
            var inputOffset = 0;
            var outputOffset = 0;
            for (var frame = 0; frame < frameCount; frame++)
            {
                samples[outputOffset] = SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, inputOffset));
                samples[outputOffset + 1] = SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, inputOffset + bytesPerSample));
                inputOffset += frameStride;
                outputOffset += 2;
            }
        }

        ClearUnusedStereoSamples(samples, frameCount);
    }

    private static void FillSelectedPcm24ChannelAsStereo(
        ReadOnlySpan<byte> buffer,
        Span<float> samples,
        int channels,
        int selectedChannel,
        int frameCount)
    {
        const int bytesPerSample = 3;
        var inputOffset = selectedChannel * bytesPerSample;
        var frameStride = channels * bytesPerSample;
        var outputOffset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = SanitizeCaptureSample(ReadPcm24SampleRaw(buffer, inputOffset));
            samples[outputOffset] = sample;
            samples[outputOffset + 1] = sample;
            inputOffset += frameStride;
            outputOffset += 2;
        }
    }

    private static void ClearUnusedStereoSamples(Span<float> samples, int filledFrameCount)
    {
        var filledSamples = Math.Clamp(filledFrameCount * 2, 0, samples.Length);
        if (filledSamples < samples.Length)
        {
            samples[filledSamples..].Clear();
        }
    }

    private static int GetSummedInputChannelCount(int channels)
    {
        return Math.Clamp(channels, 1, 10);
    }

    private static bool TryGetSelectedChannel(InputChannelMode mode, int availableChannels, out int selectedChannel)
    {
        selectedChannel = 0;
        var selected = InputChannelModeInfo.GetSelectedChannelIndex(mode);
        if (selected is null || selected.Value < 0 || selected.Value >= availableChannels)
        {
            return false;
        }

        selectedChannel = selected.Value;
        return true;
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
        var stoppedCapture = sender as IWaveIn;
        var shouldRecover = (stoppedCapture is null || ReferenceEquals(_capture, stoppedCapture))
            && _autoRecoverCapture
            && !_isStoppingCapture
            && !_isDisposing;
        ReleaseCapture(stoppedCapture);
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
                        Start(_currentDeviceNumber, _currentInputEndpointId, _currentInputBackend, _currentProcessorSettings, _currentInputChannelMode);
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

    private void AbandonCurrentCaptureForDriverReset()
    {
        var capture = _capture;
        if (capture is null)
        {
            return;
        }

        try
        {
            DetachCaptureEvents(capture);
        }
        catch
        {
        }

        if (ReferenceEquals(_capture, capture))
        {
            _capture = null;
            _voiceProcessor = null;
            System.Threading.Interlocked.Increment(ref _spectrumAnalysisVersion);
            RestoreRuntimeMode();
        }
    }

    private void ReleaseCapture(IWaveIn? capture = null)
    {
        capture ??= _capture;
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
                _voiceProcessor = null;
                _captureBackendDescription = "not open";
                System.Threading.Interlocked.Increment(ref _spectrumAnalysisVersion);
                RestoreRuntimeMode();
            }
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









