using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using JerichoDown.Modules.Audio.Asio;
using JerichoDown.Modules.Audio.CoreAudio;
using JerichoDown.Modules.Audio.Devices;
using JerichoDown.Modules.Audio.Diagnostics;
using JerichoDown.Modules.Audio.Dsp;
using JerichoDown.Modules.Audio.Live;
using JerichoDown.Modules.Audio.Recording;
using JerichoDown.Modules.Help;
using JerichoDown.Modules.Karaoke;
using JerichoDown.Modules.Midi;
using JerichoDown.Modules.Mixer;
using JerichoDown.Modules.SessionPlayback;
using JerichoDown.Modules.Webcam;
using JerichoDown.Modules.Webcam.DirectShow;
using JerichoDown.Modules.Webcam.Dx12;
using JerichoDown.Modules.Webcam.MediaFoundation;
using JerichoDown.Modules.Visualization;
using JerichoDown.Modules.Visualization.Dx12;
using ShapePath = System.Windows.Shapes.Path;

namespace JerichoDown;

public partial class EqualizerWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const uint SeeMaskInvokeIdList = 0x0000000C;
    private const int SwShowNormal = 1;
    private const double MinimumDisplayFrequency = 20d;
    private const double MaximumDisplayFrequency = 20000d;
    private const double EqualizerHoverQ = 1.35d;
    private const int MaximumMicChannelCount = 10;
    private const int SystemAudioLoopbackChannelNumber = MaximumMicChannelCount + 1;
    private const double ExpandedControlRailWidth = 360d;
    private const double CollapsedControlRailWidth = 44d;
    private const double RightControlRailWidth = 360d;
    private const double DefaultAnalyzerSmoothingPercent = 80d;
    private const double EqualizerFaceplateOuterGap = 18d;
    private const double DenoiseDefaultStrength = 2d;
    private const double DenoiseMaximumStrength = 5d;
    private const int MaximumKaraokeBrowserFolders = 100000;
    private const int MaximumKaraokeBrowserTracks = 20000;
    private const int MaximumAnalyzedRecordingRows = 24;
    private static readonly TimeSpan EagerRecordingAnalysisDuration = TimeSpan.FromSeconds(10);
    private const long MaximumKaraokeEmbeddedLyricsProbeBytes = 256L * 1024L * 1024L;
    private const int MaximumKaraokeMp4AtomDepth = 24;
    private const int MaximumKaraokeMp4AtomCount = 100000;
    private const int KaraokeLyricVisibleLineCount = 5;
    private static readonly TimeSpan KaraokeLyricDisplayLead = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan KaraokeShortWordDisplayHold = TimeSpan.FromMilliseconds(155);
    private static readonly TimeSpan KaraokeLyricLineTransitionDuration = TimeSpan.FromMilliseconds(135);
    private static readonly TimeSpan AudioRecordingFolderRefreshDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AudioDeviceFormatPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AudioCallbackStartupGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AudioCallbackStaleDuration = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan AudioStreamRestartBaseBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AudioStreamRestartMaximumBackoff = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AppStatePersistDebounceInterval = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CameraPumpWarningDisplayDuration = TimeSpan.FromSeconds(8);
    private static readonly Regex KaraokeLyricTimestampRegex = new(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex KaraokeInlineLyricTimestampRegex = new(@"<(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?>", RegexOptions.Compiled);
    private static readonly string KaraokeTrackOpenFileFilter = KaraokePlaybackPolicy.CreateOpenFileFilter();
    private static readonly string UserPresetFolder = AppStoragePaths.UserPresetFolder;
    private static readonly string CameraProfileFolder = AppStoragePaths.CameraProfileFolder;
    private static readonly string KaraokeAiToolsFolder = System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "Tools", "KaraokeAi");
    private static readonly string KaraokeAiWorkFolder = System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "KaraokeAiWork");
    private static readonly string KaraokeLyricCacheFolder = System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "KaraokeLyrics");
    private static readonly string DefaultKaraokeRecordingFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Jericho Down Karaoke Recordings");
    private static readonly JsonSerializerOptions UserPresetJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly PropertyInfo[] UserPresetSettingProperties = typeof(VoiceProcessorSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property =>
            property.CanRead
            && property.CanWrite
            && (property.PropertyType == typeof(double) || property.PropertyType == typeof(bool)))
        .OrderBy(property => property.Name)
        .ToArray();
    private static readonly double EqualizerHoverHalfPowerRatio = CalculateEqualizerHoverHalfPowerRatio();
    private const double MixerUnityVolumePercent = 100d;
    private const double MixerUnityMagnetismPercent = 2d;
    private readonly MicrophoneSpectrumService _spectrumService = new();
    private readonly MediaFoundationCameraPreviewService _cameraPreviewService = new();
    private readonly DirectShowCameraPreviewService _directShowPreviewService = new();
    private readonly MediaFoundationCameraModeService _cameraModeService = new();
    private readonly DirectShowCameraControlService _cameraControlService = new();
    private readonly AppSettingsState _appSettings = AppStateStore.LoadSettings();
    private readonly AppStartupRecovery _startupRecovery = AppStateStore.StartupRecovery;
    private readonly ObservableCollection<MicChannelStrip> _micChannels = CreateDefaultMicChannels();
    private readonly MidiInputMonitor _midiInputMonitor = new();
    private readonly MidiOutputPort _midiOutputPort = new();
    private readonly MidiControlMappingTriggerState _midiControlMappingTriggerState = new();
    private readonly ObservableCollection<MidiMessageSnapshot> _midiMessages = [];
    private readonly ObservableCollection<MidiControlMappingRule> _midiControlMappings = [];
    private readonly ObservableCollection<MidiTrackSummary> _midiSequenceTracks = [];
    private readonly ObservableCollection<SoundFontPresetSummary> _midiSoundFontPresets = [];
    private readonly ObservableCollection<SoundFontInstrumentSummary> _midiSoundFontInstruments = [];
    private readonly ObservableCollection<SoundFontSampleSummary> _midiSoundFontSamples = [];
    private readonly ObservableCollection<CoreAudioSessionControlItem> _coreAudioSessionItems = [];
    private MicChannelStrip _activeMicChannel = null!;
    private readonly DispatcherTimer _audioDeviceFormatTimer = new();
    private readonly DispatcherTimer _audioDeviceRefreshTimer = new();
    private readonly DispatcherTimer _outputAudioSessionTimer = new();
    private readonly DispatcherTimer _sessionPlaybackPositionTimer = new();
    private readonly DispatcherTimer _appStatePersistTimer = new();
    private readonly List<Line> _gridLines = [];
    private readonly IReadOnlyList<VoiceZone> _voiceZones =
    [
        new("Rumble", 40, 80, "Low thumps, desk vibration, plosives"),
        new("Warmth", 100, 250, "Chest and fullness"),
        new("Mud", 250, 500, "Cloudy or boomy buildup"),
        new("Box/Nasal", 500, 1200, "Hollow, boxy, or nasal tone"),
        new("Presence", 2000, 5000, "Clarity and intelligibility"),
        new("Sibilance", 5000, 9000, "S, sh, and harsh bite"),
        new("Air", 10000, 16000, "Breath and sparkle")
    ];
    private readonly ShapePath _liveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 3,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly ShapePath _averageTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(167, 176, 188)),
        StrokeThickness = 1.5,
        Opacity = 0.68,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly ShapePath _input1Trace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 2,
        Opacity = 0.95,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly ShapePath _input2Trace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(167, 176, 188)),
        StrokeThickness = 2,
        Opacity = 0.95,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Rectangle _equalizerHoverRegion = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(58, 0, 190, 230)),
        Stroke = new SolidColorBrush(Color.FromArgb(170, 53, 232, 216)),
        StrokeThickness = 1,
        RadiusX = 3,
        RadiusY = 3,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false
    };
    private readonly ShapePath[] _mixingMicSpectrumTraces = CreateMixingMicSpectrumTraces();
    private readonly List<Line> _mixingSpectrumGridLines = [];
    private readonly double[][] _renderedMixingMicMagnitudes = new double[2][];
    private readonly SolidColorBrush _mixingProgramSpectrumBrush = new(Color.FromRgb(0, 190, 230));
    private readonly SolidColorBrush _mixingRecordingSpectrumBrush = new(Color.FromRgb(167, 176, 188));
    private readonly SolidColorBrush _recordingDspActiveBrush = new(Color.FromRgb(66, 215, 125));
    private readonly SolidColorBrush _recordingNaturalAudioBrush = new(Color.FromRgb(215, 178, 32));
    private readonly SolidColorBrush _waveformGridBrush = new(Color.FromRgb(36, 45, 54));
    private readonly SolidColorBrush _meterMutedBrush = new(Color.FromRgb(105, 132, 156));
    private readonly SolidColorBrush _meterTextMutedBrush = new(Color.FromRgb(184, 199, 217));
    private readonly SolidColorBrush _meterGoodBrush = new(Color.FromRgb(66, 215, 125));
    private readonly SolidColorBrush _meterWarnBrush = new(Color.FromRgb(215, 178, 32));
    private readonly SolidColorBrush _meterDangerBrush = new(Color.FromRgb(218, 80, 72));
    private readonly SolidColorBrush _karaokeInactiveLineBrush = new(Color.FromRgb(141, 162, 183));
    private readonly SolidColorBrush _karaokeActiveLineBrush = new(Colors.White);
    private readonly SolidColorBrush _karaokeSungWordBrush = new(Color.FromRgb(121, 238, 226));
    private readonly SolidColorBrush _karaokeActiveWordBrush = new(Color.FromRgb(53, 232, 216));
    private readonly SolidColorBrush _karaokeActiveLineBackgroundBrush = new(Color.FromArgb(105, 38, 61, 74));
    private volatile SpectrumFrame? _latestFrame;
    private double[] _renderedMagnitudes = [];
    private double[] _renderedRawMagnitudes = [];
    private double[] _renderedInput1Magnitudes = [];
    private double[] _renderedInput2Magnitudes = [];
    private double _visualCeiling = 0.25d;
    private double _mixingVisualCeiling = 0.25d;
    private double _displayInputPeak;
    private long _lastInputLevelDisplayTimestamp;
    private double _lastInputLevelMeterWidth = -1d;
    private string? _lastInputCoachText;
    private Brush? _lastInputCoachForeground;
    private Brush? _lastInputLevelFill;
    private double _displayOutputPeak;
    private double _displayOutputRms;
    private double _displayMasterLimiterReductionDb;
    private long _lastOutputLevelDisplayTimestamp;
    private double _lastOutputLevelMeterWidth = -1d;
    private double _lastOutputRmsMeterWidth = -1d;
    private double _lastMasterLimiterReductionMeterWidth = -1d;
    private string? _lastOutputSignalText;
    private string? _lastMasterMeterText;
    private string? _lastMasterLimiterReductionText;
    private string? _lastMasterLimiterNormalizeText;
    private string? _lastFeedbackRiskText;
    private Brush? _lastOutputSignalForeground;
    private Brush? _lastOutputLevelFill;
    private Brush? _lastMasterClipFill;
    private Brush? _lastFeedbackRiskForeground;
    private int _masterClipHoldFrames;
    private int _feedbackRiskHoldFrames;
    private double _displayFeedbackRiskScore;
    private double _lastFeedbackRiskFrequencyHz;
    private double _lastFeedbackRiskSpikeRiseDb;
    private long _lastAudioStabilityDisplayTimestamp;
    private double _audioStabilityScore;
    private double _audioStabilityMeterWidth;
    private double _displayedAudioFrameIntervalMs;
    private double _displayedAudioProcessingMs;
    private int _audioStabilitySeverity;
    private int _audioStabilityCandidateSeverity = -1;
    private long _audioStabilityCandidateTimestamp;
    private int _silentFrameCount;
    private readonly Dictionary<Slider, double> _processingSliderDefaults = [];
    private readonly Dictionary<Slider, string> _processingSliderBaseToolTips = [];
    private bool _isSnappingProcessingSlider;
    private bool _isSnappingMixerUnitySlider;
    private bool _isSnappingVideoDenoiseSlider;
    private AudioInputDevice? _selectedDevice;
    private AudioOutputDevice? _selectedOutputDevice;
    private WasapiOutputSettings _wasapiOutputSettings = WasapiOutputSettings.Default;
    private AudioDeviceNotificationWatcher? _audioDeviceNotificationWatcher;
    private AudioDeviceFormat? _selectedDeviceFormat;
    private bool _isRestartingAudioStream;
    private bool _isCheckingAudioDeviceFormat;
    private DateTime _nextAudioStreamRestartAttemptUtc = DateTime.MinValue;
    private int _audioStreamRestartFailureCount;
    private string? _lastAudioStreamRestartFailureMessage;
    private bool _isClosing;
    private bool _isRestoringAppState = true;
    private bool _safeStartCameraRecoveryActive;
    private bool _safeStartDx12Disabled;
    private bool _isUpdatingOutputRoutingUi;
    private bool _isUpdatingWasapiOutputUi;
    private bool _isUpdatingMicChannelUi;
    private bool _isUpdatingMixerUi;
    private bool _isUpdatingCoreAudioSessionUi;
    private bool _isUpdatingMidiEnabledUi;
    private bool _midiEnabled;
    private string? _lastShownAsioNoCallbackDiagnosticKey;
    private string? _asioNoCallbackAutoStartSuppressedDeviceKey;
    private int _audioStreamOperationVersion;
    private int _selectedDeviceFormatRefreshVersion;
    private InputChannelMode _selectedInputChannelMode = InputChannelMode.MonoSum;
    private double _masterMixVolumePercent = 100d;
    private bool _masterMixNormalizeEnabled = true;
    private bool _masterMixLimiterEnabled = true;
    private double _masterMixLimiterCeilingDb = -1d;
    private MixBusOutputMode _masterMixOutputMode = MixBusOutputMode.Stereo;
    private ProcessedRecordingSource _audioRecordingSource = ProcessedRecordingSource.ProgramMix;
    private string _outputFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Jericho Down Sessions");
    private string _audioRecordingFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Jericho Down Audio Recordings");
    private string? _activeAudioRecordingPath;
    private DateTime _audioRecordingStartedAt;
    private DateTime _audioRecordingPausedAt;
    private TimeSpan _audioRecordingPausedDuration;
    private bool _isStandaloneAudioRecording;
    private bool _isStandaloneAudioRecordingPaused;
    private string _activeAudioRecordingSourceLabel = "program mix";
    private string? _lastAudioRecordingPath;
    private readonly ObservableCollection<AudioRecordingFileItem> _audioRecordingFiles = [];
    private FileBrowserWatcher? _audioRecordingFolderWatcher;
    private int _audioRecordingFolderRefreshQueued;
    private IWavePlayer? _audioPlaybackOutput;
    private AudioFileReader? _audioPlaybackReader;
    private string? _audioPlaybackPath;
    private bool _isStoppingAudioPlayback;
    private readonly DispatcherTimer _karaokePlaybackPositionTimer = new();
    private IWavePlayer? _karaokeTrackOutput;
    private KaraokeTrackAudioReader? _karaokeTrackReader;
    private MediaPlayer? _karaokeTrackMediaPlayer;
    private KaraokeRateSampleProvider? _karaokeTrackRateProvider;
    private SmbPitchShiftingSampleProvider? _karaokeTrackPitchProvider;
    private KaraokeVocalReductionSampleProvider? _karaokeTrackVocalReductionProvider;
    private string? _karaokeTrackPath;
    private TimeSpan _karaokeTrackDuration;
    private string? _karaokeBrowserFolder;
    private double _karaokeKeySemitones;
    private double _karaokeTempoPercent = 100d;
    private bool _karaokeVocalReductionEnabled;
    private bool _isKaraokeTrackPlaying;
    private bool _isStoppingKaraokePlayback;
    private bool _isScrubbingKaraokePlayback;
    private bool _isUpdatingKaraokePlaybackPosition;
    private bool _isUpdatingKaraokeAdjustments;
    private bool _isUpdatingKaraokeQueueSelection;
    private bool _isKaraokeVocalRecording;
    private bool _isKaraokeVocalRecordingPaused;
    private bool _isDetectingKaraokeLyrics;
    private string? _karaokeVocalRecordingPath;
    private DateTime _karaokeVocalRecordingStartedAt;
    private DateTime _karaokeVocalRecordingPausedAt;
    private TimeSpan _karaokeVocalRecordingPausedDuration;
    private string _karaokeRecordingFolder = DefaultKaraokeRecordingFolder;
    private string? _lastKaraokeRecordingPath;
    private bool _karaokeRecordVideoEnabled;
    private readonly ObservableCollection<AudioRecordingFileItem> _karaokeRecordingFiles = [];
    private FileBrowserWatcher? _karaokeRecordingFolderWatcher;
    private int _karaokeRecordingFolderRefreshQueued;
    private IWavePlayer? _karaokeRecordingPlaybackOutput;
    private AudioFileReader? _karaokeRecordingPlaybackReader;
    private string? _karaokeRecordingPlaybackPath;
    private bool _isStoppingKaraokeRecordingPlayback;
    private readonly ObservableCollection<KaraokeTrackItem> _karaokeQueue = [];
    private readonly ObservableCollection<KaraokeBrowserFileItem> _karaokeBrowserFiles = [];
    private bool _karaokeQueueExplicitlyCleared;
    private readonly ObservableCollection<KaraokeLyricLineItem> _karaokeLyricDisplayLines = [];
    private readonly List<Border> _karaokeLyricLineBorders = [];
    private readonly List<TextBlock> _karaokeLyricLineTextBlocks = [];
    private readonly List<List<Run>> _karaokeLyricLineRuns = [];
    private List<KaraokeTimedLyricLine> _karaokeTimedLyrics = [];
    private int _karaokeActiveLineIndex = -1;
    private int _karaokeActiveTokenIndex = -1;
    private int _karaokeRenderedActiveLineIndex = int.MinValue;
    private int _karaokeRenderedActiveTokenIndex = int.MinValue;
    private int _karaokeRenderedWindowStartLineIndex = int.MinValue;
    private string? _lastSavedKaraokeLyricsTrackPath;
    private string? _lastSavedKaraokeLyricsHash;
    private readonly Stopwatch _karaokePlaybackClock = new();
    private TimeSpan _karaokePlaybackClockBasePosition;
    private DateTime _lastKaraokeTransportUiUpdateUtc = DateTime.MinValue;
    private DateTime _lastAudioVisualRenderUtc = DateTime.MinValue;
    private DateTime _lastRecordingTimerUpdateUtc = DateTime.MinValue;
    private DateTime _lastRecordingHealthUpdateUtc = DateTime.MinValue;
    private DateTime _lastStandaloneRecordingSyncUtc = DateTime.MinValue;
    private string? _activeRecordingSessionFolder;
    private string? _lastSessionRecordingPath;
    private int _activeRecordingSetNumber;
    private DateTime _recordingStartedAt;
    private DateTime _recordingPausedAt;
    private TimeSpan _recordingPausedDuration;
    private bool _cameraAvailable;
    private bool _isCameraEnabled;
    private bool _isUpdatingCameraUi;
    private bool _isUpdatingCameraControls;
    private bool _isLoadingCameraModes;
    private bool _isCameraServiceStopPending;
    private bool _isCameraPreviewStartPending;
    private bool _isDirectShowPreviewActive;
    private int _cameraServiceStopOperationVersion;
    private int _cameraPreviewStartOperationVersion;
    private bool _pendingVideoDenoiseEnabled;
    private double _pendingVideoDenoiseStrength = DenoiseDefaultStrength;
    private double _videoDenoiseSliderStrength = DenoiseDefaultStrength;
    private VideoFrameColorSettings _pendingVideoColorSettings = VideoFrameColorSettings.Off;
    private readonly CpuPreviewFramePump _cpuPreviewFramePump;
    private WriteableBitmap? _cameraPreviewBitmap;
    private Dx12Camera? _dx12Camera;
    private readonly TextureNativeStatusPump _textureNativeStatusPump;
    private string? _lastTextureNativeCameraError;
    private TextureNativeCameraRecordingSession? _textureNativeRecordingSession;
    private TextureNativeRecordingResult? _lastTextureNativeRecordingResult;
    private string? _lastPreviewRecordingDiagnostics;
    private CancellationTokenSource? _cameraModeLoadCancellation;
    private string? _cameraPumpWarningText;
    private DateTime _cameraPumpWarningExpiresUtc = DateTime.MinValue;
    private Direct3D12AudioGraphHost? _waveform3DGraphHost;
    private Direct3D12AudioGraphHost? _selectedMicSpectrumGraphHost;
    private Direct3D12AudioGraphHost? _podcastSpectrumWaterfallGraphHost;
    private Direct3D12AudioGraphHost? _karaokeSpectrumWaterfallGraphHost;
    private Direct3D12AudioGraphHost? _mixingMicSpectrumGraphHost;
    private Direct3D12AudioGraphHost? _mixingOutputWaveform3DGraphHost;
    private int _waterfallLinesPerGap = Direct3D12AudioGraphHost.DefaultWaterfallLinesPerGap;
    private volatile bool _isMicDspSelectedMicSpectrumActive;
    private volatile bool _isMicDspSpectrumWaterfallActive;
    private volatile bool _isPodcastSpectrumWaterfallActive;
    private volatile bool _isKaraokeSpectrumWaterfallActive;
    private volatile bool _isMixingMicSpectrumGraphActive;
    private volatile bool _isMixingOutputWaveform3DActive;
    private EqualizerBand? _hoveredEqualizerBand;
    private bool _showWaveform3D = true;
    private bool _isLeftControlRailCollapsed;
    private string _lastLoadedPresetName = "Warm Radio";
    private bool _lastLoadedPresetIsUserPreset;
    private bool _isRecordingSession;
    private bool _isRecordingPaused;
    private string? _pendingCameraProfileModeLabel;
    private readonly ObservableCollection<SessionRecordingItem> _sessionRecordings = [];
    private string? _loadedMidiFilePath;
    private MidiSequencePlaybackPlan? _midiSequencePlaybackPlan;
    private CancellationTokenSource? _midiSequencePlaybackCancellation;
    private bool _isMidiSequencePlaying;
    private bool _isRestoringMidiState;
    private SoundFontSummary? _loadedMidiSoundFont;
    private IWavePlayer? _midiSoundFontPreviewOutput;
    private WaveStream? _midiSoundFontPreviewStream;
    private FileBrowserWatcher? _sessionFolderWatcher;
    private int _sessionFolderRefreshQueued;
    private string? _sessionPlaybackPath;
    private bool _isScrubbingSessionPlayback;
    private bool _isUpdatingSessionPlaybackPosition;
    private bool _isSessionPlaybackPlaying;
    private bool _isSessionPlaybackEnded;
    private bool _startSessionPlaybackFromBeginning;
    private MediaFoundationFilePlaybackService? _sessionVideoPlaybackService;
    private Direct3D12PreviewHost? _sessionDx12PlaybackHost;
    private IWavePlayer? _sessionAudioPlaybackOutput;
    private WaveStream? _sessionAudioPlaybackReader;
    private string? _sessionAudioPlaybackPath;
    private bool _isStoppingSessionAudioPlayback;
    private bool _isSessionDx12PlaybackActive;
    private bool _isSessionMediaElementFallbackActive;
    private static ShapePath[] CreateMixingMicSpectrumTraces()
    {
        var traces = new ShapePath[2];
        for (var i = 0; i < traces.Length; i++)
        {
            traces[i] = new ShapePath
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
                StrokeThickness = 2.6d,
                Opacity = 0.98d,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(traces[i], 1);
        }

        return traces;
    }

    public EqualizerWindow()
    {
        InitializeComponent();
        _cpuPreviewFramePump = new CpuPreviewFramePump(Dispatcher, ProcessPendingCameraPreviewFrame, CameraPumpWarningRaised);
        _textureNativeStatusPump = new TextureNativeStatusPump(Dispatcher, ProcessPendingTextureNativeFrame, CameraPumpWarningRaised);
        ApplyPersistedWindowPlacement();
        _safeStartCameraRecoveryActive = _startupRecovery.PreviousRunDidNotCloseCleanly;
        _safeStartDx12Disabled = _startupRecovery.PreviousRunDidNotCloseCleanly;
        FreezeSharedBrushes();
        OrderMainTabs();
        _activeMicChannel = GetDefaultActiveMicChannel() ?? _micChannels[0];
        UpdateActiveMicSelectionFlags();
        DataContext = Settings;
        EqBandPanel.ItemsSource = Bands;
        Dispatcher.BeginInvoke(new Action(UpdateEqualizerGuideLayout), DispatcherPriority.Loaded);
        AttachEqualizerBandHandlers(_activeMicChannel);
        SyncEqualizerSettings();
        SpectrumCanvas.Children.Add(_equalizerHoverRegion);
        SpectrumCanvas.Children.Add(_input1Trace);
        SpectrumCanvas.Children.Add(_input2Trace);
        SpectrumCanvas.Children.Add(_averageTrace);
        SpectrumCanvas.Children.Add(_liveTrace);
        foreach (var trace in _mixingMicSpectrumTraces)
        {
            MixingMicSpectrumCanvas.Children.Add(trace);
        }

        _spectrumService.SpectrumAvailable += SpectrumAvailable;
        _spectrumService.StreamStatusChanged += SpectrumServiceStreamStatusChanged;
        _midiInputMonitor.MessageReceived += MidiInputMonitorMessageReceived;
        _midiInputMonitor.StatusChanged += MidiInputMonitorStatusChanged;
        _cameraPreviewService.FrameAvailable += CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged += CameraPreviewStatusChanged;
        _directShowPreviewService.FrameAvailable += CameraPreviewFrameAvailable;
        _directShowPreviewService.StatusChanged += CameraPreviewStatusChanged;
        _audioDeviceFormatTimer.Interval = AudioDeviceFormatPollInterval;
        _audioDeviceFormatTimer.Tick += AudioDeviceFormatTimerTick;
        _audioDeviceRefreshTimer.Interval = TimeSpan.FromMilliseconds(500);
        _audioDeviceRefreshTimer.Tick += AudioDeviceRefreshTimerTick;
        _outputAudioSessionTimer.Interval = TimeSpan.FromSeconds(2);
        _outputAudioSessionTimer.Tick += OutputAudioSessionTimerTick;
        _sessionPlaybackPositionTimer.Interval = TimeSpan.FromMilliseconds(120);
        _sessionPlaybackPositionTimer.Tick += SessionPlaybackPositionTimerTick;
        _appStatePersistTimer.Interval = AppStatePersistDebounceInterval;
        _appStatePersistTimer.Tick += AppStatePersistTimerTick;
        _karaokePlaybackPositionTimer.Interval = TimeSpan.FromMilliseconds(45);
        _karaokePlaybackPositionTimer.Tick += KaraokePlaybackPositionTimerTick;
        CompositionTarget.Rendering += CompositionTargetRendering;
    }

    private void FreezeSharedBrushes()
    {
        FreezeBrush(_recordingDspActiveBrush);
        FreezeBrush(_recordingNaturalAudioBrush);
        FreezeBrush(_mixingProgramSpectrumBrush);
        FreezeBrush(_mixingRecordingSpectrumBrush);
        FreezeBrush(_waveformGridBrush);
        FreezeBrush(_meterMutedBrush);
        FreezeBrush(_meterTextMutedBrush);
        FreezeBrush(_meterGoodBrush);
        FreezeBrush(_meterWarnBrush);
        FreezeBrush(_meterDangerBrush);
        FreezeBrush(_karaokeInactiveLineBrush);
        FreezeBrush(_karaokeActiveLineBrush);
        FreezeBrush(_karaokeSungWordBrush);
        FreezeBrush(_karaokeActiveWordBrush);
        FreezeBrush(_karaokeActiveLineBackgroundBrush);
    }

    private static void FreezeBrush(SolidColorBrush brush)
    {
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }
    }

    private void OrderMainTabs()
    {
        string[] preferredOrder =
        [
            "Podcast",
            "Mic / DSP",
            "Mixing",
            "Karaoke",
            "MIDI"
        ];

        var tabs = MainTabControl.Items.OfType<TabItem>().ToList();
        MainTabControl.Items.Clear();
        foreach (var header in preferredOrder)
        {
            var tab = tabs.FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            if (tab is not null)
            {
                MainTabControl.Items.Add(tab);
                tabs.Remove(tab);
            }
        }

        MainTabControl.SelectedIndex = 0;
    }

    public ObservableCollection<EqualizerBand> Bands => _activeMicChannel.Bands;

    public ObservableCollection<string> UserPresetNames { get; } = [];

    public ObservableCollection<string> CameraProfileNames { get; } = [];

    public VoiceProcessorSettings Settings => _activeMicChannel.ProcessorSettings;

    private void AttachEqualizerBandHandlers(MicChannelStrip channel)
    {
        foreach (var band in channel.Bands)
        {
            band.PropertyChanged += EqualizerBandPropertyChanged;
        }
    }

    private void DetachEqualizerBandHandlers(MicChannelStrip channel)
    {
        foreach (var band in channel.Bands)
        {
            band.PropertyChanged -= EqualizerBandPropertyChanged;
        }
    }

    private void EqualizerBandPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EqualizerBand.GainDb) or nameof(EqualizerBand.IsEnabled))
        {
            SyncEqualizerSettings();
        }
    }

    private void SyncEqualizerSettings()
    {
        SyncEqualizerSettings(_activeMicChannel);
    }

    private static void SyncEqualizerSettings(MicChannelStrip channel)
    {
        var gains = new double[channel.Bands.Count];
        for (var i = 0; i < channel.Bands.Count; i++)
        {
            gains[i] = channel.Bands[i].IsEnabled ? channel.Bands[i].GainDb : 0d;
        }

        channel.ProcessorSettings.SetEqualizerGains(gains);
    }

    private static void ApplyEqualizerBandState(MicChannelStrip channel, IReadOnlyList<EqualizerBandSettingsState> bands)
    {
        for (var i = 0; i < channel.Bands.Count; i++)
        {
            if (i >= bands.Count)
            {
                channel.Bands[i].IsEnabled = true;
                channel.Bands[i].GainDb = 0d;
                continue;
            }

            channel.Bands[i].IsEnabled = bands[i].IsEnabled;
            channel.Bands[i].GainDb = Math.Clamp(bands[i].GainDb, -12d, 12d);
        }
    }

    private static void SetProcessorSetting<T>(VoiceProcessorSettings settings, string name, T value)
    {
        var property = UserPresetSettingProperties.FirstOrDefault(candidate =>
            candidate.Name.Equals(name, StringComparison.Ordinal)
            && candidate.PropertyType == typeof(T));
        property?.SetValue(settings, value);
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        _isRestoringAppState = true;
        var hasPersistedState = HasPersistedAppState();

        RestorePersistedFolders();
        RestorePersistedVideoDenoise();
        UpdateVideoColorPolishSettings();
        UpdateAdvancedCameraControlsVisibility(loadWhenOpened: false);
        RefreshActiveSpectrumWaterfallHosts();

        UpdateRecordingDspIndicator();
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Collapsed;

        ApplyDarkTitleBar();
        var devices = MicrophoneSpectrumService.GetInputDevices();
        var equalizerInputDevices = GetEqualizerInputDevices(devices);
        _isUpdatingMicChannelUi = true;
        try
        {
            MicrophoneComboBox.ItemsSource = equalizerInputDevices;
            RestoreMicChannelState(devices);
            RefreshEqualizerMicChannelList();
            MicChannelComboBox.SelectedItem = FindMicChannel(GetEqualizerMicChannels(), _appSettings.SelectedMicChannelNumber)
                ?? GetDefaultEqualizerMicChannel()
                ?? _micChannels[0];
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }

        MixingChannelsItemsControl.ItemsSource = _micChannels;
        UpdateMixingMicLegend();
        ApplyMixerStateToUi();
        if (equalizerInputDevices.Count > 0)
        {
            ApplyActiveMicChannelToUi();
        }
        else
        {
            StatusText.Text = "No microphones found";
        }

        var outputDevices = MicrophoneSpectrumService.GetOutputDevices();
        OutputDeviceComboBox.ItemsSource = outputDevices;
        KaraokeMonitorOutputDeviceComboBox.ItemsSource = outputDevices;
        if (outputDevices.Count > 0)
        {
            var selectedOutput = FindAudioOutputDevice(
                outputDevices,
                _appSettings.OutputEndpointId,
                _appSettings.OutputDeviceName) ?? outputDevices[0];
            SetSelectedOutputDevice(selectedOutput);
        }

        _wasapiOutputSettings = WasapiOutputSettings.FromPersisted(
            _appSettings.WasapiOutputProfile,
            _appSettings.WasapiOutputExclusiveMode,
            _appSettings.WasapiOutputCustomLatencyMilliseconds);
        ApplyWasapiOutputSettingsToUi();
        _spectrumService.ConfigureWasapiOutput(_wasapiOutputSettings);
        if (ProcessedOutputCheckBox is not null)
        {
            SetProcessedOutputToggleState(_appSettings.ProcessedOutputEnabled);
        }

        UserPresetComboBox.ItemsSource = UserPresetNames;
        LoadUserPresetList();
        CameraProfileComboBox.ItemsSource = CameraProfileNames;
        LoadCameraProfileList();

        var cameras = CameraSourceSelection.GetCameras();

        CameraComboBox.ItemsSource = cameras;
        if (cameras.Count > 0)
        {
            CameraComboBox.SelectedItem = CameraSourceSelection.FindCamera(
                cameras,
                _appSettings.CameraDevicePath,
                _appSettings.CameraSource,
                _appSettings.CameraName) ?? cameras[0];
            CameraComboBox.IsEnabled = true;
            _cameraAvailable = true;
        }
        else
        {
            CameraComboBox.ItemsSource = new[] { "No cameras found" };
            CameraComboBox.SelectedIndex = 0;
            CameraComboBox.IsEnabled = false;
            _cameraAvailable = false;
        }

        _pendingCameraProfileModeLabel = _appSettings.CameraModeLabel;
        _isCameraEnabled = false;
        _isLeftControlRailCollapsed = _appSettings.LeftControlRailCollapsed;
        ApplyLeftControlRailLayout();
        _lastAudioRecordingPath = _appSettings.LastAudioRecordingPath;
        _lastSessionRecordingPath = _appSettings.LastSessionRecordingPath;
        _lastKaraokeRecordingPath = _appSettings.LastKaraokeRecordingPath;
        KaraokeQueueDataGrid.ItemsSource = _karaokeQueue;
        KaraokeBrowserFilesListBox.ItemsSource = _karaokeBrowserFiles;
        KaraokeRecordingFilesListBox.ItemsSource = _karaokeRecordingFiles;
        RestoreKaraokeState();
        _ = LoadCameraModesAsync();
        ResetCameraControlPanel("Camera controls are available after mode loading. Use Refresh if you want to query the selected camera.");
        UpdateCameraEnabledState();
        UpdateOutputFolderText();
        RecordingFilesListBox.ItemsSource = _audioRecordingFiles;
        SessionFilesListBox.ItemsSource = _sessionRecordings;
        UpdateAudioRecordingFolderText();
        UpdateKaraokeRecordingFolderText();
        RefreshSessionRecordings();
        UpdateRecordingTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();
        UpdateSessionPlaybackTransportControls();
        MidiMessageListBox.ItemsSource = _midiMessages;
        MidiControlMappingsListBox.ItemsSource = _midiControlMappings;
        MidiControlMappingActionComboBox.ItemsSource = MidiControlMappingActions.DefaultActions;
        MidiControlMappingActionComboBox.SelectedIndex = 0;
        RestoreMidiControlMappings();
        MidiSequenceTracksItemsControl.ItemsSource = _midiSequenceTracks;
        MidiSoundFontPresetComboBox.ItemsSource = _midiSoundFontPresets;
        MidiSoundFontInstrumentsListBox.ItemsSource = _midiSoundFontInstruments;
        MidiSoundFontSamplesListBox.ItemsSource = _midiSoundFontSamples;
        CoreAudioSessionsItemsControl.ItemsSource = _coreAudioSessionItems;
        RestoreMidiWorkflowState();
        _midiEnabled = _appSettings.MidiEnabled;
        ApplyMidiEnabledState(refreshDevices: _midiEnabled, persist: false, updateStatus: false);
        UpdateMidiOutputParameterText();
        UpdateSelectedMidiMessageDetails();

        RestorePersistedPresetOrDefault();
        StartAudioDeviceNotificationWatcher();
        _audioDeviceFormatTimer.Start();
        _outputAudioSessionTimer.Start();
        UpdateAudioFormatRouteText();
        UpdateOutputAudioSessionText();
        if (_startupRecovery.PreviousRunDidNotCloseCleanly)
        {
            CameraPreviewStatusText.Text = "Safe start: previous run did not close cleanly. Camera auto-start, DX12 preview, and video denoise are disabled for this run.";
            CameraControlStatusText.Text = "Safe start is active. Turn the camera on manually after confirming devices look right.";
        }

        _isRestoringAppState = false;
        SaveAppStateNow();
    }

    private bool HasPersistedAppState()
    {
        return _appSettings.UpdatedAt != default;
    }

    private void RestorePersistedFolders()
    {
        if (!string.IsNullOrWhiteSpace(_appSettings.OutputFolder))
        {
            _outputFolder = _appSettings.OutputFolder;
        }

        if (!string.IsNullOrWhiteSpace(_appSettings.AudioRecordingFolder))
        {
            _audioRecordingFolder = _appSettings.AudioRecordingFolder;
        }

        if (!string.IsNullOrWhiteSpace(_appSettings.KaraokeRecordingFolder))
        {
            _karaokeRecordingFolder = _appSettings.KaraokeRecordingFolder;
        }
    }

    private void RestoreKaraokeState()
    {
        var restoredTrackPaths = (_appSettings.KaraokeTrackPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(_appSettings.KaraokeTrackPath)
            && File.Exists(_appSettings.KaraokeTrackPath)
            && !restoredTrackPaths.Contains(_appSettings.KaraokeTrackPath, StringComparer.OrdinalIgnoreCase))
        {
            restoredTrackPaths.Insert(0, _appSettings.KaraokeTrackPath);
        }

        AddKaraokeTracksToQueue(restoredTrackPaths, selectFirstAdded: false);

        _karaokeBrowserFolder = Directory.Exists(_appSettings.KaraokeBrowserFolder)
            ? _appSettings.KaraokeBrowserFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        RefreshKaraokeBrowserFiles();

        _isUpdatingKaraokeAdjustments = true;
        try
        {
            _karaokeKeySemitones = Math.Clamp(_appSettings.KaraokeKeySemitones ?? 0d, -6d, 6d);
            _karaokeTempoPercent = Math.Clamp(_appSettings.KaraokeTempoPercent ?? 100d, 75d, 125d);
            _karaokeVocalReductionEnabled = _appSettings.KaraokeVocalReductionEnabled;
            _karaokeRecordVideoEnabled = _appSettings.KaraokeRecordVideoEnabled;
            KaraokeKeySlider.Value = _karaokeKeySemitones;
            KaraokeTempoSlider.Value = _karaokeTempoPercent;
            KaraokeVocalReductionCheckBox.IsChecked = _karaokeVocalReductionEnabled;
            KaraokeRecordVideoCheckBox.IsChecked = _karaokeRecordVideoEnabled;
        }
        finally
        {
            _isUpdatingKaraokeAdjustments = false;
        }

        var restoredTrack = !string.IsNullOrWhiteSpace(_appSettings.KaraokeTrackPath) && File.Exists(_appSettings.KaraokeTrackPath)
            ? _appSettings.KaraokeTrackPath
            : _karaokeQueue.FirstOrDefault()?.Path;
        if (!string.IsNullOrWhiteSpace(restoredTrack))
        {
            SetKaraokeTrack(restoredTrack, updateQueueSelection: true);
        }
        else
        {
            if (KaraokeLyricsTextBox is not null)
            {
                KaraokeLyricsTextBox.Text = _appSettings.KaraokeLyrics ?? string.Empty;
            }

            UpdateKaraokeTrackUi();
        }

        UpdateKaraokeLyricsDisplay();
        UpdateKaraokeAdjustmentStatus();
        UpdateKaraokeVideoPreviewState();
        UpdateKaraokeTransportControls();
    }

    private static InputChannelOption[] CreateInputChannelOptions(AudioInputDevice? device, AudioDeviceFormat? deviceFormat)
    {
        var availableChannels = GetAvailableInputChannelCount(device, deviceFormat);
        var options = new List<InputChannelOption>
        {
            new(InputChannelMode.MonoSum, GetInputChannelDisplayLabel(InputChannelMode.MonoSum, availableChannels, device))
        };

        if (availableChannels >= 2)
        {
            options.Add(new InputChannelOption(InputChannelMode.StereoPair, GetInputChannelDisplayLabel(InputChannelMode.StereoPair, availableChannels, device)));
        }

        for (var channelIndex = 0; channelIndex < availableChannels; channelIndex++)
        {
            var mode = InputChannelModeInfo.GetChannelMode(channelIndex);
            if (mode is not null)
            {
                options.Add(new InputChannelOption(mode.Value, GetInputChannelDisplayLabel(mode.Value, availableChannels, device)));
            }
        }

        return options.ToArray();
    }

    private static InputChannelMode CoerceInputChannelModeForDevice(
        AudioInputDevice? device,
        AudioDeviceFormat? deviceFormat,
        InputChannelMode requestedMode)
    {
        var options = CreateInputChannelOptions(device, deviceFormat);
        return options.Any(option => option.Mode == requestedMode)
            ? requestedMode
            : options.FirstOrDefault(option => option.Mode == InputChannelMode.MonoSum)?.Mode
                ?? options.FirstOrDefault()?.Mode
                ?? InputChannelMode.MonoSum;
    }

    private static int GetAvailableInputChannelCount(AudioInputDevice? device, AudioDeviceFormat? deviceFormat)
    {
        if (device is null)
        {
            return 0;
        }

        var channelCount = deviceFormat?.Channels > 0
            ? deviceFormat.Value.Channels
            : device.MaximumInputChannels;
        return Math.Clamp(channelCount, 0, MaximumMicChannelCount);
    }

    private static string GetInputChannelDisplayLabel(InputChannelMode mode, int availableChannels, AudioInputDevice? device = null)
    {
        if (availableChannels <= 1 && mode == InputChannelMode.Input1Left)
        {
            return "Input 1";
        }

        return InputChannelModeInfo.GetDisplayLabel(mode);
    }

    private void RestoreMicChannelState(IReadOnlyList<AudioInputDevice> devices)
    {
        foreach (var channel in _micChannels)
        {
            channel.SelectedDevice = null;
        }

        var persistedChannels = _appSettings.MicChannels ?? [];
        foreach (var state in persistedChannels)
        {
            var channel = FindMicChannel(state.ChannelNumber);
            if (channel is null)
            {
                continue;
            }

            ApplyMicChannelState(channel, state, devices);
        }

        EnsureSystemAudioLoopbackChannel(devices);
        ApplySystemAudioLoopbackSafeDefault();

        if (persistedChannels.Count == 0)
        {
            var firstMicChannel = FindMicChannel(1) ?? GetDefaultActiveMicChannel();
            if (firstMicChannel is not null)
            {
                firstMicChannel.SelectedDevice = FindAudioInputDevice(devices, _appSettings.MicrophoneEndpointId, _appSettings.MicrophoneName) ?? GetDefaultPhysicalInputDevice(devices);
                firstMicChannel.InputChannelMode = Enum.TryParse<InputChannelMode>(_appSettings.InputChannelMode, out var parsedMode)
                    ? parsedMode
                    : InputChannelMode.MonoSum;
                firstMicChannel.ActivePresetName = string.IsNullOrWhiteSpace(_appSettings.ActivePresetName)
                    ? firstMicChannel.ActivePresetName
                    : _appSettings.ActivePresetName;
                firstMicChannel.ActivePresetIsUserPreset = _appSettings.ActivePresetIsUserPreset;
            }
        }
        else
        {
            CoercePersistedMicChannelModes(devices);
        }

        _activeMicChannel = FindMicChannel(_appSettings.SelectedMicChannelNumber)
            ?? GetDefaultActiveMicChannel()
            ?? _micChannels[0];
        UpdateActiveMicSelectionFlags();
    }

    private void UpdateActiveMicSelectionFlags()
    {
        foreach (var channel in _micChannels)
        {
            channel.IsSelected = ReferenceEquals(channel, _activeMicChannel);
        }
    }

    private void ApplyMicChannelState(MicChannelStrip channel, MicChannelSettingsState state, IReadOnlyList<AudioInputDevice> devices)
    {
        channel.DisplayName = string.IsNullOrWhiteSpace(state.DisplayName)
            ? channel.DefaultDisplayName
            : state.DisplayName.Trim();
        channel.SelectedDevice = ResolvePersistedMicChannelDeviceByEndpoint(devices, state.MicrophoneEndpointId, state.MicrophoneName);
        channel.InputChannelMode = Enum.TryParse<InputChannelMode>(state.InputChannelMode, out var parsedMode)
            ? parsedMode
            : InputChannelMode.MonoSum;
        channel.IsEnabled = true;
        channel.IsMuted = state.IsMuted;
        channel.VolumePercent = Math.Clamp(state.VolumePercent ?? 100d, 0d, 150d);
        channel.InputGainDb = Math.Clamp(state.InputGainDb ?? 0d, -24d, 24d);
        channel.Pan = Math.Clamp(state.Pan ?? 0d, -100d, 100d);
        channel.PolarityInverted = state.PolarityInverted;
        channel.IsSoloed = state.IsSoloed;
        channel.DelayMilliseconds = Math.Clamp(state.DelayMilliseconds ?? 0d, 0d, 250d);
        channel.ActivePresetName = string.IsNullOrWhiteSpace(state.ActivePresetName)
            ? channel.ActivePresetName
            : state.ActivePresetName.Trim();
        channel.ActivePresetIsUserPreset = state.ActivePresetIsUserPreset;
        channel.PresetDescription = string.IsNullOrWhiteSpace(state.PresetDescription)
            ? channel.PresetDescription
            : state.PresetDescription.Trim();
        channel.AnalyzerSmoothing = Math.Clamp(state.AnalyzerSmoothing ?? channel.AnalyzerSmoothing, 0d, 100d);

        foreach (var setting in state.BooleanSettings ?? [])
        {
            SetProcessorSetting(channel.ProcessorSettings, setting.Key, setting.Value);
        }

        foreach (var setting in state.NumberSettings ?? [])
        {
            if (double.IsFinite(setting.Value))
            {
                SetProcessorSetting(channel.ProcessorSettings, setting.Key, setting.Value);
            }
        }

        ApplyEqualizerBandState(channel, state.EqualizerBands ?? []);
        SyncEqualizerSettings(channel);
    }

    private void CoercePersistedMicChannelModes(IReadOnlyList<AudioInputDevice> devices)
    {
        foreach (var channel in _micChannels)
        {
            if (channel.SelectedDevice is null)
            {
                channel.InputChannelMode = InputChannelMode.MonoSum;
                continue;
            }

            channel.InputChannelMode = CoerceInputChannelModeForDevice(channel.SelectedDevice, null, channel.InputChannelMode);
        }
    }

    private static MicChannelStrip? FindMicChannel(IEnumerable<MicChannelStrip> channels, int channelNumber)
    {
        return channels.FirstOrDefault(channel => channel.ChannelNumber == channelNumber);
    }

    private MicChannelStrip? FindMicChannel(int channelNumber)
    {
        return FindMicChannel(_micChannels, channelNumber);
    }

    private MicChannelStrip? GetDefaultActiveMicChannel()
    {
        return FindMicChannel(1)
            ?? _micChannels.FirstOrDefault(channel => !channel.IsSystemAudioLoopbackChannel)
            ?? _micChannels.FirstOrDefault();
    }

    private MicChannelStrip? GetDefaultEqualizerMicChannel()
    {
        return FindMicChannel(1) is { } firstMicChannel && IsEqualizerEditableMicChannel(firstMicChannel)
            ? firstMicChannel
            : _micChannels.FirstOrDefault(IsEqualizerEditableMicChannel);
    }

    private IReadOnlyList<MicChannelStrip> GetEqualizerMicChannels()
    {
        return _micChannels
            .Where(IsEqualizerEditableMicChannel)
            .ToArray();
    }

    private static bool IsEqualizerEditableMicChannel(MicChannelStrip channel)
    {
        return !channel.IsSystemAudioLoopbackChannel
            && (channel.SelectedDevice is null || IsEqualizerInputDevice(channel.SelectedDevice));
    }

    private static IReadOnlyList<AudioInputDevice> GetEqualizerInputDevices(IEnumerable<AudioInputDevice> devices)
    {
        return devices
            .Where(IsEqualizerInputDevice)
            .ToArray();
    }

    private static bool IsEqualizerInputDevice(AudioInputDevice device)
    {
        return !device.IsSystemAudioLoopback
            && !device.IsProcessLoopback
            && !device.IsStereoTestTone;
    }

    private void RefreshEqualizerMicChannelList()
    {
        if (MicChannelComboBox is null)
        {
            return;
        }

        var channels = GetEqualizerMicChannels();
        var selected = MicChannelComboBox.SelectedItem as MicChannelStrip;
        MicChannelComboBox.ItemsSource = channels;
        if (selected is not null && channels.Contains(selected))
        {
            MicChannelComboBox.SelectedItem = selected;
        }
        else if (_activeMicChannel is not null && channels.Contains(_activeMicChannel))
        {
            MicChannelComboBox.SelectedItem = _activeMicChannel;
        }
        else
        {
            MicChannelComboBox.SelectedItem = GetDefaultEqualizerMicChannel();
        }
    }

    private void EnsureSystemAudioLoopbackChannel(IReadOnlyList<AudioInputDevice> devices)
    {
        var channel = FindMicChannel(SystemAudioLoopbackChannelNumber);
        if (channel is null)
        {
            return;
        }

        var loopbackDevice = devices.FirstOrDefault(device => device.IsSystemAudioLoopback);
        channel.SelectedDevice = loopbackDevice;
        if (loopbackDevice is not null && channel.InputChannelMode == InputChannelMode.MonoSum)
        {
            channel.InputChannelMode = InputChannelMode.StereoPair;
        }

        channel.InputChannelMode = CoerceInputChannelModeForDevice(loopbackDevice, null, channel.InputChannelMode);
    }

    private void ApplySystemAudioLoopbackSafeDefault()
    {
        if (_appSettings.SystemAudioLoopbackDefaultMuteApplied)
        {
            return;
        }

        var channel = FindMicChannel(SystemAudioLoopbackChannelNumber);
        if (channel is null)
        {
            return;
        }

        channel.IsMuted = true;
        channel.IsSoloed = false;
    }

    private static AudioInputDevice? FindAudioInputDevice(
        IReadOnlyList<AudioInputDevice> devices,
        string? name)
    {
        return FindAudioInputDevice(devices, null, name);
    }

    private static AudioInputDevice? FindAudioInputDevice(
        IReadOnlyList<AudioInputDevice> devices,
        string? endpointId,
        string? name)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            var endpointMatch = devices.FirstOrDefault(device =>
                device.EndpointId?.Equals(endpointId, StringComparison.OrdinalIgnoreCase) == true);
            if (endpointMatch is not null)
            {
                return endpointMatch;
            }
        }

        return string.IsNullOrWhiteSpace(name)
            ? null
            : devices.FirstOrDefault(device =>
                device.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static AudioInputDevice? ResolvePersistedMicChannelDevice(
        IReadOnlyList<AudioInputDevice> devices,
        string? name)
    {
        return ResolvePersistedMicChannelDeviceByEndpoint(devices, null, name);
    }

    private static AudioInputDevice? ResolvePersistedMicChannelDeviceByEndpoint(
        IReadOnlyList<AudioInputDevice> devices,
        string? endpointId,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(endpointId) && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var exact = FindAudioInputDevice(devices, endpointId, name);
        if (exact is not null)
        {
            return exact;
        }

        if (MicrophoneSpectrumService.TryGetAsioDriverName(endpointId, out _))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(name) ? null : GetDefaultPhysicalInputDevice(devices);
    }

    private static bool AudioInputDevicesMatch(AudioInputDevice first, AudioInputDevice second)
    {
        if (!string.IsNullOrWhiteSpace(first.EndpointId) || !string.IsNullOrWhiteSpace(second.EndpointId))
        {
            return first.EndpointId?.Equals(second.EndpointId, StringComparison.OrdinalIgnoreCase) == true
                && first.Backend == second.Backend;
        }

        return first.DeviceNumber == second.DeviceNumber && first.Backend == second.Backend;
    }

    private static AudioInputDevice? GetDefaultPhysicalInputDevice(IReadOnlyList<AudioInputDevice> devices)
    {
        return devices.FirstOrDefault(IsEqualizerInputDevice);
    }

    private static AudioOutputDevice? FindAudioOutputDevice(
        IReadOnlyList<AudioOutputDevice> devices,
        string? endpointId,
        string? name)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            var endpointMatch = devices.FirstOrDefault(device =>
                device.EndpointId?.Equals(endpointId, StringComparison.OrdinalIgnoreCase) == true);
            if (endpointMatch is not null)
            {
                return endpointMatch;
            }
        }

        return string.IsNullOrWhiteSpace(name)
            ? null
            : devices.FirstOrDefault(device =>
                device.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void RestorePersistedVideoDenoise()
    {
        if (VideoDenoiseSlider is not null && _appSettings.VideoDenoiseStrength is { } strength && double.IsFinite(strength))
        {
            VideoDenoiseSlider.Value = Math.Clamp(strength, VideoDenoiseSlider.Minimum, VideoDenoiseSlider.Maximum);
            _videoDenoiseSliderStrength = VideoDenoiseSlider.Value;
        }

        if (VideoDenoiseCheckBox is not null)
        {
            VideoDenoiseCheckBox.IsChecked = _appSettings.VideoDenoiseEnabled && !_safeStartCameraRecoveryActive;
        }

        UpdateVideoDenoiseSettings(restartPreview: false);
    }

    private void RestorePersistedPresetOrDefault()
    {
        if (HasPersistedMicChannelState())
        {
            ApplyActiveMicPresetUiState();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_appSettings.ActivePresetName))
        {
            try
            {
                if (_appSettings.ActivePresetIsUserPreset)
                {
                    var preset = LoadUserPreset(_appSettings.ActivePresetName);
                    if (preset is not null)
                    {
                        ApplyUserPreset(preset);
                        return;
                    }
                }
                else if (ApplyBuiltInPreset(_appSettings.ActivePresetName))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                AppStateStore.LogDiagnostic("preset-restore-failed", ex);
            }
        }

        LoadBuiltInPreset(BuiltInVoicePresetCatalog.WarmRadio);
    }

    private bool HasPersistedMicChannelState()
    {
        return _appSettings.MicChannels is { Count: > 0 };
    }

    private bool ApplyBuiltInPreset(string presetName)
    {
        var preset = BuiltInVoicePresetCatalog.Find(presetName);
        if (preset is null)
        {
            return false;
        }

        LoadBuiltInPreset(preset);
        return true;
    }

    private void ApplyActiveMicPresetUiState()
    {
        SetProcessingSliderDefaults(_activeMicChannel.PresetDescription);
        SetActivePresetButton(_activeMicChannel.ActivePresetIsUserPreset ? null : _activeMicChannel.ActivePresetName);
        _lastLoadedPresetName = _activeMicChannel.ActivePresetName;
        _lastLoadedPresetIsUserPreset = _activeMicChannel.ActivePresetIsUserPreset;
    }

    private void ApplyDarkTitleBar()
    {
        ApplyDarkTitleBar(this);
    }

    private static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));

            var captionColor = RgbToColorRef(5, 7, 10);
            var textColor = RgbToColorRef(248, 250, 252);
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch
        {
        }
    }

    private void ApplyPersistedWindowPlacement()
    {
        if (!HasPersistedAppState())
        {
            return;
        }

        var width = _appSettings.WindowWidth;
        var height = _appSettings.WindowHeight;
        if (width is > 0 && height is > 0 && double.IsFinite(width.Value) && double.IsFinite(height.Value))
        {
            Width = Math.Max(MinWidth, width.Value);
            Height = Math.Max(MinHeight, height.Value);
        }

        var left = _appSettings.WindowLeft;
        var top = _appSettings.WindowTop;
        if (left is not null
            && top is not null
            && double.IsFinite(left.Value)
            && double.IsFinite(top.Value)
            && IsWindowPointOnVirtualScreen(left.Value, top.Value))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left.Value;
            Top = top.Value;
        }

        if (_appSettings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private static bool IsWindowPointOnVirtualScreen(double left, double top)
    {
        return left >= SystemParameters.VirtualScreenLeft - 80
            && top >= SystemParameters.VirtualScreenTop - 80
            && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80;
    }

    private void PersistAppState()
    {
        if (_isRestoringAppState || _isClosing || !IsLoaded)
        {
            return;
        }

        SaveAppStateNow();
    }

    private void ScheduleAppStatePersist()
    {
        if (_isRestoringAppState || _isClosing || !IsLoaded)
        {
            return;
        }

        _appStatePersistTimer.Stop();
        _appStatePersistTimer.Start();
    }

    private void AppStatePersistTimerTick(object? sender, EventArgs e)
    {
        _appStatePersistTimer.Stop();
        PersistAppState();
    }

    private void SaveAppStateNow()
    {
        _appStatePersistTimer.Stop();
        SaveCurrentKaraokeLyricsForCurrentTrack();
        AppStateStore.SaveSettings(CaptureAppSettingsState());
    }

    private AppSettingsState CaptureAppSettingsState()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var camera = CameraComboBox?.SelectedItem as CameraDevice;
        var mode = CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox?.SelectedItem);
        var selectedAudioPath = GetSelectedAudioRecordingPath();
        var selectedSessionPath = GetSelectedSessionRecordingPath();
        var selectedKaraokeRecordingPath = GetSelectedKaraokeRecordingPath();
        var karaokeTrackPath = _karaokeTrackPath;
        var karaokeTrackPaths = _karaokeQueue.Select(track => track.Path).ToList();
        var activeMicChannel = _activeMicChannel ?? GetDefaultActiveMicChannel();
        if (karaokeTrackPaths.Count == 0
            && !_karaokeQueueExplicitlyCleared
            && _appSettings.KaraokeTrackPaths is { Count: > 0 })
        {
            karaokeTrackPaths = _appSettings.KaraokeTrackPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(karaokeTrackPath)
                && !string.IsNullOrWhiteSpace(_appSettings.KaraokeTrackPath))
            {
                karaokeTrackPath = _appSettings.KaraokeTrackPath;
            }
        }

        return new AppSettingsState
        {
            WindowLeft = double.IsFinite(bounds.Left) ? bounds.Left : null,
            WindowTop = double.IsFinite(bounds.Top) ? bounds.Top : null,
            WindowWidth = double.IsFinite(bounds.Width) ? bounds.Width : null,
            WindowHeight = double.IsFinite(bounds.Height) ? bounds.Height : null,
            WindowMaximized = WindowState == WindowState.Maximized,
            LeftControlRailCollapsed = _isLeftControlRailCollapsed,
            SelectedMicChannelNumber = activeMicChannel?.ChannelNumber ?? 1,
            MicChannels = CaptureMicChannelStates(),
            MidiControlMappings = CaptureMidiControlMappingStates(),
            MidiEnabled = _midiEnabled,
            MidiInputDeviceName = (MidiInputDeviceComboBox?.SelectedItem as MidiInputDevice)?.ProductName,
            MidiInputDeviceProductId = (MidiInputDeviceComboBox?.SelectedItem as MidiInputDevice)?.ProductId,
            MidiOutputDeviceName = (MidiOutputDeviceComboBox?.SelectedItem as MidiOutputDevice)?.ProductName,
            MidiOutputDeviceProductId = (MidiOutputDeviceComboBox?.SelectedItem as MidiOutputDevice)?.ProductId,
            MidiSequenceSpeedPercent = MidiSequenceTempoSlider?.Value,
            MicrophoneName = activeMicChannel?.SelectedDevice?.Name,
            MicrophoneEndpointId = activeMicChannel?.SelectedDevice?.EndpointId,
            InputChannelMode = activeMicChannel?.InputChannelMode.ToString(),
            MixerMasterVolumePercent = _masterMixVolumePercent,
            MixerAutoNormalizeEnabled = _masterMixNormalizeEnabled,
            MixerLimiterEnabled = _masterMixLimiterEnabled,
            MixerLimiterCeilingDb = _masterMixLimiterCeilingDb,
            MixerOutputMode = _masterMixOutputMode.ToString(),
            SystemAudioLoopbackDefaultMuteApplied = true,
            OutputDeviceName = _selectedOutputDevice?.Name,
            OutputEndpointId = _selectedOutputDevice?.EndpointId,
            ProcessedOutputEnabled = ProcessedOutputCheckBox?.IsChecked == true,
            WasapiOutputProfile = _wasapiOutputSettings.Profile.ToString(),
            WasapiOutputExclusiveMode = _wasapiOutputSettings.ExclusiveMode,
            WasapiOutputCustomLatencyMilliseconds = _wasapiOutputSettings.CustomLatencyMilliseconds,
            OutputFolder = _outputFolder,
            AudioRecordingFolder = _audioRecordingFolder,
            AudioRecordingSource = _audioRecordingSource.ToString(),
            LastAudioRecordingPath = !string.IsNullOrWhiteSpace(selectedAudioPath) ? selectedAudioPath : _lastAudioRecordingPath,
            LastSessionRecordingPath = !string.IsNullOrWhiteSpace(selectedSessionPath) ? selectedSessionPath : _lastSessionRecordingPath,
            KaraokeTrackPath = karaokeTrackPath,
            KaraokeTrackPaths = karaokeTrackPaths,
            KaraokeBrowserFolder = _karaokeBrowserFolder,
            KaraokeRecordingFolder = _karaokeRecordingFolder,
            LastKaraokeRecordingPath = !string.IsNullOrWhiteSpace(selectedKaraokeRecordingPath) ? selectedKaraokeRecordingPath : _lastKaraokeRecordingPath,
            KaraokeRecordVideoEnabled = KaraokeRecordVideoCheckBox?.IsChecked == true,
            KaraokeLyrics = string.IsNullOrWhiteSpace(karaokeTrackPath) ? KaraokeLyricsTextBox?.Text : null,
            KaraokeKeySemitones = _karaokeKeySemitones,
            KaraokeTempoPercent = _karaokeTempoPercent,
            KaraokeVocalReductionEnabled = _karaokeVocalReductionEnabled,
            CameraName = camera?.Name,
            CameraSource = camera?.Source,
            CameraDevicePath = camera?.DevicePath,
            CameraEnabled = _isCameraEnabled,
            CameraModeLabel = mode.Label,
            CameraModeWidth = mode.Width,
            CameraModeHeight = mode.Height,
            CameraModeFramesPerSecond = mode.FramesPerSecond,
            CameraModeInputFormat = mode.InputFormat,
            VideoDenoiseEnabled = VideoDenoiseCheckBox?.IsChecked == true,
            VideoDenoiseStrength = VideoDenoiseSlider?.Value,
            ActivePresetName = _lastLoadedPresetName,
            ActivePresetIsUserPreset = _lastLoadedPresetIsUserPreset
        };
    }

    private List<MicChannelSettingsState> CaptureMicChannelStates()
    {
        return _micChannels.Select(channel =>
        {
            var state = new MicChannelSettingsState
            {
                ChannelNumber = channel.ChannelNumber,
                DisplayName = channel.DisplayName,
                MicrophoneName = channel.SelectedDevice?.Name,
                MicrophoneEndpointId = channel.SelectedDevice?.EndpointId,
                InputChannelMode = channel.InputChannelMode.ToString(),
                IsEnabled = true,
                IsMuted = channel.IsMuted,
                VolumePercent = channel.VolumePercent,
                InputGainDb = channel.InputGainDb,
                Pan = channel.Pan,
                PolarityInverted = channel.PolarityInverted,
                IsSoloed = channel.IsSoloed,
                DelayMilliseconds = channel.DelayMilliseconds,
                ActivePresetName = channel.ActivePresetName,
                ActivePresetIsUserPreset = channel.ActivePresetIsUserPreset,
                PresetDescription = channel.PresetDescription,
                AnalyzerSmoothing = channel.AnalyzerSmoothing,
                EqualizerBands = channel.Bands
                    .Select(band => new EqualizerBandSettingsState
                    {
                        Label = band.Label,
                        FrequencyHz = band.CenterFrequencyHz,
                        GainDb = band.GainDb,
                        IsEnabled = band.IsEnabled
                    })
                    .ToList()
            };

            foreach (var property in UserPresetSettingProperties)
            {
                if (property.PropertyType == typeof(double))
                {
                    var value = (double)(property.GetValue(channel.ProcessorSettings) ?? 0d);
                    if (double.IsFinite(value))
                    {
                        state.NumberSettings[property.Name] = value;
                    }
                }
                else if (property.PropertyType == typeof(bool))
                {
                    state.BooleanSettings[property.Name] = (bool)(property.GetValue(channel.ProcessorSettings) ?? false);
                }
            }

            return state;
        }).ToList();
    }

    private static int RgbToColorRef(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo executeInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr WindowHandle;
        public string Verb;
        public string File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public IntPtr InstanceHandle;
        public IntPtr ItemIdList;
        public string? Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr IconOrMonitor;
        public IntPtr ProcessHandle;
    }

    private void ShowWindowsFileProperties(string path)
    {
        var executeInfo = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = SeeMaskInvokeIdList,
            WindowHandle = new WindowInteropHelper(this).Handle,
            Verb = "properties",
            File = path,
            Show = SwShowNormal
        };

        if (!ShellExecuteEx(ref executeInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private void WindowActivated(object? sender, EventArgs e)
    {
        StartSelectedDevice();
        RefreshAudioRecordingFiles(_lastAudioRecordingPath);
        RefreshKaraokeRecordingFiles(_lastKaraokeRecordingPath);
        RefreshSessionRecordings(_lastSessionRecordingPath);
    }

    private void WindowDeactivated(object? sender, EventArgs e)
    {
        if (_spectrumService.IsRunning)
        {
            StatusText.Text = _spectrumService.IsProcessedOutputEnabled
                ? "Listening and routing output"
                : "Listening";
        }
    }

    private void StartAudioDeviceNotificationWatcher()
    {
        try
        {
            _audioDeviceNotificationWatcher = new AudioDeviceNotificationWatcher();
            _audioDeviceNotificationWatcher.DevicesChanged += AudioDeviceNotificationChanged;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Audio device notifications unavailable: {ex.Message}";
        }
    }

    private void DisposeAudioDeviceNotificationWatcher()
    {
        if (_audioDeviceNotificationWatcher is null)
        {
            return;
        }

        _audioDeviceNotificationWatcher.DevicesChanged -= AudioDeviceNotificationChanged;
        _audioDeviceNotificationWatcher.Dispose();
        _audioDeviceNotificationWatcher = null;
    }

    private void AudioDeviceNotificationChanged(object? sender, AudioDeviceChangedEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(ScheduleAudioDeviceRefresh), DispatcherPriority.Background);
    }

    private void ScheduleAudioDeviceRefresh()
    {
        if (_isClosing)
        {
            return;
        }

        _audioDeviceRefreshTimer.Stop();
        _audioDeviceRefreshTimer.Start();
    }

    private void AudioDeviceRefreshTimerTick(object? sender, EventArgs e)
    {
        _audioDeviceRefreshTimer.Stop();
        RefreshAudioDevicesFromSystem();
    }

    private void OutputAudioSessionTimerTick(object? sender, EventArgs e)
    {
        UpdateOutputAudioSessionText();
    }

    private void RefreshAudioDevicesFromSystem()
    {
        ClearAsioNoCallbackAutoStartSuppression();
        var inputDevices = MicrophoneSpectrumService.GetInputDevices();
        var equalizerInputDevices = GetEqualizerInputDevices(inputDevices);
        _isUpdatingMicChannelUi = true;
        try
        {
            MicrophoneComboBox.ItemsSource = equalizerInputDevices;
            foreach (var channel in _micChannels)
            {
                if (channel.SelectedDevice is not null)
                {
                    channel.SelectedDevice = FindAudioInputDevice(inputDevices, channel.SelectedDevice.EndpointId, channel.SelectedDevice.Name);
                }
            }

            EnsureSystemAudioLoopbackChannel(inputDevices);
            CoercePersistedMicChannelModes(inputDevices);
            RefreshEqualizerMicChannelList();
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }

        if (IsEqualizerEditableMicChannel(_activeMicChannel))
        {
            ApplyActiveMicChannelToUi();
        }
        else
        {
            ApplyActiveMicChannelSelectionToMixerUi();
        }

        ConfigureLiveMixFromChannels();

        var outputDevices = MicrophoneSpectrumService.GetOutputDevices();
        OutputDeviceComboBox.ItemsSource = outputDevices;
        KaraokeMonitorOutputDeviceComboBox.ItemsSource = outputDevices;
        var selectedOutput = _selectedOutputDevice is null
            ? outputDevices.FirstOrDefault()
            : FindAudioOutputDevice(outputDevices, _selectedOutputDevice.EndpointId, _selectedOutputDevice.Name)
                ?? outputDevices.FirstOrDefault();
        SetSelectedOutputDevice(selectedOutput);
        UpdateOutputRouting();
        StatusText.Text = inputDevices.Count > 0
            ? "Audio devices refreshed."
            : "Audio devices refreshed; no microphones found.";
    }

    private void RefreshVideoDevicesFromSystem()
    {
        var previousCamera = CameraComboBox?.SelectedItem as CameraDevice;
        var previousModeLabel = CameraModeComboBox?.SelectedItem is CameraVideoMode selectedMode
            ? selectedMode.Label
            : _appSettings.CameraModeLabel;
        if (_isCameraEnabled)
        {
            CancelPendingCameraPreviewStart();
        }

        IReadOnlyList<CameraDevice> cameras;
        try
        {
            cameras = CameraSourceSelection.GetCameras();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Video device refresh failed: {ex.Message}";
            CameraPreviewStatusText.Text = $"Video device refresh failed: {ex.Message}";
            return;
        }

        CameraDevice? selectedCamera = null;
        _isUpdatingCameraUi = true;
        try
        {
            if (cameras.Count > 0)
            {
                selectedCamera = CameraSourceSelection.FindCamera(
                    cameras,
                    previousCamera?.DevicePath,
                    previousCamera?.Source,
                    previousCamera?.Name) ?? cameras[0];
                CameraComboBox!.ItemsSource = cameras;
                CameraComboBox.SelectedItem = selectedCamera;
                CameraComboBox.IsEnabled = true;
                _cameraAvailable = true;
            }
            else
            {
                _cameraAvailable = false;
                _isCameraEnabled = false;
                CameraComboBox!.ItemsSource = new[] { "No cameras found" };
                CameraComboBox.SelectedIndex = 0;
                CameraComboBox.IsEnabled = false;
                CameraModeComboBox!.ItemsSource = new[] { CameraVideoMode.Auto };
                CameraModeComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingCameraUi = false;
        }

        if (!_cameraAvailable)
        {
            StopCameraPreview("Video devices refreshed; no cameras found.");
            ResetCameraControlPanel("No camera source found.");
            StatusText.Text = "Video devices refreshed; no cameras found.";
            UpdateCameraEnabledState();
            PersistAppState();
            return;
        }

        _pendingCameraProfileModeLabel = string.IsNullOrWhiteSpace(previousModeLabel)
            ? CameraVideoMode.Auto.Label
            : previousModeLabel;
        ResetCameraControlPanel("Video devices refreshed. Use Refresh after modes load if you want to query Windows camera controls.");
        var cameraCountText = cameras.Count == 1 ? "1 camera source" : $"{cameras.Count} camera sources";
        var selectedName = selectedCamera?.Name ?? "selected camera";
        StatusText.Text = $"Video devices refreshed: {cameraCountText}.";
        CameraPreviewStatusText.Text = $"Video devices refreshed: {selectedName}.";
        _ = LoadCameraModesAsync();
        PersistAppState();
    }

    private void ApplyMidiEnabledState(bool refreshDevices, bool persist, bool updateStatus = true)
    {
        if (EnableMidiMenuItem is not null && EnableMidiMenuItem.IsChecked != _midiEnabled)
        {
            _isUpdatingMidiEnabledUi = true;
            try
            {
                EnableMidiMenuItem.IsChecked = _midiEnabled;
            }
            finally
            {
                _isUpdatingMidiEnabledUi = false;
            }
        }

        if (RefreshMidiDevicesMenuItem is not null)
        {
            RefreshMidiDevicesMenuItem.IsEnabled = _midiEnabled;
        }

        if (MidiHelpMenuItem is not null)
        {
            MidiHelpMenuItem.IsEnabled = _midiEnabled;
        }

        if (MidiTabItem is not null)
        {
            if (!_midiEnabled && MidiTabItem.IsSelected)
            {
                SelectFirstVisibleMainTab(except: MidiTabItem);
            }

            MidiTabItem.Visibility = _midiEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_midiEnabled)
        {
            if (refreshDevices)
            {
                RefreshMidiDevicesFromSystem(updateStatus);
            }
        }
        else
        {
            StopMidiSequencePlayback("MIDI disabled.", resetOutput: false, updateStatus: false);
            _midiInputMonitor.Stop();
            _midiOutputPort.Close();
            MidiInputDeviceComboBox.ItemsSource = Array.Empty<MidiInputDevice>();
            MidiOutputDeviceComboBox.ItemsSource = Array.Empty<MidiOutputDevice>();
            UpdateMidiControlState();
            if (updateStatus && MidiStatusText is not null)
            {
                MidiStatusText.Text = "MIDI disabled. Enable it from File when needed.";
            }
        }

        if (persist)
        {
            PersistAppState();
        }
    }

    private void SelectFirstVisibleMainTab(TabItem? except = null)
    {
        if (MainTabControl is null)
        {
            return;
        }

        foreach (var tab in MainTabControl.Items.OfType<TabItem>())
        {
            if (!ReferenceEquals(tab, except) && tab.Visibility == Visibility.Visible)
            {
                MainTabControl.SelectedItem = tab;
                return;
            }
        }
    }

    private void RefreshMidiDevicesFromSystem(bool updateStatus = true)
    {
        if (!_midiEnabled)
        {
            if (updateStatus && MidiStatusText is not null)
            {
                MidiStatusText.Text = "MIDI disabled. Enable it from File when needed.";
            }

            return;
        }

        _isRestoringMidiState = true;
        var selectedInputNumber = (MidiInputDeviceComboBox.SelectedItem as MidiInputDevice)?.DeviceNumber
            ?? _midiInputMonitor.DeviceNumber;
        var selectedOutputNumber = (MidiOutputDeviceComboBox.SelectedItem as MidiOutputDevice)?.DeviceNumber
            ?? _midiOutputPort.DeviceNumber;
        IReadOnlyList<MidiInputDevice> inputDevices = [];
        IReadOnlyList<MidiOutputDevice> outputDevices = [];
        try
        {
            inputDevices = MidiDeviceCatalog.GetInputDevices();
            if (_midiInputMonitor.IsRunning
                && _midiInputMonitor.DeviceNumber is int runningInput
                && inputDevices.All(device => device.DeviceNumber != runningInput))
            {
                _midiInputMonitor.Stop();
            }

            MidiInputDeviceComboBox.ItemsSource = inputDevices;
            MidiInputDeviceComboBox.SelectedItem = SelectMidiInputDevice(inputDevices, selectedInputNumber);

            outputDevices = MidiDeviceCatalog.GetOutputDevices();
            if (_midiOutputPort.IsOpen
                && _midiOutputPort.DeviceNumber is int openOutput
                && outputDevices.All(device => device.DeviceNumber != openOutput))
            {
                StopMidiSequencePlayback("MIDI file test stopped because output disconnected.", resetOutput: false, updateStatus: false);
                _midiOutputPort.Close();
            }

            MidiOutputDeviceComboBox.ItemsSource = outputDevices;
            MidiOutputDeviceComboBox.SelectedItem = SelectMidiOutputDevice(outputDevices, selectedOutputNumber);
        }
        finally
        {
            _isRestoringMidiState = false;
        }

        UpdateMidiControlState();
        UpdateMidiDeviceDetails();
        if (updateStatus)
        {
            SetMidiStatus($"MIDI devices refreshed: {inputDevices.Count} inputs, {outputDevices.Count} outputs.");
        }
    }

    private void MidiInputMonitorMessageReceived(object? sender, MidiMessageSnapshot message)
    {
        if (_isClosing)
        {
            return;
        }

        PostMidiMessage(message);
    }

    private void MidiInputMonitorStatusChanged(object? sender, string status)
    {
        if (_isClosing)
        {
            return;
        }

        PostMidiUi(() => SetMidiStatus(status));
    }

    private void RestoreMidiWorkflowState()
    {
        _isRestoringMidiState = true;
        try
        {
            if (MidiSequenceTempoSlider is not null)
            {
                MidiSequenceTempoSlider.Value = Math.Clamp(_appSettings.MidiSequenceSpeedPercent ?? 100d, 50d, 200d);
            }

            if (MidiSequenceTempoValueText is not null)
            {
                MidiSequenceTempoValueText.Text = $"{Math.Round(MidiSequenceTempoSlider?.Value ?? 100d):0}%";
            }
        }
        finally
        {
            _isRestoringMidiState = false;
        }
    }

    private MidiInputDevice? SelectMidiInputDevice(IReadOnlyList<MidiInputDevice> devices, int? selectedDeviceNumber)
    {
        return devices.FirstOrDefault(device =>
                !string.IsNullOrWhiteSpace(_appSettings.MidiInputDeviceName)
                && string.Equals(device.ProductName, _appSettings.MidiInputDeviceName, StringComparison.OrdinalIgnoreCase)
                && (!_appSettings.MidiInputDeviceProductId.HasValue || device.ProductId == _appSettings.MidiInputDeviceProductId.Value))
            ?? devices.FirstOrDefault(device => device.DeviceNumber == selectedDeviceNumber)
            ?? devices.FirstOrDefault();
    }

    private MidiOutputDevice? SelectMidiOutputDevice(IReadOnlyList<MidiOutputDevice> devices, int? selectedDeviceNumber)
    {
        return devices.FirstOrDefault(device =>
                !string.IsNullOrWhiteSpace(_appSettings.MidiOutputDeviceName)
                && string.Equals(device.ProductName, _appSettings.MidiOutputDeviceName, StringComparison.OrdinalIgnoreCase)
                && (!_appSettings.MidiOutputDeviceProductId.HasValue || device.ProductId == _appSettings.MidiOutputDeviceProductId.Value))
            ?? devices.FirstOrDefault(device => device.DeviceNumber == selectedDeviceNumber)
            ?? devices.FirstOrDefault();
    }

    private void MidiDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringMidiState || _isClosing)
        {
            return;
        }

        UpdateMidiControlState();
        UpdateMidiDeviceDetails();
        ScheduleAppStatePersist();
    }

    private void AddMidiMessage(MidiMessageSnapshot message)
    {
        _midiMessages.Insert(0, message);
        while (_midiMessages.Count > 200)
        {
            _midiMessages.RemoveAt(_midiMessages.Count - 1);
        }

        if (MidiMessageListBox is not null)
        {
            MidiMessageListBox.SelectedIndex = _midiMessages.Count > 0 ? 0 : -1;
        }

        ApplyMidiControlMappings(message);
    }

    private void PostMidiMessage(MidiMessageSnapshot message)
    {
        PostMidiUi(() => AddMidiMessage(message));
    }

    private void PostMidiSequenceStatus(string status)
    {
        PostMidiUi(() =>
        {
            if (MidiSequenceStatusText is not null)
            {
                MidiSequenceStatusText.Text = status;
            }
        });
    }

    private void PostMidiUi(Action action)
    {
        if (_isClosing)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isClosing)
            {
                action();
            }
        }), DispatcherPriority.Background);
    }

    private Task InvokeMidiUiAsync(Action action)
    {
        if (_isClosing)
        {
            return Task.CompletedTask;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(new Action(() =>
        {
            if (!_isClosing)
            {
                action();
            }
        }), DispatcherPriority.Background).Task;
    }

    private void SetMidiStatus(string status)
    {
        if (MidiStatusText is not null)
        {
            MidiStatusText.Text = status;
        }

        if (StatusText is not null)
        {
            StatusText.Text = status;
        }
    }

    private void RestoreMidiControlMappings()
    {
        _midiControlMappings.Clear();
        _midiControlMappingTriggerState.Clear();
        foreach (var state in _appSettings.MidiControlMappings)
        {
            if (string.IsNullOrWhiteSpace(state.ActionName)
                || string.IsNullOrWhiteSpace(state.MessageType)
                || !MidiControlMappingActions.DefaultActions.Contains(state.ActionName, StringComparer.Ordinal))
            {
                continue;
            }

            _midiControlMappings.Add(new MidiControlMappingRule(
                state.ActionName,
                state.MessageType,
                state.Channel,
                state.Data1));
        }
    }

    private void UpdateMidiControlState()
    {
        var midiOutputReady = _midiOutputPort.IsOpen;
        var soundFontPresetReady = midiOutputReady && MidiSoundFontPresetComboBox?.SelectedItem is SoundFontPresetSummary;
        var soundFontSampleReady = _loadedMidiSoundFont is not null && MidiSoundFontSamplesListBox?.SelectedItem is SoundFontSampleSummary;
        if (MidiInputStartButton is not null)
        {
            MidiInputStartButton.IsEnabled = MidiInputDeviceComboBox.SelectedItem is MidiInputDevice && !_midiInputMonitor.IsRunning;
        }

        if (MidiInputStopButton is not null)
        {
            MidiInputStopButton.IsEnabled = _midiInputMonitor.IsRunning;
        }

        if (MidiOutputOpenButton is not null)
        {
            MidiOutputOpenButton.IsEnabled = MidiOutputDeviceComboBox.SelectedItem is MidiOutputDevice && !_midiOutputPort.IsOpen;
        }

        if (MidiOutputCloseButton is not null)
        {
            MidiOutputCloseButton.IsEnabled = midiOutputReady;
        }

        if (MidiPanicButton is not null)
        {
            MidiPanicButton.IsEnabled = midiOutputReady;
        }

        if (MidiAllNotesOffButton is not null)
        {
            MidiAllNotesOffButton.IsEnabled = midiOutputReady;
        }

        if (MidiResetControllersButton is not null)
        {
            MidiResetControllersButton.IsEnabled = midiOutputReady;
        }

        if (MidiFileExportButton is not null)
        {
            MidiFileExportButton.IsEnabled = !string.IsNullOrWhiteSpace(_loadedMidiFilePath);
        }

        if (MidiSequencePlayButton is not null)
        {
            MidiSequencePlayButton.IsEnabled = _midiSequencePlaybackPlan is not null && midiOutputReady && !_isMidiSequencePlaying;
        }

        if (MidiSequenceStopButton is not null)
        {
            MidiSequenceStopButton.IsEnabled = _isMidiSequencePlaying;
        }

        foreach (var button in new[]
                 {
                     MidiNoteOnButton,
                     MidiNoteOffButton,
                     MidiControlChangeButton,
                     MidiPatchChangeButton,
                     MidiPitchWheelButton,
                     MidiRawSendButton,
                     MidiSysexSendButton
                 })
        {
            if (button is not null)
            {
                button.IsEnabled = midiOutputReady;
            }
        }

        if (MidiSoundFontApplyButton is not null)
        {
            MidiSoundFontApplyButton.IsEnabled = soundFontPresetReady;
        }

        if (MidiSoundFontPreviewButton is not null)
        {
            MidiSoundFontPreviewButton.IsEnabled = soundFontPresetReady;
        }

        if (MidiSoundFontSamplePreviewButton is not null)
        {
            MidiSoundFontSamplePreviewButton.IsEnabled = soundFontSampleReady;
        }
    }

    private void StartMidiInputClicked(object sender, RoutedEventArgs e)
    {
        if (MidiInputDeviceComboBox.SelectedItem is not MidiInputDevice device)
        {
            SetMidiStatus("Select a MIDI input first.");
            return;
        }

        try
        {
            _midiInputMonitor.Start(device);
            SetMidiStatus($"MIDI input listening: {device.ProductName}.");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
        finally
        {
            UpdateMidiControlState();
        }
    }

    private void StopMidiInputClicked(object sender, RoutedEventArgs e)
    {
        _midiInputMonitor.Stop();
        SetMidiStatus("MIDI input stopped.");
        UpdateMidiControlState();
    }

    private void ClearMidiMessagesClicked(object sender, RoutedEventArgs e)
    {
        _midiMessages.Clear();
        UpdateSelectedMidiMessageDetails();
        SetMidiStatus("MIDI message monitor cleared.");
    }

    private void OpenMidiOutputClicked(object sender, RoutedEventArgs e)
    {
        if (MidiOutputDeviceComboBox.SelectedItem is not MidiOutputDevice device)
        {
            SetMidiStatus("Select a MIDI output first.");
            return;
        }

        try
        {
            _midiOutputPort.Open(device);
            SetMidiStatus($"MIDI output open: {device.ProductName}.");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
        finally
        {
            UpdateMidiControlState();
        }
    }

    private void CloseMidiOutputClicked(object sender, RoutedEventArgs e)
    {
        StopMidiSequencePlayback("MIDI file test stopped because output closed.", resetOutput: false);
        _midiOutputPort.Close();
        SetMidiStatus("MIDI output closed.");
        UpdateMidiControlState();
    }

    private void MidiPanicClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            StopMidiSequencePlayback("MIDI file test stopped by panic.", resetOutput: false);
            _midiOutputPort.Reset();
            SetMidiStatus("MIDI panic sent.");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
    }

    private void MidiAllNotesOffClicked(object sender, RoutedEventArgs e)
    {
        SendMidiBatchMessages(() => _midiOutputPort.SendAllNotesOff(), "All notes off sent on all channels.");
    }

    private void MidiResetControllersClicked(object sender, RoutedEventArgs e)
    {
        SendMidiBatchMessages(() => _midiOutputPort.SendResetAllControllers(), "Reset all controllers sent on all channels.");
    }

    private void SendMidiNoteOnClicked(object sender, RoutedEventArgs e)
    {
        SendMidiShortMessage(() => _midiOutputPort.SendNoteOn(MidiChannel(), MidiNote(), MidiVelocity()), "MIDI note on sent.");
    }

    private void SendMidiNoteOffClicked(object sender, RoutedEventArgs e)
    {
        SendMidiShortMessage(() => _midiOutputPort.SendNoteOff(MidiChannel(), MidiNote(), MidiVelocity()), "MIDI note off sent.");
    }

    private void SendMidiControlChangeClicked(object sender, RoutedEventArgs e)
    {
        SendMidiShortMessage(
            () => _midiOutputPort.SendControlChange(MidiChannel(), SliderInt(MidiControllerSlider), SliderInt(MidiControllerValueSlider)),
            "MIDI control change sent.");
    }

    private void SendMidiPatchChangeClicked(object sender, RoutedEventArgs e)
    {
        SendMidiShortMessage(() => _midiOutputPort.SendPatchChange(MidiChannel(), SliderInt(MidiPatchSlider)), "MIDI patch change sent.");
    }

    private void SendMidiPitchWheelClicked(object sender, RoutedEventArgs e)
    {
        SendMidiShortMessage(() => _midiOutputPort.SendPitchWheel(MidiChannel(), SliderInt(MidiPitchWheelSlider)), "MIDI pitch wheel sent.");
    }

    private void SendMidiRawClicked(object sender, RoutedEventArgs e)
    {
        if (!MidiHexParser.TryParseShortMessage(MidiRawMessageTextBox.Text, out var rawMessage))
        {
            SetMidiStatus("Raw MIDI message must be 1 to 3 hex bytes.");
            return;
        }

        SendMidiShortMessage(
            () =>
            {
                _midiOutputPort.SendRawMessage(rawMessage);
                return rawMessage;
            },
            "Raw MIDI message sent.");
    }

    private void SendMidiSysexClicked(object sender, RoutedEventArgs e)
    {
        if (!MidiHexParser.TryParseBytes(MidiSysexTextBox.Text, out var bytes)
            || bytes.Length < 2
            || bytes[0] != 0xF0
            || bytes[^1] != 0xF7)
        {
            SetMidiStatus("Sysex must be hex bytes starting with F0 and ending with F7.");
            return;
        }

        try
        {
            _midiOutputPort.SendSysex(bytes);
            AddMidiMessage(MidiMessageSnapshot.FromSysex(bytes, Environment.TickCount, "Out"));
            SetMidiStatus($"MIDI sysex sent: {bytes.Length} bytes.");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
    }

    private void OpenMidiFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MIDI files|*.mid;*.midi|All files|*.*",
            Title = "Open MIDI File"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var summary = MidiFileService.ReadSummary(dialog.FileName);
            var playbackPlan = MidiSequenceService.CreatePlaybackPlan(dialog.FileName);
            StopMidiSequencePlayback("MIDI file test replaced by newly loaded file.", resetOutput: false, updateStatus: false);
            _loadedMidiFilePath = dialog.FileName;
            _midiSequencePlaybackPlan = playbackPlan;
            _midiSequenceTracks.Clear();
            foreach (var track in summary.TrackSummaries)
            {
                _midiSequenceTracks.Add(track);
            }

            MidiFileStatusText.Text = summary.DisplayText;
            MidiSequenceStatusText.Text = playbackPlan.DisplayText;
            SetMidiStatus($"MIDI file loaded: {summary.FileName}.");
        }
        catch (Exception ex)
        {
            StopMidiSequencePlayback("MIDI file test cleared after file load failure.", resetOutput: false, updateStatus: false);
            _loadedMidiFilePath = null;
            _midiSequencePlaybackPlan = null;
            _midiSequenceTracks.Clear();
            MidiSequenceStatusText.Text = "No MIDI file test queued.";
            MidiFileStatusText.Text = $"MIDI file failed: {ex.Message}";
            SetMidiStatus($"MIDI file failed: {ex.Message}");
        }
        finally
        {
            UpdateMidiControlState();
        }
    }

    private void ExportMidiFileCopyClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_loadedMidiFilePath))
        {
            SetMidiStatus("Open a MIDI file before exporting.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "MIDI files|*.mid|All files|*.*",
            FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_loadedMidiFilePath)}_copy.mid",
            Title = "Export MIDI File Copy"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            MidiFileService.ExportCopy(_loadedMidiFilePath, dialog.FileName);
            SetMidiStatus($"MIDI file exported: {System.IO.Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            SetMidiStatus($"MIDI export failed: {ex.Message}");
        }
    }

    private void PlayMidiSequenceClicked(object sender, RoutedEventArgs e)
    {
        if (_midiSequencePlaybackPlan is not MidiSequencePlaybackPlan playbackPlan)
        {
            SetMidiStatus("Open a MIDI file before playing to output.");
            return;
        }

        if (!_midiOutputPort.IsOpen)
        {
            SetMidiStatus("Open a MIDI output before playing the file test.");
            return;
        }

        if (playbackPlan.Events.Count == 0)
        {
            SetMidiStatus("MIDI file has no playable output events.");
            return;
        }

        StopMidiSequencePlayback("Restarting MIDI file test.", resetOutput: true, updateStatus: false);
        var cancellation = new CancellationTokenSource();
        _midiSequencePlaybackCancellation = cancellation;
        _isMidiSequencePlaying = true;
        UpdateMidiControlState();
        MidiSequenceStatusText.Text = $"Playing {playbackPlan.FileName} at {MidiSequenceSpeedRatio() * 100d:0}% speed.";
        _ = PlayMidiSequenceAsync(playbackPlan, MidiSequenceSpeedRatio(), cancellation);
    }

    private void StopMidiSequenceClicked(object sender, RoutedEventArgs e)
    {
        StopMidiSequencePlayback("MIDI file test stopped.");
    }

    private async Task PlayMidiSequenceAsync(MidiSequencePlaybackPlan playbackPlan, double speedRatio, CancellationTokenSource cancellation)
    {
        var sentEvents = 0;
        try
        {
            await Task.Run(async () =>
            {
                var previousOffset = TimeSpan.Zero;
                foreach (var playbackEvent in playbackPlan.Events)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var scaledOffset = ScaleMidiSequenceOffset(playbackEvent.Offset, speedRatio);
                    var delay = scaledOffset - previousOffset;
                    previousOffset = scaledOffset;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
                    }

                    if (playbackEvent.SysexBytes is { Length: > 0 } sysexBytes)
                    {
                        _midiOutputPort.SendSysex(sysexBytes);
                        PostMidiMessage(MidiMessageSnapshot.FromSysex(sysexBytes, Environment.TickCount, "Out"));
                    }
                    else
                    {
                        _midiOutputPort.SendRawMessage(playbackEvent.RawMessage);
                        PostMidiMessage(MidiMessageSnapshot.FromRaw(playbackEvent.RawMessage, Environment.TickCount, "Out"));
                    }

                    sentEvents++;
                    if (sentEvents == 1 || sentEvents % 32 == 0)
                    {
                        PostMidiSequenceStatus($"Playing {playbackPlan.FileName}: {sentEvents}/{playbackPlan.Events.Count} events.");
                    }
                }

                _midiOutputPort.Reset();
            }, cancellation.Token).ConfigureAwait(false);

            await InvokeMidiUiAsync(() =>
            {
                MidiSequenceStatusText.Text = $"File test complete: {sentEvents} events sent.";
                SetMidiStatus($"MIDI file test complete: {playbackPlan.FileName}.");
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await InvokeMidiUiAsync(() => MidiSequenceStatusText.Text = "MIDI file test stopped.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await InvokeMidiUiAsync(() =>
            {
                MidiSequenceStatusText.Text = $"MIDI file test failed: {ex.Message}";
                SetMidiStatus($"MIDI file test failed: {ex.Message}");
            }).ConfigureAwait(false);
        }
        finally
        {
            await InvokeMidiUiAsync(() =>
            {
                var ownsCurrentPlayback = ReferenceEquals(_midiSequencePlaybackCancellation, cancellation);
                if (ownsCurrentPlayback)
                {
                    _midiSequencePlaybackCancellation = null;
                    _isMidiSequencePlaying = false;
                    UpdateMidiControlState();
                }
            }).ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    private void StopMidiSequencePlayback(string status, bool resetOutput = true, bool updateStatus = true)
    {
        var wasPlaying = _isMidiSequencePlaying;
        _midiSequencePlaybackCancellation?.Cancel();
        _midiSequencePlaybackCancellation = null;
        _isMidiSequencePlaying = false;
        if (resetOutput && _midiOutputPort.IsOpen)
        {
            try
            {
                _midiOutputPort.Reset();
            }
            catch
            {
            }
        }

        if (updateStatus && (wasPlaying || MidiSequenceStatusText is not null))
        {
            MidiSequenceStatusText.Text = status;
            if (wasPlaying)
            {
                SetMidiStatus(status);
            }
        }

        UpdateMidiControlState();
    }

    private void MidiSequenceTempoSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MidiSequenceTempoValueText is null)
        {
            return;
        }

        MidiSequenceTempoValueText.Text = $"{Math.Round(e.NewValue):0}%";
        if (_midiSequencePlaybackPlan is not null && !_isMidiSequencePlaying)
        {
            MidiSequenceStatusText.Text = $"{_midiSequencePlaybackPlan.DisplayText} | speed {Math.Round(e.NewValue):0}%";
        }

        if (!_isRestoringMidiState)
        {
            ScheduleAppStatePersist();
        }
    }

    private double MidiSequenceSpeedRatio()
    {
        return Math.Clamp((MidiSequenceTempoSlider?.Value ?? 100d) / 100d, 0.5d, 2d);
    }

    private static TimeSpan ScaleMidiSequenceOffset(TimeSpan offset, double speedRatio)
    {
        return speedRatio <= 0d
            ? offset
            : TimeSpan.FromTicks(Math.Max(0L, (long)Math.Round(offset.Ticks / speedRatio)));
    }

    private void AddMidiControlMappingClicked(object sender, RoutedEventArgs e)
    {
        if (MidiMessageListBox.SelectedItem is not MidiMessageSnapshot message)
        {
            SetMidiStatus("Select a MIDI monitor message before mapping.");
            return;
        }

        if (!string.Equals(message.Direction, "In", StringComparison.Ordinal))
        {
            SetMidiStatus("Select an incoming MIDI message before mapping.");
            return;
        }

        if (message.Channel is null)
        {
            SetMidiStatus("Choose a channel message for control mapping.");
            return;
        }

        var actionName = MidiControlMappingActionComboBox.SelectedItem as string
            ?? MidiControlMappingActions.ToggleSelectedInputMute;
        var rule = MidiControlMappingRule.FromMessage(message, actionName);
        if (_midiControlMappings.Any(existing =>
                string.Equals(existing.ActionName, rule.ActionName, StringComparison.Ordinal)
                && string.Equals(existing.MessageType, rule.MessageType, StringComparison.Ordinal)
                && existing.Channel == rule.Channel
                && existing.Data1 == rule.Data1))
        {
            SetMidiStatus("That MIDI control mapping already exists.");
            return;
        }

        _midiControlMappings.Add(rule);
        ScheduleAppStatePersist();
        SetMidiStatus($"MIDI control mapped: {rule.DisplayName}.");
    }

    private void RemoveMidiControlMappingClicked(object sender, RoutedEventArgs e)
    {
        if (MidiControlMappingsListBox.SelectedItem is not MidiControlMappingRule rule)
        {
            SetMidiStatus("Select a MIDI control mapping to remove.");
            return;
        }

        _midiControlMappings.Remove(rule);
        _midiControlMappingTriggerState.Remove(rule);
        ScheduleAppStatePersist();
        SetMidiStatus($"MIDI control mapping removed: {rule.ActionName}.");
    }

    private void ClearMidiControlMappingsClicked(object sender, RoutedEventArgs e)
    {
        _midiControlMappings.Clear();
        _midiControlMappingTriggerState.Clear();
        ScheduleAppStatePersist();
        SetMidiStatus("MIDI control mappings cleared.");
    }

    private void LoadSoundFontClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SoundFont files|*.sf2|All files|*.*",
            Title = "Open SoundFont"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            StopSoundFontSamplePreview();
            var summary = SoundFontLibrary.LoadSummary(dialog.FileName);
            _loadedMidiSoundFont = summary;
            _midiSoundFontPresets.Clear();
            _midiSoundFontInstruments.Clear();
            _midiSoundFontSamples.Clear();
            foreach (var preset in summary.Presets.Take(256))
            {
                _midiSoundFontPresets.Add(preset);
            }

            foreach (var instrument in summary.Instruments.Take(256))
            {
                _midiSoundFontInstruments.Add(instrument);
            }

            foreach (var sample in summary.Samples.Take(256))
            {
                _midiSoundFontSamples.Add(sample);
            }

            if (_midiSoundFontPresets.Count > 0)
            {
                MidiSoundFontPresetComboBox.SelectedIndex = 0;
            }

            if (_midiSoundFontSamples.Count > 0)
            {
                MidiSoundFontSamplesListBox.SelectedIndex = 0;
            }

            MidiSoundFontStatusText.Text = summary.DisplayText;
            UpdateMidiControlState();
            SetMidiStatus($"SoundFont loaded: {summary.FileName}.");
        }
        catch (Exception ex)
        {
            _loadedMidiSoundFont = null;
            _midiSoundFontPresets.Clear();
            _midiSoundFontInstruments.Clear();
            _midiSoundFontSamples.Clear();
            MidiSoundFontStatusText.Text = $"SoundFont failed: {ex.Message}";
            UpdateMidiControlState();
            SetMidiStatus($"SoundFont failed: {ex.Message}");
        }
    }

    private void ClearSoundFontClicked(object sender, RoutedEventArgs e)
    {
        _loadedMidiSoundFont = null;
        StopSoundFontSamplePreview();
        _midiSoundFontPresets.Clear();
        _midiSoundFontInstruments.Clear();
        _midiSoundFontSamples.Clear();
        MidiSoundFontStatusText.Text = "No SoundFont loaded.";
        UpdateMidiControlState();
        SetMidiStatus("SoundFont cleared.");
    }

    private void MidiSoundFontPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MidiSoundFontPresetComboBox.SelectedItem is not SoundFontPresetSummary preset)
        {
            return;
        }

        MidiPatchSlider.Value = Math.Clamp(preset.Patch, MidiPatchSlider.Minimum, MidiPatchSlider.Maximum);
        UpdateMidiControlState();
        SetMidiStatus($"SoundFont preset selected: {preset.DisplayName}.");
    }

    private void MidiSoundFontSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMidiControlState();
    }

    private void PreviewSoundFontSampleClicked(object sender, RoutedEventArgs e)
    {
        if (_loadedMidiSoundFont is not SoundFontSummary summary
            || MidiSoundFontSamplesListBox.SelectedItem is not SoundFontSampleSummary sample)
        {
            SetMidiStatus("Load a SoundFont and select a sample first.");
            return;
        }

        try
        {
            StopSoundFontSamplePreview();
            _midiSoundFontPreviewStream = SoundFontLibrary.CreateSamplePreviewStream(summary.FilePath, sample.Index);
            _midiSoundFontPreviewOutput = new WaveOutEvent();
            _midiSoundFontPreviewOutput.PlaybackStopped += MidiSoundFontPreviewPlaybackStopped;
            _midiSoundFontPreviewOutput.Init(_midiSoundFontPreviewStream);
            _midiSoundFontPreviewOutput.Play();
            MidiSoundFontStatusText.Text = $"Previewing SF2 sample: {sample.DisplayName}.";
            SetMidiStatus($"SoundFont sample preview started: {sample.DisplayName}.");
        }
        catch (Exception ex)
        {
            StopSoundFontSamplePreview();
            SetMidiStatus($"SoundFont sample preview failed: {ex.Message}");
        }
    }

    private void MidiSoundFontPreviewPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StopSoundFontSamplePreview();
            if (e.Exception is not null)
            {
                SetMidiStatus($"SoundFont sample preview failed: {e.Exception.Message}");
            }
        }));
    }

    private void StopSoundFontSamplePreview()
    {
        if (_midiSoundFontPreviewOutput is not null)
        {
            _midiSoundFontPreviewOutput.PlaybackStopped -= MidiSoundFontPreviewPlaybackStopped;
            try
            {
                _midiSoundFontPreviewOutput.Stop();
            }
            catch
            {
            }

            _midiSoundFontPreviewOutput.Dispose();
            _midiSoundFontPreviewOutput = null;
        }

        _midiSoundFontPreviewStream?.Dispose();
        _midiSoundFontPreviewStream = null;
    }

    private void ApplySoundFontPresetToMidiOutputClicked(object sender, RoutedEventArgs e)
    {
        if (MidiSoundFontPresetComboBox.SelectedItem is not SoundFontPresetSummary preset)
        {
            SetMidiStatus("Load and select a SoundFont preset first.");
            return;
        }

        try
        {
            SendSoundFontPresetSelection(preset);
            SetMidiStatus($"SoundFont bank and patch sent to MIDI output: {preset.DisplayName}.");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
    }

    private void PreviewSoundFontNoteClicked(object sender, RoutedEventArgs e)
    {
        if (MidiSoundFontPresetComboBox.SelectedItem is not SoundFontPresetSummary preset)
        {
            SetMidiStatus("Load and select a SoundFont preset first.");
            return;
        }

        try
        {
            SendSoundFontPresetSelection(preset);
            var noteRaw = _midiOutputPort.SendNoteOn(MidiChannel(), MidiNote(), MidiVelocity());
            AddMidiMessage(MidiMessageSnapshot.FromRaw(noteRaw, Environment.TickCount, "Out"));
            _ = StopMidiPreviewNoteAsync(MidiChannel(), MidiNote());
            SetMidiStatus($"SoundFont preset preview note sent: {preset.DisplayName}.");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
    }

    private void SendSoundFontPresetSelection(SoundFontPresetSummary preset)
    {
        foreach (var rawMessage in _midiOutputPort.SendBankSelect(MidiChannel(), preset.Bank))
        {
            AddMidiMessage(MidiMessageSnapshot.FromRaw(rawMessage, Environment.TickCount, "Out"));
        }

        var patchRaw = _midiOutputPort.SendPatchChange(MidiChannel(), preset.Patch);
        AddMidiMessage(MidiMessageSnapshot.FromRaw(patchRaw, Environment.TickCount, "Out"));
    }

    private async Task StopMidiPreviewNoteAsync(int channel, int note)
    {
        await Task.Delay(450);
        if (_isClosing)
        {
            return;
        }

        try
        {
            var raw = _midiOutputPort.SendNoteOff(channel, note, 0);
            await Dispatcher.BeginInvoke(new Action(() =>
                AddMidiMessage(MidiMessageSnapshot.FromRaw(raw, Environment.TickCount, "Out"))));
        }
        catch
        {
        }
    }

    private void ApplyMidiControlMappings(MidiMessageSnapshot message)
    {
        if (!string.Equals(message.Direction, "In", StringComparison.Ordinal)
            || _midiControlMappings.Count == 0)
        {
            return;
        }

        var triggeredMappings = _midiControlMappingTriggerState.GetTriggeredMappings(_midiControlMappings, message);
        if (triggeredMappings.Count == 0)
        {
            return;
        }

        foreach (var rule in triggeredMappings)
        {
            ExecuteMidiControlMapping(rule);
        }
    }

    private void ExecuteMidiControlMapping(MidiControlMappingRule rule)
    {
        try
        {
            switch (rule.ActionName)
            {
                case MidiControlMappingActions.ToggleSelectedInputMute:
                    if (_activeMicChannel is null)
                    {
                        SetMidiStatus("No selected mixer input to mute.");
                        return;
                    }

                    _activeMicChannel.IsMuted = !_activeMicChannel.IsMuted;
                    UpdateLiveMixControlsFromChannels();
                    ScheduleAppStatePersist();
                    SetMidiStatus($"{_activeMicChannel.DisplayName} mute {(_activeMicChannel.IsMuted ? "on" : "off")} from MIDI.");
                    break;
                case MidiControlMappingActions.ToggleSelectedInputSolo:
                    if (_activeMicChannel is null)
                    {
                        SetMidiStatus("No selected mixer input to solo.");
                        return;
                    }

                    _activeMicChannel.IsSoloed = !_activeMicChannel.IsSoloed;
                    UpdateLiveMixControlsFromChannels();
                    ScheduleAppStatePersist();
                    SetMidiStatus($"{_activeMicChannel.DisplayName} solo {(_activeMicChannel.IsSoloed ? "on" : "off")} from MIDI.");
                    break;
                case MidiControlMappingActions.ToggleProcessedOutput:
                    SetProcessedOutputToggleState(!IsProcessedOutputRequested());
                    UpdateOutputRouting();
                    ScheduleAppStatePersist();
                    SetMidiStatus("Processed output toggled from MIDI.");
                    break;
                case MidiControlMappingActions.StartOrStopRecording:
                    if (_isStandaloneAudioRecording)
                    {
                        StopAudioRecordingClicked(this, new RoutedEventArgs());
                    }
                    else
                    {
                        StartAudioRecordingClicked(this, new RoutedEventArgs());
                    }

                    SetMidiStatus("Recording transport toggled from MIDI.");
                    break;
                case MidiControlMappingActions.SendAllNotesOff:
                    SendMidiBatchMessages(() => _midiOutputPort.SendAllNotesOff(), "All notes off sent from MIDI mapping.");
                    break;
                case MidiControlMappingActions.ResetMidiControllers:
                    SendMidiBatchMessages(() => _midiOutputPort.SendResetAllControllers(), "Reset all controllers sent from MIDI mapping.");
                    break;
                case MidiControlMappingActions.MidiPanic:
                    StopMidiSequencePlayback("MIDI file test stopped by panic.", resetOutput: false);
                    _midiOutputPort.Reset();
                    SetMidiStatus("MIDI panic sent from mapping.");
                    break;
            }
        }
        catch (Exception ex)
        {
            SetMidiStatus($"MIDI mapping failed: {ex.Message}");
        }
    }

    private void SendMidiBatchMessages(Func<IReadOnlyList<int>> send, string status)
    {
        try
        {
            var rawMessages = send();
            foreach (var rawMessage in rawMessages)
            {
                AddMidiMessage(MidiMessageSnapshot.FromRaw(rawMessage, Environment.TickCount, "Out"));
            }

            SetMidiStatus($"{status} ({rawMessages.Count} messages).");
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
    }

    private void SendMidiShortMessage(Func<int> send, string status)
    {
        try
        {
            var rawMessage = send();
            AddMidiMessage(MidiMessageSnapshot.FromRaw(rawMessage, Environment.TickCount, "Out"));
            SetMidiStatus(status);
        }
        catch (Exception ex)
        {
            SetMidiStatus(ex.Message);
        }
    }

    private void MidiMessageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedMidiMessageDetails();
    }

    private void MidiOutputParameterValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateMidiOutputParameterText();
    }

    private void UpdateSelectedMidiMessageDetails()
    {
        if (MidiSelectedMessageDetailsText is null)
        {
            return;
        }

        MidiSelectedMessageDetailsText.Text = MidiMessageListBox?.SelectedItem is MidiMessageSnapshot message
            ? message.InspectionText
            : "No MIDI message selected.";
    }

    private void UpdateMidiDeviceDetails()
    {
        if (MidiInputDeviceDetailsText is not null)
        {
            MidiInputDeviceDetailsText.Text = MidiInputDeviceComboBox?.SelectedItem is MidiInputDevice input
                ? $"{input.ProductName} | {input.Details}"
                : "No MIDI input selected.";
        }

        if (MidiOutputDeviceDetailsText is not null)
        {
            MidiOutputDeviceDetailsText.Text = MidiOutputDeviceComboBox?.SelectedItem is MidiOutputDevice output
                ? $"{output.ProductName} | {output.Details} | {FormatMidiOutputSupport(output)}"
                : "No MIDI output selected.";
        }
    }

    private static string FormatMidiOutputSupport(MidiOutputDevice output)
    {
        var support = new List<string>();
        if (output.SupportsAllChannels)
        {
            support.Add("all channels");
        }

        if (output.SupportsVolumeControl)
        {
            support.Add("volume");
        }

        if (output.SupportsSeparateLeftAndRightVolume)
        {
            support.Add("L/R volume");
        }

        if (output.SupportsPatchCaching)
        {
            support.Add("patch cache");
        }

        if (output.SupportsMidiStreamOut)
        {
            support.Add("stream out");
        }

        return support.Count == 0 ? "basic output" : string.Join(", ", support);
    }

    private void UpdateMidiOutputParameterText()
    {
        if (MidiNoteValueText is not null && MidiNoteSlider is not null)
        {
            var note = SliderInt(MidiNoteSlider);
            MidiNoteValueText.Text = FormatMidiNumberWithLabel(note, MidiMessageSnapshot.FormatNoteName(note));
        }

        if (MidiControllerValueText is not null && MidiControllerSlider is not null)
        {
            var controller = SliderInt(MidiControllerSlider);
            MidiControllerValueText.Text = FormatMidiNumberWithLabel(controller, MidiMessageSnapshot.FormatControllerName(controller));
        }

        if (MidiPitchWheelValueText is not null && MidiPitchWheelSlider is not null)
        {
            var pitchValue = SliderInt(MidiPitchWheelSlider);
            MidiPitchWheelValueText.Text = MidiMessageSnapshot.FormatPitchWheelValue(pitchValue & 0x7F, (pitchValue >> 7) & 0x7F);
        }
    }

    private static string FormatMidiNumberWithLabel(int value, string label)
    {
        return string.IsNullOrWhiteSpace(label)
            ? value.ToString(CultureInfo.InvariantCulture)
            : $"{value} {label}";
    }

    private int MidiChannel() => SliderInt(MidiChannelSlider);

    private int MidiNote() => SliderInt(MidiNoteSlider);

    private int MidiVelocity() => SliderInt(MidiVelocitySlider);

    private static int SliderInt(Slider slider)
    {
        return (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero);
    }

    private void PodcastSettingsMenuClicked(object sender, RoutedEventArgs e)
    {
        SelectMainTabFromMenu("Podcast", "Podcast settings selected.");
    }

    private void KaraokeSettingsMenuClicked(object sender, RoutedEventArgs e)
    {
        SelectMainTabFromMenu("Karaoke", "Karaoke settings selected.");
    }

    private void AudioDeviceDiagnosticsMenuClicked(object sender, RoutedEventArgs e)
    {
        var report = BuildAudioDeviceDiagnosticsReport();
        ShowAudioDeviceDiagnosticsDialog(report);
        StatusText.Text = "Audio device diagnostics opened.";
    }

    private string BuildAudioDeviceDiagnosticsReport(AudioInputDevice? selectedInputOverride = null)
    {
        var selectedInput = _activeMicChannel?.SelectedDevice ?? _selectedDevice;
        if (selectedInputOverride is not null)
        {
            selectedInput = selectedInputOverride;
        }

        var selectedOutput = _selectedOutputDevice;
        var inputFormat = ReferenceEquals(selectedInput, _selectedDevice)
            ? _selectedDeviceFormat ?? GetSelectedDeviceFormat()
            : selectedInput is null
                ? null
                : MicrophoneSpectrumService.TryGetInputDeviceFormat(selectedInput);
        var outputFormat = selectedOutput is null
            ? null
            : MicrophoneSpectrumService.TryGetOutputDeviceFormat(selectedOutput);
        var inputDevices = MicrophoneSpectrumService.GetInputDevices();
        var outputDevices = MicrophoneSpectrumService.GetOutputDevices();
        var report = AudioDeviceDiagnostics.BuildReport(
            selectedInput,
            selectedOutput,
            inputFormat,
            outputFormat,
            inputDevices,
            outputDevices,
            selectedInput?.IsAsio == true ? _spectrumService.ActiveAsioInputDiagnostics : null,
            selectedInput?.IsAsio == true ? _spectrumService.ActiveInputFormatStatus : null);
        return report;
    }

    private void ShowAsioNoCallbackDiagnosticsOnce(AudioInputDevice selectedDevice)
    {
        var diagnosticKey = selectedDevice.EndpointId ?? selectedDevice.Name;
        if (string.Equals(_lastShownAsioNoCallbackDiagnosticKey, diagnosticKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastShownAsioNoCallbackDiagnosticKey = diagnosticKey;
        var report = BuildAudioDeviceDiagnosticsReport(selectedDevice);
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosing)
            {
                return;
            }

            ShowAudioDeviceDiagnosticsDialog(report);
        }, DispatcherPriority.Background);
    }

    private bool IsAsioNoCallbackAutoStartSuppressed(AudioInputDevice selectedDevice)
    {
        return selectedDevice.IsAsio
            && string.Equals(
                _asioNoCallbackAutoStartSuppressedDeviceKey,
                CreateAsioNoCallbackSuppressionKey(selectedDevice),
                StringComparison.Ordinal);
    }

    private void ClearAsioNoCallbackAutoStartSuppression()
    {
        _asioNoCallbackAutoStartSuppressedDeviceKey = null;
    }

    private static string CreateAsioNoCallbackSuppressionKey(AudioInputDevice selectedDevice)
    {
        return $"{selectedDevice.Backend}|{selectedDevice.EndpointId}|{selectedDevice.Name}";
    }

    private static string CreateAsioNoCallbackSuppressedStatus()
    {
        return "ASIO input stopped after no driver callbacks. Diagnostics are open; refresh or reselect the input to try again.";
    }

    private void ShowAudioDeviceDiagnosticsDialog(string report)
    {
        var reportBox = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Background = new SolidColorBrush(Color.FromRgb(8, 16, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 75, 92)),
            Padding = new Thickness(12)
        };
        var closeButton = new Button
        {
            Content = "Close",
            Width = 96,
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var content = new Grid
        {
            Margin = new Thickness(18)
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new TextBlock
        {
            Text = "Audio Device Diagnostics",
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        Grid.SetRow(reportBox, 1);
        content.Children.Add(reportBox);
        Grid.SetRow(closeButton, 2);
        content.Children.Add(closeButton);

        var dialog = new Window
        {
            Title = "Audio Device Diagnostics",
            Owner = this,
            Width = Math.Min(920d, Math.Max(720d, ActualWidth * 0.62d)),
            Height = Math.Min(760d, Math.Max(540d, ActualHeight * 0.72d)),
            MinWidth = 640d,
            MinHeight = 460d,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)),
            Content = content,
            ShowInTaskbar = false
        };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.SourceInitialized += (_, _) => ApplyDarkTitleBar(dialog);
        dialog.ShowDialog();
    }

    private void SelectMainTabFromMenu(string header, string status)
    {
        if (MainTabControl is null)
        {
            StatusText.Text = $"{header} settings unavailable.";
            return;
        }

        var tab = MainTabControl.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase));
        if (tab is null)
        {
            StatusText.Text = $"{header} settings unavailable.";
            return;
        }

        MainTabControl.SelectedItem = tab;
        StatusText.Text = status;
    }

    private void RefreshAudioDevicesMenuClicked(object sender, RoutedEventArgs e)
    {
        RefreshAudioDevicesFromSystem();
    }

    private void RefreshVideoDevicesMenuClicked(object sender, RoutedEventArgs e)
    {
        RefreshVideoDevicesFromSystem();
    }

    private void RefreshMidiDevicesMenuClicked(object sender, RoutedEventArgs e)
    {
        RefreshMidiDevicesFromSystem();
    }

    private void EnableMidiChanged(object sender, RoutedEventArgs e)
    {
        if (_isClosing || _isUpdatingMidiEnabledUi || EnableMidiMenuItem is null)
        {
            return;
        }

        _midiEnabled = EnableMidiMenuItem.IsChecked;
        ApplyMidiEnabledState(refreshDevices: _midiEnabled, persist: !_isRestoringAppState);
    }

    private void RefreshMidiDevicesClicked(object sender, RoutedEventArgs e)
    {
        RefreshMidiDevicesFromSystem();
    }

    private void AsioSettingsMenuClicked(object sender, RoutedEventArgs e)
    {
        var endpointId = ResolvePreferredAsioSettingsEndpointId();
        var opened = MicrophoneSpectrumService.TryShowAsioControlPanel(endpointId, out var status);
        StatusText.Text = status;
        if (opened && OutputStatusText is not null && _selectedOutputDevice?.IsAsio == true)
        {
            OutputStatusText.Text = status;
        }

        if (opened && KaraokeMonitorStatusText is not null && _selectedOutputDevice?.IsAsio == true)
        {
            KaraokeMonitorStatusText.Text = status;
        }
    }

    private async void AsioCallbackTestMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedInput = _activeMicChannel?.SelectedDevice ?? _selectedDevice;
        var endpointId = selectedInput?.IsAsio == true
            ? selectedInput.EndpointId
            : ResolvePreferredAsioSettingsEndpointId();
        if (!MicrophoneSpectrumService.TryGetAsioDriverName(endpointId, out var driverName))
        {
            const string status = "Select an ASIO input or output before running the callback test.";
            StatusText.Text = status;
            MessageBox.Show(this, status, "ASIO Callback Test", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var activeAsioDiagnostics = _spectrumService.ActiveAsioInputDiagnostics;
        var sampleRate = activeAsioDiagnostics?.RequestedSampleRate
            ?? _selectedDeviceFormat?.SampleRate
            ?? 48_000;
        var shouldRestartSelectedInput = selectedInput?.IsAsio == true;
        if (selectedInput?.IsAsio == true && activeAsioDiagnostics is not null)
        {
            _asioNoCallbackAutoStartSuppressedDeviceKey = CreateAsioNoCallbackSuppressionKey(selectedInput);
            _spectrumService.Stop();
            ClearLiveSpectrumDisplay();
        }

        StatusText.Text = $"Running ASIO callback test for {driverName}...";
        try
        {
            var report = await Task.Run(() => AsioCallbackProbe.BuildReport(
                driverName,
                sampleRate,
                TimeSpan.FromMilliseconds(900)));
            if (_isClosing)
            {
                return;
            }

            ShowAudioDeviceDiagnosticsDialog(report);
            ClearAsioNoCallbackAutoStartSuppression();
            _lastShownAsioNoCallbackDiagnosticKey = null;
            if (shouldRestartSelectedInput)
            {
                StartSelectedDevice();
            }

            StatusText.Text = $"ASIO callback test finished for {driverName}.";
        }
        catch (Exception ex)
        {
            var report = $"ASIO Callback Test\r\nGenerated: {DateTime.Now:G}\r\n\r\nDriver: {driverName}\r\nError: {ex.Message}";
            ShowAudioDeviceDiagnosticsDialog(report);
            ClearAsioNoCallbackAutoStartSuppression();
            StatusText.Text = $"ASIO callback test failed: {ex.Message}";
        }
    }

    private void AboutMenuClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About Jericho Down",
            Owner = this,
            Width = Math.Min(1380d, Math.Max(980d, ActualWidth * 0.82d)),
            Height = Math.Min(860d, Math.Max(620d, ActualHeight * 0.82d)),
            MinWidth = 900d,
            MinHeight = 560d,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)),
            Content = new AboutView(),
            ShowInTaskbar = false
        };

        dialog.ShowDialog();
    }

    private void VerificationMenuClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Jericho Down DSP Verification",
            Owner = this,
            Width = Math.Min(1240d, Math.Max(900d, ActualWidth * 0.74d)),
            Height = Math.Min(820d, Math.Max(600d, ActualHeight * 0.78d)),
            MinWidth = 860d,
            MinHeight = 560d,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)),
            Content = new VerificationView(),
            ShowInTaskbar = false
        };

        dialog.ShowDialog();
    }

    private void PodcastHelpMenuClicked(object sender, RoutedEventArgs e)
    {
        OpenHelpPdf("Podcast Help", "jericho-down-podcast-guide.pdf");
    }

    private void KaraokeHelpMenuClicked(object sender, RoutedEventArgs e)
    {
        OpenHelpPdf("Karaoke Help", "jericho-down-karaoke-guide.pdf");
    }

    private void MicDspHelpMenuClicked(object sender, RoutedEventArgs e)
    {
        OpenHelpPdf("Mic / DSP Help", "jericho-down-mic-dsp-guide.pdf");
    }

    private void MixingHelpMenuClicked(object sender, RoutedEventArgs e)
    {
        OpenHelpPdf("Mixing Help", "jericho-down-mixing-guide.pdf");
    }

    private void MidiHelpMenuClicked(object sender, RoutedEventArgs e)
    {
        OpenHelpPdf("MIDI Help", "jericho-down-midi-guide.pdf");
    }

    private void OpenHelpPdf(string title, string fileName)
    {
        var helpPath = ResolveHelpPdfPath(fileName);
        if (string.IsNullOrWhiteSpace(helpPath))
        {
            MessageBox.Show(
                this,
                $"The {title} PDF could not be found. Rebuild Jericho Down and try again.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = helpPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Unable to open the {title} PDF.\n\n{ex.Message}",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string? ResolveHelpPdfPath(string fileName)
    {
        var outputPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Docs", fileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Docs",
            fileName));
        return File.Exists(projectPath) ? projectPath : null;
    }

    private string? ResolvePreferredAsioSettingsEndpointId()
    {
        var selectedEndpoint = ResolvePreferredAsioSettingsEndpointId(
            _activeMicChannel?.SelectedDevice,
            _selectedOutputDevice,
            [],
            []);
        if (!string.IsNullOrWhiteSpace(selectedEndpoint))
        {
            return selectedEndpoint;
        }

        var outputDevices = MicrophoneSpectrumService.GetOutputDevices();
        var outputEndpoint = outputDevices.FirstOrDefault(device => device.IsAsio)?.EndpointId;
        if (!string.IsNullOrWhiteSpace(outputEndpoint))
        {
            return outputEndpoint;
        }

        var inputDevices = MicrophoneSpectrumService.GetInputDevices();
        return inputDevices.FirstOrDefault(device => device.IsAsio)?.EndpointId;
    }

    private static string? ResolvePreferredAsioSettingsEndpointId(
        AudioInputDevice? selectedInput,
        AudioOutputDevice? selectedOutput,
        IEnumerable<AudioInputDevice> inputDevices,
        IEnumerable<AudioOutputDevice> outputDevices)
    {
        if (selectedOutput?.IsAsio == true)
        {
            return selectedOutput.EndpointId;
        }

        if (selectedInput?.IsAsio == true)
        {
            return selectedInput.EndpointId;
        }

        return outputDevices.FirstOrDefault(device => device.IsAsio)?.EndpointId
            ?? inputDevices.FirstOrDefault(device => device.IsAsio)?.EndpointId;
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        _audioStreamOperationVersion++;
        System.Threading.Interlocked.Increment(ref _cameraPreviewStartOperationVersion);
        System.Threading.Interlocked.Increment(ref _cameraServiceStopOperationVersion);

        TryShutdownStep(SaveAppStateNow);
        TryShutdownStep(() => CompositionTarget.Rendering -= CompositionTargetRendering);
        TryShutdownStep(StopShutdownTimers);
        TryShutdownStep(DisposeAudioDeviceNotificationWatcher);
        TryShutdownStep(() =>
        {
            _spectrumService.SpectrumAvailable -= SpectrumAvailable;
            _spectrumService.StreamStatusChanged -= SpectrumServiceStreamStatusChanged;
            _midiInputMonitor.MessageReceived -= MidiInputMonitorMessageReceived;
            _midiInputMonitor.StatusChanged -= MidiInputMonitorStatusChanged;
            _cameraPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
            _cameraPreviewService.StatusChanged -= CameraPreviewStatusChanged;
            _directShowPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
            _directShowPreviewService.StatusChanged -= CameraPreviewStatusChanged;
        });
        TryShutdownStep(() => StopMidiSequencePlayback("MIDI file test stopped.", resetOutput: true, updateStatus: false));
        TryShutdownStep(StopSoundFontSamplePreview);
        TryShutdownStep(() => _midiInputMonitor.Dispose());
        TryShutdownStep(() => _midiOutputPort.Dispose());
        TryShutdownStep(DisposeAudioRecordingFolderWatcher);
        TryShutdownStep(DisposeKaraokeRecordingFolderWatcher);
        TryShutdownStep(DisposeSessionFolderWatcher);
        TryShutdownStep(StopAudioPlayback);
        TryShutdownStep(StopKaraokeRecordingPlayback);
        TryShutdownStep(() => StopKaraokePlayback(clearTrack: false));
        TryShutdownStep(() =>
        {
            if (_isKaraokeVocalRecording)
            {
                _spectrumService.StopProcessedAudioRecording();
                _isKaraokeVocalRecording = false;
            }
        });
        TryShutdownStep(StopSessionPlayback);
        TryShutdownStep(() =>
        {
            StopTextureNativeCameraStream();
            StopPreviewServices();
            _isCameraServiceStopPending = false;
            _isDirectShowPreviewActive = false;
        });
        TryShutdownStep(() => _directShowPreviewService.Dispose());
        TryShutdownStep(() => Dx12Camera.CloseActive(collectGarbage: true));
        TryShutdownStep(() => _cameraModeLoadCancellation?.Cancel());
        TryShutdownStep(() => _cameraModeLoadCancellation?.Dispose());
        _cameraModeLoadCancellation = null;
        TryShutdownStep(() => DisposeGraphHost(ref _waveform3DGraphHost));
        TryShutdownStep(() => DisposeGraphHost(ref _selectedMicSpectrumGraphHost));
        TryShutdownStep(() => DisposeGraphHost(ref _podcastSpectrumWaterfallGraphHost));
        TryShutdownStep(() => DisposeGraphHost(ref _karaokeSpectrumWaterfallGraphHost));
        TryShutdownStep(() => DisposeGraphHost(ref _mixingMicSpectrumGraphHost));
        TryShutdownStep(() => DisposeGraphHost(ref _mixingOutputWaveform3DGraphHost));
        TryShutdownStep(() =>
        {
            _textureNativeRecordingSession?.Dispose();
            _textureNativeRecordingSession = null;
        });
        TryShutdownStep(() => _cameraPreviewService.Dispose());
        TryShutdownStep(() => _spectrumService.Dispose());
        TryShutdownStep(() => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background));
    }

    private void StopShutdownTimers()
    {
        StopDispatcherTimer(_audioDeviceFormatTimer, AudioDeviceFormatTimerTick);
        StopDispatcherTimer(_audioDeviceRefreshTimer, AudioDeviceRefreshTimerTick);
        StopDispatcherTimer(_appStatePersistTimer, AppStatePersistTimerTick);
        StopDispatcherTimer(_outputAudioSessionTimer, OutputAudioSessionTimerTick);
        StopDispatcherTimer(_sessionPlaybackPositionTimer, SessionPlaybackPositionTimerTick);
        StopDispatcherTimer(_karaokePlaybackPositionTimer, KaraokePlaybackPositionTimerTick);
    }

    private static void StopDispatcherTimer(DispatcherTimer timer, EventHandler handler)
    {
        timer.Stop();
        timer.Tick -= handler;
    }

    private static void TryShutdownStep(Action action)
    {
        try
        {
            action();
        }
        catch
        {
        }
    }

    private static void DisposeGraphHost(ref Direct3D12AudioGraphHost? graphHost)
    {
        graphHost?.Dispose();
        graphHost = null;
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void LeftControlRailToggleClicked(object sender, RoutedEventArgs e)
    {
        _isLeftControlRailCollapsed = !_isLeftControlRailCollapsed;
        ApplyLeftControlRailLayout();
        PersistAppState();
    }

    private void ApplyLeftControlRailLayout()
    {
        var leftWidth = _isLeftControlRailCollapsed
            ? CollapsedControlRailWidth
            : ExpandedControlRailWidth;
        var centerMargin = new Thickness(leftWidth, 0, RightControlRailWidth, 0);

        LeftControlRail.Width = leftWidth;
        LeftControlContent.Visibility = _isLeftControlRailCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        LeftControlRailToggle.Content = _isLeftControlRailCollapsed ? ">" : "<";

        SpectrumCanvas.Margin = centerMargin;
        AnalyzerToolbar.Margin = centerMargin;
        EqualizerFaceplate.Margin = new Thickness(
            leftWidth + EqualizerFaceplateOuterGap,
            0,
            RightControlRailWidth + EqualizerFaceplateOuterGap,
            EqualizerFaceplateOuterGap);
        UpdateEqualizerGuideLayout(leftWidth);
    }

    private void UpdateEqualizerGuideLayout()
    {
        var leftWidth = _isLeftControlRailCollapsed
            ? CollapsedControlRailWidth
            : ExpandedControlRailWidth;
        UpdateEqualizerGuideLayout(leftWidth);
    }

    private void UpdateEqualizerGuideLayout(double leftWidth)
    {
        if (EqVoiceZoneGuideCanvas is null || EqualizerFaceplate is null)
        {
            return;
        }

        var faceplateHeight = EqualizerFaceplate.ActualHeight;
        if (faceplateHeight <= 1d)
        {
            faceplateHeight = 252d;
        }

        var guideHeight = EqVoiceZoneGuideCanvas.ActualHeight > 1d
            ? EqVoiceZoneGuideCanvas.ActualHeight
            : 30d;
        var guideBottomMargin = EqualizerFaceplateOuterGap + faceplateHeight + 4d;
        InlineWaveform3DHost.Margin = new Thickness(
            leftWidth,
            58,
            RightControlRailWidth,
            guideBottomMargin + guideHeight + 6d);
        EqVoiceZoneGuideCanvas.Margin = new Thickness(
            leftWidth + EqualizerFaceplateOuterGap,
            0,
            RightControlRailWidth + EqualizerFaceplateOuterGap,
            guideBottomMargin);
        Dispatcher.BeginInvoke(new Action(UpdateEqVoiceZoneGuide), DispatcherPriority.Render);
    }

    private void BrowseOutputFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose podcast output folder",
            InitialDirectory = Directory.Exists(_outputFolder)
                ? _outputFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _outputFolder = dialog.FolderName;
        UpdateOutputFolderText();
        PersistAppState();
    }

    private void BrowseAudioRecordingFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose audio recording folder",
            InitialDirectory = Directory.Exists(_audioRecordingFolder)
                ? _audioRecordingFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _audioRecordingFolder = dialog.FolderName;
        UpdateAudioRecordingFolderText();
        PersistAppState();
    }

    private void StartAudioRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (_isStandaloneAudioRecording)
        {
            return;
        }

        if (_spectrumService.IsProcessedAudioRecording)
        {
            AudioRecordingStatusText.Text = "Another audio recording is already running.";
            UpdateStandaloneAudioRecordingTransportControls();
            UpdateKaraokeTransportControls();
            return;
        }

        ConfigureLiveMixFromChannels();
        var sourceLabel = GetAudioRecordingSourceLabel();
        if (_audioRecordingSource != ProcessedRecordingSource.ProgramMix
            && _activeMicChannel?.SelectedDevice is null)
        {
            AudioRecordingStatusText.Text = $"Assign {_activeMicChannel?.DisplayName ?? "the selected mic"} before recording {sourceLabel}.";
            return;
        }

        if (!_spectrumService.IsRunning)
        {
            StartSelectedDevice();
            if (!_spectrumService.IsRunning)
            {
                AudioRecordingStatusText.Text = $"Assign and enable a mic before recording {sourceLabel}.";
                return;
            }
        }

        try
        {
            var path = CreateAudioRecordingFilePath(DateTime.Now);
            _spectrumService.StartProcessedAudioRecording(path);
            _activeAudioRecordingPath = path;
            _activeAudioRecordingSourceLabel = sourceLabel;
            _audioRecordingStartedAt = DateTime.UtcNow;
            _audioRecordingPausedAt = default;
            _audioRecordingPausedDuration = TimeSpan.Zero;
            _isStandaloneAudioRecording = true;
            _isStandaloneAudioRecordingPaused = false;
            AudioRecordingStatusText.Text = $"Recording {sourceLabel}: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            AudioRecordingStatusText.Text = $"Audio recording failed: {ex.Message}";
            _activeAudioRecordingPath = null;
            _activeAudioRecordingSourceLabel = "program mix";
            _isStandaloneAudioRecording = false;
            _isStandaloneAudioRecordingPaused = false;
        }

        UpdateStandaloneAudioRecordingTransportControls();
        UpdateKaraokeTransportControls();
    }

    private void PauseAudioRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (!_isStandaloneAudioRecording)
        {
            return;
        }

        if (_isStandaloneAudioRecordingPaused)
        {
            _audioRecordingPausedDuration += DateTime.UtcNow - _audioRecordingPausedAt;
            _isStandaloneAudioRecordingPaused = false;
            _spectrumService.ResumeProcessedAudioRecording();
            AudioRecordingStatusText.Text = $"Recording {_activeAudioRecordingSourceLabel} resumed.";
        }
        else
        {
            _audioRecordingPausedAt = DateTime.UtcNow;
            _isStandaloneAudioRecordingPaused = true;
            _spectrumService.PauseProcessedAudioRecording();
            AudioRecordingStatusText.Text = $"Recording {_activeAudioRecordingSourceLabel} paused.";
        }

        UpdateStandaloneAudioRecordingTransportControls();
        UpdateKaraokeTransportControls();
    }

    private void StopAudioRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (!_isStandaloneAudioRecording)
        {
            return;
        }

        var elapsed = GetStandaloneAudioRecordingElapsed();
        var savedPath = _spectrumService.StopProcessedAudioRecording() ?? _activeAudioRecordingPath;
        _activeAudioRecordingPath = null;
        _audioRecordingStartedAt = default;
        _audioRecordingPausedAt = default;
        _audioRecordingPausedDuration = TimeSpan.Zero;
        _isStandaloneAudioRecording = false;
        _isStandaloneAudioRecordingPaused = false;
        var sourceLabel = _activeAudioRecordingSourceLabel;
        _activeAudioRecordingSourceLabel = "program mix";

        AudioRecordingStatusText.Text = string.IsNullOrWhiteSpace(savedPath)
            ? $"Audio recording stopped at {FormatDuration(elapsed)}."
            : $"Saved {sourceLabel} recording {System.IO.Path.GetFileName(savedPath)} ({FormatDuration(elapsed)}).";
        _lastAudioRecordingPath = savedPath;
        RefreshAudioRecordingFiles(savedPath);
        PersistAppState();
        UpdateStandaloneAudioRecordingTransportControls();
        UpdateKaraokeTransportControls();
    }

    private void PlayAudioFileClicked(object sender, RoutedEventArgs e)
    {
        if (_audioPlaybackOutput is not null)
        {
            StopAudioPlayback();
            AudioRecordingStatusText.Text = "Playback stopped.";
            UpdateStandaloneAudioRecordingTransportControls();
            return;
        }

        RefreshAudioRecordingFiles(GetSelectedAudioRecordingPath());
        var selectedPath = GetSelectedAudioRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            AudioRecordingStatusText.Text = "Choose a saved recording above.";
            return;
        }

        StartAudioPlayback(selectedPath);
    }

    private void RecordingFilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _lastAudioRecordingPath = GetSelectedAudioRecordingPath();
        PersistAppState();
        UpdateStandaloneAudioRecordingTransportControls();
        UpdateSelectedAudioRecordingAnalysisStatus();
    }

    private void RecordingFileDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        var selectedPath = GetSelectedAudioRecordingPath();
        if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
        {
            StartAudioPlayback(selectedPath);
        }
    }

    private void RecordingFileRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
            e.Handled = false;
        }
    }

    private void PlaySelectedRecordingMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = GetSelectedAudioRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            AudioRecordingStatusText.Text = "Choose a saved recording above.";
            return;
        }

        StartAudioPlayback(selectedPath);
    }

    private void ShowSelectedRecordingPropertiesMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = GetSelectedAudioRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            AudioRecordingStatusText.Text = "Choose a saved recording above.";
            return;
        }

        try
        {
            ShowWindowsFileProperties(selectedPath);
            AudioRecordingStatusText.Text = $"Opened properties for {System.IO.Path.GetFileName(selectedPath)}.";
        }
        catch (Exception ex)
        {
            AudioRecordingStatusText.Text = $"Properties failed: {ex.Message}";
        }
    }

    private void ExportSelectedRecordingMp3MenuClicked(object sender, RoutedEventArgs e)
    {
        ExportSelectedAudioRecording(AudioRecordingExportFormat.Mp3);
    }

    private void ExportSelectedRecordingAacMenuClicked(object sender, RoutedEventArgs e)
    {
        ExportSelectedAudioRecording(AudioRecordingExportFormat.Aac);
    }

    private void ExportSelectedRecordingWmaMenuClicked(object sender, RoutedEventArgs e)
    {
        ExportSelectedAudioRecording(AudioRecordingExportFormat.Wma);
    }

    private void ExportSelectedAudioRecording(AudioRecordingExportFormat format)
    {
        var selectedPath = GetSelectedAudioRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            AudioRecordingStatusText.Text = "Choose a saved recording above.";
            return;
        }

        try
        {
            if (!TryChooseAudioExportPath(selectedPath, format, out var exportPath))
            {
                return;
            }

            var info = AudioRecordingExporter.GetFormatInfo(format);
            AudioRecordingStatusText.Text = $"Exporting {info.DisplayName}: {System.IO.Path.GetFileName(selectedPath)}";
            AudioRecordingExporter.Export(selectedPath, exportPath, format);
            _lastAudioRecordingPath = exportPath;
            RefreshAudioRecordingFiles(exportPath);
            AudioRecordingStatusText.Text = $"Exported {info.DisplayName}: {System.IO.Path.GetFileName(exportPath)}.";
        }
        catch (Exception ex)
        {
            AudioRecordingStatusText.Text = $"Export failed: {ex.Message}";
        }

        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void DeleteSelectedRecordingMenuClicked(object sender, RoutedEventArgs e)
    {
        DeleteSelectedAudioRecording();
    }

    private bool TryChooseAudioExportPath(string sourcePath, AudioRecordingExportFormat format, out string exportPath)
    {
        var info = AudioRecordingExporter.GetFormatInfo(format);
        var defaultPath = AudioRecordingExporter.GetDefaultExportPath(sourcePath, format);
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = info.Extension.TrimStart('.'),
            FileName = System.IO.Path.GetFileName(defaultPath),
            Filter = $"{info.SaveDialogFilter}|All files|*.*",
            InitialDirectory = System.IO.Path.GetDirectoryName(defaultPath),
            OverwritePrompt = true,
            Title = $"Export {info.DisplayName}"
        };

        if (dialog.ShowDialog(this) != true)
        {
            exportPath = string.Empty;
            return false;
        }

        exportPath = AudioRecordingExporter.IsSupportedExportExtension(dialog.FileName)
            ? dialog.FileName
            : System.IO.Path.ChangeExtension(dialog.FileName, info.Extension);
        return true;
    }

    private void OpenAudioRecordingLocationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_audioRecordingFolder);
            var selectedPath = GetSelectedAudioRecordingPath();
            if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
            {
                if (!PathSafety.IsRegularFileUnderFolder(selectedPath, _audioRecordingFolder, AudioRecordingCatalog.SupportedRecordingExtensions))
                {
                    AudioRecordingStatusText.Text = "Open location blocked: selected recording is outside the recording folder.";
                    return;
                }

                PathSafety.RevealFileInExplorer(selectedPath);
                AudioRecordingStatusText.Text = $"Opened location for {System.IO.Path.GetFileName(selectedPath)}.";
                return;
            }

            PathSafety.OpenFolderInExplorer(_audioRecordingFolder);
            AudioRecordingStatusText.Text = "Opened recording folder.";
        }
        catch (Exception ex)
        {
            AudioRecordingStatusText.Text = $"Open location failed: {ex.Message}";
        }

        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void DeleteSelectedAudioRecording()
    {
        var selectedPath = GetSelectedAudioRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            AudioRecordingStatusText.Text = "Choose a saved recording to delete.";
            return;
        }

        if (!PathSafety.IsRegularFileUnderFolder(selectedPath, _audioRecordingFolder, AudioRecordingCatalog.SupportedRecordingExtensions))
        {
            AudioRecordingStatusText.Text = "Delete blocked: selected recording is outside the recording folder.";
            return;
        }

        var fileName = System.IO.Path.GetFileName(selectedPath);
        var result = MessageBox.Show(
            this,
            $"Delete this recording?{Environment.NewLine}{fileName}",
            "Delete recording",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (string.Equals(_audioPlaybackPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                StopAudioPlayback();
            }

            File.Delete(selectedPath);
            if (string.Equals(_lastAudioRecordingPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                _lastAudioRecordingPath = null;
            }

            RefreshAudioRecordingFiles();
            AudioRecordingStatusText.Text = $"Deleted {fileName}.";
        }
        catch (Exception ex)
        {
            AudioRecordingStatusText.Text = $"Delete failed: {ex.Message}";
        }

        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void SessionFilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _lastSessionRecordingPath = GetSelectedSessionRecordingPath();
        PersistAppState();
        UpdateSessionPlaybackTransportControls();
    }

    private void SessionFileDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        var selectedPath = GetSelectedSessionRecordingPath();
        if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
        {
            StartSessionPlayback(selectedPath);
        }
    }

    private void SessionFileRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
            e.Handled = false;
        }
    }

    private void PlaySessionFileClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_sessionPlaybackPath))
        {
            if (_isSessionPlaybackEnded)
            {
                PlayLoadedSessionPlayback(restartIfAtEnd: true);
                RecordingStatusText.Text = $"Playing {System.IO.Path.GetFileName(_sessionPlaybackPath)}.";
                UpdateSessionPlaybackTransportControls();
                return;
            }

            StopSessionPlayback();
            RecordingStatusText.Text = "Session playback stopped.";
            UpdateSessionPlaybackTransportControls();
            return;
        }

        RefreshSessionRecordings(GetSelectedSessionRecordingPath());
        var selectedPath = GetSelectedSessionRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            RecordingStatusText.Text = "Choose a saved session above.";
            return;
        }

        StartSessionPlayback(selectedPath);
    }

    private void PlaySelectedSessionMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = GetSelectedSessionRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            RecordingStatusText.Text = "Choose a saved session above.";
            return;
        }

        StartSessionPlayback(selectedPath);
    }

    private void ShowSelectedSessionPropertiesMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = GetSelectedSessionRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            RecordingStatusText.Text = "Choose a saved session above.";
            return;
        }

        try
        {
            ShowWindowsFileProperties(selectedPath);
            RecordingStatusText.Text = $"Opened properties for {System.IO.Path.GetFileName(selectedPath)}.";
        }
        catch (Exception ex)
        {
            RecordingStatusText.Text = $"Properties failed: {ex.Message}";
        }
    }

    private void DeleteSelectedSessionMenuClicked(object sender, RoutedEventArgs e)
    {
        DeleteSelectedSessionRecording();
    }

    private void OpenSessionLocationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_outputFolder);
            var selected = GetSelectedSessionRecording();
            if (selected is not null && File.Exists(selected.Path))
            {
                if (!PathSafety.IsDirectoryUnderFolder(selected.SessionFolder, _outputFolder)
                    || !PathSafety.IsRegularFileUnderFolder(selected.Path, selected.SessionFolder, ".mp4"))
                {
                    RecordingStatusText.Text = "Open location blocked: selected session is outside the output folder.";
                    return;
                }

                PathSafety.RevealFileInExplorer(selected.Path);
                RecordingStatusText.Text = $"Opened location for {System.IO.Path.GetFileName(selected.Path)}.";
                return;
            }

            PathSafety.OpenFolderInExplorer(_outputFolder);
            RecordingStatusText.Text = "Opened session output folder.";
        }
        catch (Exception ex)
        {
            RecordingStatusText.Text = $"Open location failed: {ex.Message}";
        }

        UpdateSessionPlaybackTransportControls();
    }

    private void DeleteSelectedSessionRecording()
    {
        var selected = GetSelectedSessionRecording();
        if (selected is null || !File.Exists(selected.Path))
        {
            RecordingStatusText.Text = "Choose a saved session to delete.";
            return;
        }

        if (!PathSafety.IsDirectoryUnderFolder(selected.SessionFolder, _outputFolder)
            || !PathSafety.IsRegularFileUnderFolder(selected.Path, selected.SessionFolder, ".mp4"))
        {
            RecordingStatusText.Text = "Delete blocked: selected session is outside the output folder.";
            return;
        }

        var deleteFolder = SessionRecordingCatalog.IsPodcastSessionFolder(selected.SessionFolder);
        var targetName = deleteFolder
            ? System.IO.Path.GetFileName(selected.SessionFolder)
            : System.IO.Path.GetFileName(selected.Path);
        var result = MessageBox.Show(
            this,
            deleteFolder
                ? $"Delete this session folder?{Environment.NewLine}{targetName}"
                : $"Delete this video file?{Environment.NewLine}{targetName}",
            deleteFolder ? "Delete session" : "Delete video",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (string.Equals(_sessionPlaybackPath, selected.Path, StringComparison.OrdinalIgnoreCase))
            {
                StopSessionPlayback();
            }

            if (deleteFolder)
            {
                Directory.Delete(selected.SessionFolder, recursive: true);
            }
            else
            {
                File.Delete(selected.Path);
            }

            if (string.Equals(_lastSessionRecordingPath, selected.Path, StringComparison.OrdinalIgnoreCase))
            {
                _lastSessionRecordingPath = null;
            }

            RefreshSessionRecordings();
            RecordingStatusText.Text = deleteFolder
                ? $"Deleted session {targetName}."
                : $"Deleted {targetName}.";
        }
        catch (Exception ex)
        {
            RecordingStatusText.Text = $"Delete failed: {ex.Message}";
        }

        UpdateSessionPlaybackTransportControls();
    }

    private void StartRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (_isRecordingSession)
        {
            return;
        }

        StopSessionPlayback();

        var recordingTarget = SessionRecordingCatalog.CreateRecordingTarget(_outputFolder);
        _activeRecordingSessionFolder = recordingTarget.SessionFolder;
        _activeRecordingSetNumber = recordingTarget.SetNumber;
        _recordingStartedAt = DateTime.UtcNow;
        _recordingPausedAt = DateTime.MinValue;
        _recordingPausedDuration = TimeSpan.Zero;
        _isRecordingSession = true;
        _isRecordingPaused = false;

        if (_cameraAvailable && !_isCameraEnabled)
        {
            _isCameraEnabled = true;
            UpdateCameraEnabledState();
        }

        var videoPath = GetActiveRecordingVideoPath();
        var textureVideoStarted = TryStartTextureNativeRecording(videoPath);
        var videoStarted = textureVideoStarted
            || (_isCameraEnabled
                && videoPath is not null
                && StartActivePreviewRecording(videoPath));

        RecordingStatusText.Text = textureVideoStarted
            ? $"Recording GPU video set {SessionRecordingCatalog.FormatRecordingSetNumber(_activeRecordingSetNumber)}: {videoPath}"
            : videoStarted
                ? $"Recording video set {SessionRecordingCatalog.FormatRecordingSetNumber(_activeRecordingSetNumber)}: {videoPath}"
            : $"Recording set {SessionRecordingCatalog.FormatRecordingSetNumber(_activeRecordingSetNumber)} started: {_activeRecordingSessionFolder}";
        UpdateRecordingTransportControls();
        UpdateSessionPlaybackTransportControls();
    }

    private void PauseRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (!_isRecordingSession)
        {
            return;
        }

        if (_isRecordingPaused)
        {
            _recordingPausedDuration += DateTime.UtcNow - _recordingPausedAt;
            _recordingPausedAt = DateTime.MinValue;
            _isRecordingPaused = false;
            if (_dx12Camera?.IsTextureNative == true && _dx12Camera.IsRecording)
            {
                _dx12Camera.ResumeMP4();
            }
            else if (_textureNativeRecordingSession is not null)
            {
                _textureNativeRecordingSession.Resume();
            }
            else if (CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice))
            {
                _directShowPreviewService.ResumeRecording();
            }
            else
            {
                _cameraPreviewService.ResumeRecording();
            }

            RecordingStatusText.Text = "Recording resumed.";
        }
        else
        {
            _recordingPausedAt = DateTime.UtcNow;
            _isRecordingPaused = true;
            if (_dx12Camera?.IsTextureNative == true && _dx12Camera.IsRecording)
            {
                _dx12Camera.PauseMP4();
            }
            else if (_textureNativeRecordingSession is not null)
            {
                _textureNativeRecordingSession.Pause();
            }
            else if (CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice))
            {
                _directShowPreviewService.PauseRecording();
            }
            else
            {
                _cameraPreviewService.PauseRecording();
            }

            RecordingStatusText.Text = "Recording paused.";
        }

        UpdateRecordingTransportControls();
    }

    private void StopRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (!_isRecordingSession)
        {
            return;
        }

        var elapsed = GetRecordingElapsed();
        var sessionFolder = _activeRecordingSessionFolder;
        var setNumber = _activeRecordingSetNumber;
        _lastPreviewRecordingDiagnostics = null;
        var textureResult = StopTextureNativeRecording();
        var videoPath = textureResult?.Path ?? StopActivePreviewRecording();
        _isRecordingSession = false;
        _isRecordingPaused = false;
        _recordingPausedAt = DateTime.MinValue;
        _recordingPausedDuration = TimeSpan.Zero;
        _activeRecordingSessionFolder = null;
        _activeRecordingSetNumber = 0;
        RecordingTimerText.Text = "00:00:00";
        if (sessionFolder is not null)
        {
            WriteRecordingSessionMetadata(sessionFolder, setNumber, videoPath, elapsed, textureResult);
        }

        _lastSessionRecordingPath = videoPath;

        RecordingStatusText.Text = textureResult is { Success: true }
            ? textureResult.RecordingPipeline.Contains("processed", StringComparison.OrdinalIgnoreCase)
                ? $"Recording stopped at {FormatDuration(elapsed)}. GPU stream saved processed texture bridge video {System.IO.Path.GetFileName(textureResult.Path)} ({textureResult.SamplesWritten} frames)."
                : $"Recording stopped at {FormatDuration(elapsed)}. GPU saved raw texture-native video {System.IO.Path.GetFileName(textureResult.Path)} ({textureResult.SamplesWritten} samples)."
            : textureResult is not null
                ? $"Recording stopped at {FormatDuration(elapsed)}. GPU recording issue: {textureResult.Status}"
            : !string.IsNullOrWhiteSpace(videoPath)
            ? $"Recording stopped at {FormatDuration(elapsed)}. Saved {System.IO.Path.GetFileName(videoPath)}."
            : !string.IsNullOrWhiteSpace(_lastPreviewRecordingDiagnostics)
                ? $"Recording stopped at {FormatDuration(elapsed)}. No video file was written. {_lastPreviewRecordingDiagnostics}"
            : sessionFolder is null
                ? $"Recording stopped at {FormatDuration(elapsed)}."
                : $"Recording stopped at {FormatDuration(elapsed)}. Session folder: {sessionFolder}";

        UpdateOutputFolderText();
        RefreshSessionRecordings(videoPath);
        UpdateRecordingTransportControls();
        UpdateSessionPlaybackTransportControls();
    }

    private bool StartActivePreviewRecording(string videoPath)
    {
        var mode = CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem);
        return CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice)
            ? _directShowPreviewService.StartRecording(videoPath, mode)
            : _cameraPreviewService.StartRecording(videoPath, mode);
    }

    private string? StopActivePreviewRecording()
    {
        if (CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice))
        {
            var path = _directShowPreviewService.StopRecording();
            _lastPreviewRecordingDiagnostics = _directShowPreviewService.LastRecordingDiagnostics;
            return path;
        }

        var mediaFoundationPath = _cameraPreviewService.StopRecording();
        _lastPreviewRecordingDiagnostics = _cameraPreviewService.LastRecordingDiagnostics;
        return mediaFoundationPath;
    }

    private bool TryStartTextureNativeRecording(string? videoPath)
    {
        _lastTextureNativeRecordingResult = null;
        if (_dx12Camera?.IsTextureNative == true
            && _isCameraEnabled
            && !string.IsNullOrWhiteSpace(videoPath))
        {
            try
            {
                return _dx12Camera.WriteMP4(
                    videoPath,
                    VideoRecordingPolicy.ShouldRecordProcessedTextureOutput(_pendingVideoDenoiseEnabled),
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength);
            }
            catch (Exception ex)
            {
                RecordingStatusText.Text = $"Shared GPU recording path unavailable: {ex.Message}";
                return false;
            }
        }

        if (!VideoRecordingPolicy.ShouldUseTextureNativeRecording()
            || !_isCameraEnabled
            || string.IsNullOrWhiteSpace(videoPath)
            || CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            return false;
        }

        if (CameraSourceSelection.IsCpuCameraPreviewOwningCamera(
            _isCameraEnabled,
            _dx12Camera,
            _cameraPreviewService.IsRunning,
            _directShowPreviewService.IsRunning,
            _isDirectShowPreviewActive))
        {
            _lastPreviewRecordingDiagnostics = "Recording reused the active preview camera owner instead of opening a second GPU capture path.";
            return false;
        }

        try
        {
            _textureNativeRecordingSession = TextureNativeCameraRecorder.StartSession(
                camera.Name,
                CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem),
                videoPath,
                new TextureNativeRecordingOptions(
                    VideoRecordingPolicy.ShouldRecordProcessedTextureOutput(_pendingVideoDenoiseEnabled),
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength));
            return true;
        }
        catch (Exception ex)
        {
            _textureNativeRecordingSession?.Dispose();
            _textureNativeRecordingSession = null;
            RecordingStatusText.Text = $"GPU recording path unavailable, falling back to CPU Media Foundation: {ex.Message}";
            return false;
        }
    }

    private TextureNativeRecordingResult? StopTextureNativeRecording()
    {
        if (_dx12Camera?.IsTextureNative == true && _dx12Camera.IsRecording)
        {
            try
            {
                _lastTextureNativeRecordingResult = _dx12Camera.StopMP4();
                return _lastTextureNativeRecordingResult;
            }
            catch (Exception ex)
            {
                _lastTextureNativeRecordingResult = new TextureNativeRecordingResult(
                    false,
                    GetActiveRecordingVideoPath() ?? string.Empty,
                    0,
                    0,
                    _dx12Camera.DeviceMode,
                    _dx12Camera.MediaSubtype,
                    _dx12Camera.Width,
                    _dx12Camera.Height,
                    _dx12Camera.FramesPerSecond,
                    ex.Message);
                return _lastTextureNativeRecordingResult;
            }
        }

        var session = _textureNativeRecordingSession;
        if (session is null)
        {
            return null;
        }

        _textureNativeRecordingSession = null;
        try
        {
            _lastTextureNativeRecordingResult = session.Stop();
            return _lastTextureNativeRecordingResult;
        }
        finally
        {
            session.Dispose();
        }
    }

    private void CameraEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCameraUi)
        {
            return;
        }

        _isCameraEnabled = CameraEnabledToggle.IsChecked == true && _cameraAvailable;
        if (_isCameraEnabled)
        {
            _safeStartCameraRecoveryActive = false;
            _safeStartDx12Disabled = false;
        }

        CameraPreviewStatusText.Text = _isCameraEnabled
            ? "Camera toggle received - starting preview"
            : "Camera toggle received - stopping preview";
        UpdateCameraEnabledState();
        PersistAppState();
    }

    private void CameraSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingCameraUi)
        {
            return;
        }

        if (_isCameraEnabled)
        {
            CancelPendingCameraPreviewStart();
        }

        _ = LoadCameraModesAsync();
        ResetCameraControlPanel("Camera changed. Use Refresh after modes load if you want to query Windows camera controls.");
        UpdateCameraEnabledState();
        PersistAppState();
    }

    private void RefreshCameraControlsClicked(object sender, RoutedEventArgs e)
    {
        LoadCameraControls();
    }

    private void AdvancedCameraControlsChanged(object sender, RoutedEventArgs e)
    {
        UpdateAdvancedCameraControlsVisibility(loadWhenOpened: true);
    }

    private void VideoDenoiseChanged(object sender, RoutedEventArgs e)
    {
        UpdateVideoDenoiseSettings(restartPreview: false);
        PersistAppState();
    }

    private void VideoDenoiseChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateVideoDenoiseSettings(restartPreview: false);
        PersistAppState();
    }

    private void VideoColorPolishChanged(object sender, RoutedEventArgs e)
    {
        UpdateVideoColorPolishSettings();
    }

    private void VideoColorPolishChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateVideoColorPolishSettings();
    }

    private void CameraModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingCameraModes)
        {
            return;
        }

        if (_isCameraEnabled)
        {
            StartCameraPreview();
            PersistAppState();
            return;
        }

        UpdateCameraIdleStatus();
        PersistAppState();
    }

    private async void MainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl) || _isClosing)
        {
            return;
        }

        RefreshActiveSpectrumWaterfallHosts();
        if (IsMicDspTabSelected())
        {
            await EnsureEqualizerEditorChannelSelectedAsync();
        }
        if (IsMicDspTabSelected() && _showWaveform3D)
        {
            EnsureInlineWaveform3DView();
            if (_latestFrame is not null)
            {
                _waveform3DGraphHost?.AcceptFrame(CreateSelectedMicFrame(_latestFrame));
            }
        }
        else if (IsMicDspTabSelected())
        {
            EnsureSelectedMicSpectrumGraphView();
            InlineWaveform3DHost.Content = _selectedMicSpectrumGraphHost;
            if (_latestFrame is not null)
            {
                _selectedMicSpectrumGraphHost?.AcceptFrame(CreateSelectedMicFrame(_latestFrame));
            }
        }
        if (IsMixingTabSelected())
        {
            EnsureMixingMicSpectrumGraphView();
            EnsureMixingOutputWaveform3DView();
            if (_latestFrame is not null)
            {
                var programOutputFrame = CreateMixerOutputFrame(_latestFrame);
                _mixingMicSpectrumGraphHost?.AcceptFrame(programOutputFrame);
                _mixingOutputWaveform3DGraphHost?.AcceptFrame(CreateProgramOutputFrame(_latestFrame));
            }
        }

        if (!_isCameraEnabled)
        {
            return;
        }

        if (IsPodcastTabSelected()
            || IsKaraokeTabSelected() && _karaokeRecordVideoEnabled)
        {
            ReinitializeCameraForFocusedPreview();
        }
    }

    private async Task EnsureEqualizerEditorChannelSelectedAsync()
    {
        _isUpdatingMicChannelUi = true;
        try
        {
            RefreshEqualizerMicChannelList();
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }

        if (IsEqualizerEditableMicChannel(_activeMicChannel))
        {
            ApplyActiveMicChannelToUi();
            return;
        }

        var fallback = GetDefaultEqualizerMicChannel();
        if (fallback is not null)
        {
            await SetActiveMicChannelAsync(fallback, restartAudio: true);
        }
    }

    private void ReinitializeCameraForFocusedPreview()
    {
        if (!_isCameraEnabled || _isLoadingCameraModes)
        {
            return;
        }

        StopCameraPreview("Restarting camera preview for the active tab.");
        Dx12Camera.CollectReleasedCamera();
        StartCameraPreview();
    }

    private bool IsPodcastTabSelected()
    {
        return IsMainTabSelected("Podcast");
    }

    private bool IsKaraokeTabSelected()
    {
        return IsMainTabSelected("Karaoke");
    }

    private bool IsMicDspTabSelected()
    {
        return IsMainTabSelected("Mic / DSP");
    }

    private bool IsMixingTabSelected()
    {
        return IsMainTabSelected("Mixing");
    }

    private bool IsMainTabSelected(string header)
    {
        return MainTabControl?.SelectedItem is TabItem { Header: string selectedHeader }
            && selectedHeader.Equals(header, StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureMediaFoundationPreview(bool denoiseEnabled, bool denoiseHandledByPreviewRenderer = false)
    {
        _cameraPreviewService.DenoiseEnabled = denoiseEnabled;
        _cameraPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;
        _cameraPreviewService.DenoiseHandledByPreviewRenderer = denoiseHandledByPreviewRenderer || _dx12Camera?.IsReady == true;
        _cameraPreviewService.ColorSettings = _pendingVideoColorSettings;
    }

    private void ConfigureDirectShowPreview(bool denoiseEnabled, bool denoiseHandledByPreviewRenderer = false)
    {
        _directShowPreviewService.DenoiseEnabled = denoiseEnabled;
        _directShowPreviewService.DenoiseHandledByPreviewRenderer = denoiseHandledByPreviewRenderer || _dx12Camera?.IsReady == true;
        _directShowPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;
        _directShowPreviewService.ColorSettings = _pendingVideoColorSettings;
    }

    private void ConfigureCpuPreviewServices()
    {
        ConfigureMediaFoundationPreview(_pendingVideoDenoiseEnabled);
        ConfigureDirectShowPreview(_pendingVideoDenoiseEnabled);
    }

    private async Task<bool> TryStartMediaFoundationPreviewAsync(
        CameraDevice camera,
        CameraVideoMode mode,
        bool denoiseEnabled)
    {
        ConfigureMediaFoundationPreview(
            denoiseEnabled,
            denoiseHandledByPreviewRenderer: denoiseEnabled);
        return await Task.Run(() => _cameraPreviewService.Start(camera, mode));
    }

    private async Task<bool> TryStartDirectShowPreviewServiceAsync(CameraDevice directShowCamera, CameraVideoMode mode)
    {
        ConfigureDirectShowPreview(
            _pendingVideoDenoiseEnabled,
            denoiseHandledByPreviewRenderer: _pendingVideoDenoiseEnabled);
        return await Task.Run(() =>
        {
            _cameraPreviewService.Stop();
            return _directShowPreviewService.Start(directShowCamera, mode);
        });
    }

    private static async Task StopPreviewServiceAsync(Action stop)
    {
        await Task.Run(() => TryStopPreviewService(stop));
    }

    private void StopPreviewServices()
    {
        TryStopPreviewService(_cameraPreviewService.Stop);
        TryStopPreviewService(_directShowPreviewService.Stop);
    }

    private static void TryStopPreviewService(Action stop)
    {
        try
        {
            stop();
        }
        catch
        {
        }
    }

    private void UpdateCameraEnabledState()
    {
        if (!RefreshCameraEnabledUi())
        {
            CancelPendingCameraPreviewStart();
            return;
        }

        if (!_isCameraEnabled)
        {
            CancelPendingCameraPreviewStart();
            if (!_isCameraServiceStopPending)
            {
                StopCameraPreview(CameraStatusText.FormatCameraDisabledStatus(CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem)));
            }

            UpdateKaraokeVideoPreviewState();
            return;
        }

        if (_isLoadingCameraModes)
        {
            CameraPreviewStatusText.Text = CameraComboBox.SelectedItem is CameraDevice loadingCamera
                ? $"Loading modes before preview: {loadingCamera.Name}"
                : "Loading camera modes before preview";
            UpdateKaraokeVideoPreviewState();
            return;
        }

        if (_isCameraServiceStopPending || _isCameraPreviewStartPending)
        {
            UpdateKaraokeVideoPreviewState();
            return;
        }

        StartCameraPreview();
        UpdateKaraokeVideoPreviewState();
    }

    private bool RefreshCameraEnabledUi()
    {
        if (CameraEnabledToggle is null || CameraPreviewStatusText is null || CameraComboBox is null || CameraModeComboBox is null)
        {
            return false;
        }

        var cameraBusy = _isCameraServiceStopPending || _isCameraPreviewStartPending;
        _isUpdatingCameraUi = true;
        try
        {
            CameraEnabledToggle.IsEnabled = _cameraAvailable && !cameraBusy;
            CameraComboBox.IsEnabled = _cameraAvailable && !cameraBusy;
            CameraModeComboBox.IsEnabled = _cameraAvailable && !cameraBusy && CameraModeComboBox.Items.Count > 0;

            if (!_cameraAvailable)
            {
                _isCameraEnabled = false;
                CameraEnabledToggle.IsChecked = false;
                CameraEnabledToggle.Content = "No Camera";
                CameraPreviewStatusText.Text = "No camera source found";
                CameraModeComboBox.ItemsSource = new[] { CameraVideoMode.Auto };
                CameraModeComboBox.SelectedIndex = 0;
                ClearPreviewSurface(CameraPreviewImage, CameraPlaceholder);
                UpdateKaraokeVideoPreviewState();
                return false;
            }

            CameraEnabledToggle.IsChecked = _isCameraEnabled;
            CameraEnabledToggle.Content = _isCameraServiceStopPending
                ? "Stopping..."
                : _isCameraPreviewStartPending
                    ? "Starting..."
                    : _isCameraEnabled
                        ? "Camera On"
                        : "Camera Off";
        }
        finally
        {
            _isUpdatingCameraUi = false;
        }

        return true;
    }

    private void CancelPendingCameraPreviewStart()
    {
        System.Threading.Interlocked.Increment(ref _cameraPreviewStartOperationVersion);
    }

    private void StartCameraPreview()
    {
        var operationVersion = System.Threading.Interlocked.Increment(ref _cameraPreviewStartOperationVersion);
        if (_isCameraPreviewStartPending)
        {
            if (CameraPreviewStatusText is not null)
            {
                CameraPreviewStatusText.Text = "Camera preview restart queued.";
            }

            RefreshCameraEnabledUi();
            return;
        }

        if (_isCameraServiceStopPending)
        {
            if (CameraPreviewStatusText is not null)
            {
                CameraPreviewStatusText.Text = "Waiting for camera preview to stop before restarting.";
            }

            RefreshCameraEnabledUi();
            return;
        }

        _ = StartCameraPreviewAsync(operationVersion);
    }

    private async Task StartCameraPreviewAsync(int operationVersion)
    {
        _isCameraPreviewStartPending = true;
        RefreshCameraEnabledUi();

        try
        {
            await StartCameraPreviewCoreAsync(operationVersion);
        }
        catch (Exception ex)
        {
            AppStateStore.LogDiagnostic("camera-preview-start-failed", ex);
            if (!_isClosing && operationVersion == System.Threading.Volatile.Read(ref _cameraPreviewStartOperationVersion))
            {
                _isCameraEnabled = false;
                StopCameraPreview($"Could not start camera preview: {ex.Message}");
            }
        }
        finally
        {
            _isCameraPreviewStartPending = false;
            RefreshCameraEnabledUi();

            if (!_isClosing
                && _isCameraEnabled
                && !_isLoadingCameraModes
                && !_isCameraServiceStopPending
                && operationVersion != System.Threading.Volatile.Read(ref _cameraPreviewStartOperationVersion))
            {
                StartCameraPreview();
            }

            UpdateKaraokeVideoPreviewState();
        }
    }

    private async Task StartCameraPreviewCoreAsync(int operationVersion)
    {
        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            StopCameraPreview("Choose a camera source");
            return;
        }

        StopSessionPlayback();

        ShowPreviewSurface(CameraPreviewImage, CameraPlaceholder);

        var mode = CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
        if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
        {
            return;
        }

        if (TextureNativePreviewPolicy.ShouldUseSharedTextureCameraStream(_safeStartDx12Disabled))
        {
            if (TextureNativePreviewPolicy.TryGetPreviewFailure(camera, mode, out var cachedTextureFailure))
            {
                _lastTextureNativeCameraError = cachedTextureFailure;
                CameraPreviewStatusText.Text = $"Shared GPU stream skipped for {camera.Name}: {cachedTextureFailure}";
            }
            else if (StartTextureNativeCameraStream(camera, mode))
            {
                TextureNativePreviewPolicy.ForgetPreviewFailure(camera, mode);
                return;
            }
            else
            {
                TextureNativePreviewPolicy.RememberPreviewFailure(
                    camera,
                    mode,
                    _lastTextureNativeCameraError ?? "shared texture preview attempt failed");
            }

            CameraPreviewStatusText.Text = $"Shared GPU stream unavailable; trying CPU preview for {camera.Name}: {_lastTextureNativeCameraError ?? "unknown error"}";
        }

        StopTextureNativeCameraStream();
        await StopPreviewServiceAsync(_directShowPreviewService.Stop);
        if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
        {
            return;
        }

        ReleaseStaleCameraPreviewOwnershipIfNeeded("CPU preview startup");
        _isDirectShowPreviewActive = false;
        CameraPreviewImage.Visibility = Visibility.Visible;

        if (CameraSourceSelection.IsDirectShowCamera(camera))
        {
            if (await TryStartDirectShowPreviewAsync(camera, camera, mode, isFallback: false, operationVersion))
            {
                return;
            }

            if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
            {
                return;
            }

            _isCameraEnabled = false;
            StopCameraPreview(_directShowPreviewService.LastStatus ?? $"Could not open DirectShow preview for {camera.Name}");
            UpdateCameraEnabledState();
            return;
        }

        if (!_cameraPreviewService.IsAvailable)
        {
            if (await TryStartDirectShowFallbackPreviewAsync(camera, mode, operationVersion))
            {
                return;
            }

            if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
            {
                return;
            }

            _isCameraEnabled = false;
            StopCameraPreview("Windows Media Foundation camera preview is unavailable");
            UpdateCameraEnabledState();
            return;
        }

        if (await TryStartMediaFoundationPreviewAsync(camera, mode, _pendingVideoDenoiseEnabled))
        {
            if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
            {
                await StopPreviewServiceAsync(_cameraPreviewService.Stop);
                return;
            }

            _isDirectShowPreviewActive = false;
            if (ClaimFallbackCameraOwner(
                camera,
                mode,
                "Media Foundation CPU preview",
                () => _cameraPreviewService.Stop()))
            {
                CameraPreviewStatusText.Text = CameraStatusText.FormatCameraStatus("Starting", camera, mode);
                return;
            }

            await StopPreviewServiceAsync(_cameraPreviewService.Stop);
        }

        if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
        {
            return;
        }

        if (_pendingVideoDenoiseEnabled)
        {
            if (await TryStartMediaFoundationPreviewAsync(camera, mode, denoiseEnabled: false))
            {
                if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
                {
                    await StopPreviewServiceAsync(_cameraPreviewService.Stop);
                    return;
                }

                _isDirectShowPreviewActive = false;
                if (ClaimFallbackCameraOwner(
                    camera,
                    mode,
                    "Media Foundation CPU preview with denoise bypassed",
                    () => _cameraPreviewService.Stop()))
                {
                    CameraPreviewStatusText.Text = $"{CameraStatusText.FormatCameraStatus("Starting", camera, mode)} - denoise bypassed because the camera rejected it";
                    return;
                }

                await StopPreviewServiceAsync(_cameraPreviewService.Stop);
            }
        }

        if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
        {
            return;
        }

        if (await TryStartDirectShowFallbackPreviewAsync(camera, mode, operationVersion))
        {
            return;
        }

        if (!IsCameraPreviewStartCurrent(operationVersion, camera, mode))
        {
            return;
        }

        _isCameraEnabled = false;
        StopCameraPreview($"Could not open preview for {camera.Name}");
        UpdateCameraEnabledState();
    }

    private async Task<bool> TryStartDirectShowFallbackPreviewAsync(CameraDevice primaryCamera, CameraVideoMode mode, int operationVersion)
    {
        return CameraSourceSelection.TryGetDirectShowFallbackCamera(primaryCamera, out var fallback)
            && fallback is not null
            && await TryStartDirectShowPreviewAsync(primaryCamera, fallback, mode, isFallback: true, operationVersion);
    }

    private async Task<bool> TryStartDirectShowPreviewAsync(
        CameraDevice displayCamera,
        CameraDevice directShowCamera,
        CameraVideoMode mode,
        bool isFallback,
        int operationVersion)
    {
        CameraPreviewStatusText.Text = isFallback
            ? $"Starting DirectShow fallback for {displayCamera.Name}"
            : $"Starting DirectShow preview for {directShowCamera.Name}";
        if (await TryStartDirectShowPreviewServiceAsync(directShowCamera, mode))
        {
            if (!IsCameraPreviewStartCurrent(operationVersion, displayCamera, mode))
            {
                await StopPreviewServiceAsync(_directShowPreviewService.Stop);
                return false;
            }

            _isDirectShowPreviewActive = true;
            if (ClaimFallbackCameraOwner(
                displayCamera,
                mode,
                isFallback ? "DirectShow CPU fallback preview" : "DirectShow CPU preview",
                () => _directShowPreviewService.Stop()))
            {
                CameraPreviewStatusText.Text = isFallback
                    ? $"{CameraStatusText.FormatCameraStatus("Starting", displayCamera, mode)} - DirectShow fallback"
                    : CameraStatusText.FormatCameraStatus("Starting", displayCamera, mode);
                return true;
            }

            _isDirectShowPreviewActive = false;
            await StopPreviewServiceAsync(_directShowPreviewService.Stop);
        }

        return false;
    }

    private bool IsCameraPreviewStartCurrent(int operationVersion, CameraDevice camera, CameraVideoMode mode)
    {
        return !_isClosing
            && _isCameraEnabled
            && operationVersion == System.Threading.Volatile.Read(ref _cameraPreviewStartOperationVersion)
            && CameraDevicesMatch(CameraComboBox.SelectedItem as CameraDevice, camera)
            && CameraModesMatch(CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem), mode);
    }

    private static bool CameraDevicesMatch(CameraDevice? selectedCamera, CameraDevice expectedCamera)
    {
        return selectedCamera is not null
            && selectedCamera.DeviceNumber == expectedCamera.DeviceNumber
            && string.Equals(selectedCamera.Name, expectedCamera.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(selectedCamera.DevicePath, expectedCamera.DevicePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(selectedCamera.Source, expectedCamera.Source, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CameraModesMatch(CameraVideoMode selectedMode, CameraVideoMode expectedMode)
    {
        return selectedMode.IsAuto == expectedMode.IsAuto
            && selectedMode.Width == expectedMode.Width
            && selectedMode.Height == expectedMode.Height
            && NullableDoublesMatch(selectedMode.FramesPerSecond, expectedMode.FramesPerSecond)
            && string.Equals(selectedMode.InputFormat, expectedMode.InputFormat, StringComparison.OrdinalIgnoreCase)
            && string.Equals(selectedMode.Label, expectedMode.Label, StringComparison.Ordinal);
    }

    private static bool NullableDoublesMatch(double? first, double? second)
    {
        return first == second || first.HasValue && second.HasValue && Math.Abs(first.Value - second.Value) < 0.001d;
    }

    private bool StartTextureNativeCameraStream(CameraDevice camera, CameraVideoMode mode)
    {
        _lastTextureNativeCameraError = null;
        StopPreviewServices();
        _isDirectShowPreviewActive = false;
        _cpuPreviewFramePump.Reset();
        _cameraPreviewBitmap = null;
        ClearPreviewSurface(
            CameraPreviewImage,
            CameraPlaceholder,
            CameraPreviewStatusText,
            $"{CameraStatusText.FormatCameraStatus("GPU stream starting", camera, mode)} - waiting for texture frames");

        try
        {
            if (!Dx12Camera.TryOpenTextureNativeIntoSlot(
                ref _dx12Camera,
                camera,
                mode,
                CreateActiveDx12CameraPreviewTarget(),
                _pendingVideoDenoiseEnabled,
                _pendingVideoDenoiseStrength,
                TextureNativeCameraFrameAvailable,
                TextureNativeCameraStatusChanged))
            {
                RecoverCameraLifecycleViolation("shared GPU preview");
                if (!Dx12Camera.TryOpenTextureNativeIntoSlot(
                    ref _dx12Camera,
                    camera,
                    mode,
                    CreateActiveDx12CameraPreviewTarget(),
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength,
                    TextureNativeCameraFrameAvailable,
                    TextureNativeCameraStatusChanged))
                {
                    _lastTextureNativeCameraError = "camera preview ownership was busy";
                    CameraPreviewStatusText.Text = "Shared GPU stream unavailable: camera preview ownership was busy.";
                    return false;
                }
            }

            _isDirectShowPreviewActive = false;
            return true;
        }
        catch (Exception ex)
        {
            _lastTextureNativeCameraError = ex.Message;
            StopTextureNativeCameraStream();
            CameraPreviewStatusText.Text = $"Shared GPU stream unavailable: {ex.Message}";
            return false;
        }
    }

    private bool ClaimFallbackCameraOwner(
        CameraDevice camera,
        CameraVideoMode mode,
        string fallbackDescription,
        Action fallbackStop)
    {
        if (Dx12Camera.TryOpenFallbackIntoSlot(
            ref _dx12Camera,
            camera,
            mode,
            CreateActiveDx12CameraPreviewTarget(),
            fallbackDescription,
            fallbackStop,
            TextureNativeCameraFrameAvailable,
            TextureNativeCameraStatusChanged))
        {
            return true;
        }

        _lastTextureNativeCameraError = "camera preview ownership was busy";
        CameraPreviewStatusText.Text = $"Could not claim camera preview ownership for {fallbackDescription}.";
        return false;
    }

    private void ReleaseStaleCameraPreviewOwnershipIfNeeded(string previewPath)
    {
        if (_dx12Camera is not null)
        {
            RecoverCameraLifecycleViolation(previewPath);
        }
    }

    private void RecoverCameraLifecycleViolation(string previewPath)
    {
        AppStateStore.LogDiagnostic(
            "camera-lifecycle-recovery",
            $"Camera preview ownership was busy while starting {previewPath}. Releasing stale camera ownership.");
        _lastTextureNativeCameraError = "stale camera preview ownership was reset";
        StopTextureNativeCameraStream();
        _isDirectShowPreviewActive = false;
        _cpuPreviewFramePump.Reset();
        _textureNativeStatusPump.Reset();
        if (CameraPreviewStatusText is not null)
        {
            CameraPreviewStatusText.Text = $"Recovered stale camera preview ownership while starting {previewPath}.";
        }
    }

    private Dx12Camera.PreviewTarget CreateActiveDx12CameraPreviewTarget()
    {
        if (IsKaraokeTabSelected() && _karaokeRecordVideoEnabled && KaraokeVideoPreviewSurfaceGrid is not null)
        {
            _cameraPreviewBitmap = null;
            ClearPreviewSurface(
                CameraPreviewImage,
                CameraPlaceholder,
                CameraPreviewStatusText,
                "Camera preview is owned by Karaoke.");

            return new Dx12Camera.PreviewTarget(
                KaraokeVideoPreviewSurfaceGrid,
                KaraokeVideoPreviewImage,
                KaraokeVideoPreviewPlaceholder,
                KaraokeVideoPreviewStatusText,
                0,
                "Karaoke");
        }

        ClearKaraokeVideoPreviewBitmap();
        return new Dx12Camera.PreviewTarget(
            CameraPreviewSurfaceGrid,
            CameraPreviewImage,
            CameraPlaceholder,
            CameraPreviewStatusText,
            1,
            "Podcast");
    }

    private void StopCameraPreview(string status)
    {
        StopTextureNativeCameraStream();
        _isDirectShowPreviewActive = false;
        _cpuPreviewFramePump.Reset();
        _cameraPreviewBitmap = null;
        ClearPreviewSurface(CameraPreviewImage, CameraPlaceholder, CameraPreviewStatusText, status);
        ClearKaraokeVideoPreviewBitmap();
        StopCameraPreviewServicesInBackground(status);
    }

    private void StopCameraPreviewServicesInBackground(string idleStatus)
    {
        if (_isCameraServiceStopPending)
        {
            CameraPreviewStatusText.Text = idleStatus;
            return;
        }

        var operationVersion = System.Threading.Interlocked.Increment(ref _cameraServiceStopOperationVersion);
        _isCameraServiceStopPending = true;
        UpdateCameraEnabledState();

        _ = Task.Run(() =>
        {
            StopPreviewServices();

            Dispatcher.BeginInvoke(() =>
            {
                if (operationVersion != System.Threading.Volatile.Read(ref _cameraServiceStopOperationVersion))
                {
                    return;
                }

                _isCameraServiceStopPending = false;
                RefreshCameraEnabledUi();
                if (_isCameraEnabled)
                {
                    UpdateCameraEnabledState();
                    return;
                }

                if (CameraPreviewStatusText is not null)
                {
                    CameraPreviewStatusText.Text = idleStatus;
                }
            }, DispatcherPriority.Background);
        });
    }

    private void StopTextureNativeCameraStream()
    {
        var camera = _dx12Camera;
        if (camera is null)
        {
            return;
        }

        var recordingResult = Dx12Camera.CloseSlot(
            ref _dx12Camera,
            TextureNativeCameraFrameAvailable,
            TextureNativeCameraStatusChanged,
            collectGarbage: false);
        if (recordingResult is not null)
        {
            _lastTextureNativeRecordingResult = recordingResult;
        }

        _textureNativeStatusPump.Reset();
    }

    private void CameraPreviewFrameAvailable(object? sender, CameraFrame frame)
    {
        _cpuPreviewFramePump.FrameAvailable(frame);
    }

    private void ProcessPendingCameraPreviewFrame(CameraFrame latestFrame)
    {
        if (!_isCameraEnabled)
        {
            return;
        }

        if (_dx12Camera?.IsFallback == true)
        {
            _dx12Camera.RenderFallbackFrame(
                latestFrame,
                _pendingVideoColorSettings,
                _pendingVideoDenoiseEnabled,
                _pendingVideoDenoiseStrength);
            return;
        }

        if (!IsPodcastTabSelected())
        {
            return;
        }

        ClearKaraokeVideoPreviewBitmap();
        if (latestFrame.HasBgra)
        {
            UpdateCameraPreviewBitmap(latestFrame);
            CameraPreviewImage.Visibility = Visibility.Visible;
        }
        else
        {
            CameraPreviewImage.Visibility = Visibility.Collapsed;
        }

        CameraPlaceholder.Visibility = Visibility.Collapsed;

        if (CameraComboBox.SelectedItem is CameraDevice camera)
        {
            var renderer = latestFrame.HasBgra ? "WPF BGRA" : "waiting for BGRA fallback";
            var status = $"{CameraStatusText.FormatCameraStatus("Live", camera, CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem))} - {renderer}";
            CameraPreviewStatusText.Text = status;
        }
    }

    private void UpdateCameraPreviewBitmap(CameraFrame frame)
    {
        if (_cameraPreviewBitmap is null
            || _cameraPreviewBitmap.PixelWidth != frame.Width
            || _cameraPreviewBitmap.PixelHeight != frame.Height)
        {
            _cameraPreviewBitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null);
            CameraPreviewImage.Source = _cameraPreviewBitmap;
        }

        _cameraPreviewBitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            frame.BgraBytes,
            frame.Stride,
            0);
    }

    private void KaraokeRecordVideoChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingKaraokeAdjustments)
        {
            return;
        }

        _karaokeRecordVideoEnabled = KaraokeRecordVideoCheckBox.IsChecked == true;
        UpdateKaraokeVideoPreviewState();
        if (_isCameraEnabled && (IsKaraokeTabSelected() || _dx12Camera is not null))
        {
            ReinitializeCameraForFocusedPreview();
        }

        PersistAppState();
    }

    private void UpdateKaraokeVideoPreviewState()
    {
        if (KaraokeVideoPreviewImage is null || KaraokeVideoPreviewPlaceholder is null)
        {
            return;
        }

        if (!_karaokeRecordVideoEnabled)
        {
            ClearPreviewSurface(KaraokeVideoPreviewImage, KaraokeVideoPreviewPlaceholder, KaraokeVideoPreviewStatusText, "Karaoke camera preview is off.");
            KaraokeVideoPreviewPlaceholder.Text = "Video preview off";
            return;
        }

        if (!_isCameraEnabled)
        {
            ClearPreviewSurface(KaraokeVideoPreviewImage, KaraokeVideoPreviewPlaceholder, KaraokeVideoPreviewStatusText, "Turn on the camera to start Karaoke video preview.");
            KaraokeVideoPreviewPlaceholder.Text = "Turn camera on for preview";
            return;
        }

        KaraokeVideoPreviewPlaceholder.Visibility = KaraokeVideoPreviewImage.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        KaraokeVideoPreviewPlaceholder.Text = "Starting Karaoke camera";
        if (KaraokeVideoPreviewStatusText is not null)
        {
            KaraokeVideoPreviewStatusText.Text = "Karaoke camera preview is starting.";
        }
    }

    private void ClearKaraokeVideoPreviewBitmap()
    {
        ClearPreviewSurface(KaraokeVideoPreviewImage, KaraokeVideoPreviewPlaceholder);
    }

    private static void ClearPreviewSurface(
        UIElement? previewImage,
        UIElement? placeholder,
        TextBlock? statusText = null,
        string? status = null)
    {
        if (previewImage is Image image)
        {
            image.Source = null;
        }

        if (previewImage is not null)
        {
            previewImage.Visibility = Visibility.Collapsed;
        }

        if (placeholder is not null)
        {
            placeholder.Visibility = Visibility.Visible;
        }

        if (statusText is not null && status is not null)
        {
            statusText.Text = status;
        }
    }

    private static void ShowPreviewSurface(UIElement? previewImage, UIElement? placeholder)
    {
        if (placeholder is not null)
        {
            placeholder.Visibility = Visibility.Collapsed;
        }

        if (previewImage is not null)
        {
            previewImage.Visibility = Visibility.Visible;
        }
    }

    private void TextureNativeCameraFrameAvailable(object? sender, TextureNativeFrameInfo frame)
    {
        _textureNativeStatusPump.FrameAvailable(frame);
    }

    private void ProcessPendingTextureNativeFrame(TextureNativeFrameInfo frame)
    {
        if (frame is not null && _isCameraEnabled && IsPodcastTabSelected())
        {
            CameraPreviewImage.Visibility = Visibility.Collapsed;
            CameraPlaceholder.Visibility = Visibility.Visible;

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                CameraPreviewStatusText.Text = CameraStatusText.FormatTextureNativeCameraStatus(
                    _dx12Camera,
                    "GPU stream live",
                    camera,
                    frame,
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength,
                    VideoRecordingPolicy.ShouldRecordProcessedTextureOutput(_pendingVideoDenoiseEnabled));
            }
        }
    }

    private void TextureNativeCameraStatusChanged(object? sender, string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isCameraEnabled && _dx12Camera is not null)
            {
                if (IsPodcastTabSelected())
                {
                    CameraPreviewStatusText.Text = status;
                }

                if (IsKaraokeTabSelected() && _karaokeRecordVideoEnabled && KaraokeVideoPreviewStatusText is not null)
                {
                    KaraokeVideoPreviewStatusText.Text = status;
                }
            }
        });
    }

    private async Task LoadCameraModesAsync()
    {
        if (CameraModeComboBox is null)
        {
            return;
        }

        _cameraModeLoadCancellation?.Cancel();
        _cameraModeLoadCancellation?.Dispose();
        _cameraModeLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _cameraModeLoadCancellation.Token;

        _isLoadingCameraModes = true;
        CameraModeComboBox.ItemsSource = new[] { CameraVideoMode.Auto };
        CameraModeComboBox.SelectedIndex = 0;
        CameraModeComboBox.IsEnabled = false;

        if (CameraComboBox.SelectedItem is not CameraDevice camera || !_cameraModeService.IsAvailable)
        {
            _isLoadingCameraModes = false;
            UpdateCameraEnabledState();
            return;
        }

        if (CameraSourceSelection.IsDirectShowCamera(camera))
        {
            _isLoadingCameraModes = false;
            CameraModeComboBox.IsEnabled = true;
            SelectPendingCameraProfileMode();
            CameraPreviewStatusText.Text = CameraStatusText.FormatDirectShowCameraSelectedStatus(camera);
            UpdateCameraEnabledState();

            return;
        }

        try
        {
            CameraPreviewStatusText.Text = CameraStatusText.FormatLoadingCameraModesStatus(
                camera,
                CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem));
            var modes = await _cameraModeService.GetModesAsync(camera, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CameraModeComboBox.ItemsSource = modes;
            CameraModeComboBox.SelectedIndex = 0;
            SelectPendingCameraProfileMode();
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _isLoadingCameraModes = false;
                UpdateCameraEnabledState();
                if (!_isCameraEnabled)
                {
                    UpdateCameraIdleStatus();
                }
            }
        }
    }

    private void CameraPreviewStatusChanged(object? sender, string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isCameraEnabled)
            {
                CameraPreviewStatusText.Text = CameraStatusText.FormatCameraPreviewStatus(
                    status,
                    CameraComboBox.SelectedItem as CameraDevice,
                    CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem));
            }
        });
    }

    private void UpdateCameraIdleStatus()
    {
        CameraPreviewStatusText.Text = CameraStatusText.FormatCameraIdleStatus(
            _cameraAvailable,
            CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem));
    }

    private void LoadCameraControls()
    {
        if (CameraControlPanel is null || CameraControlStatusText is null)
        {
            return;
        }

        CameraControlPanel.Children.Clear();

        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            CameraControlStatusText.Text = CameraControlText.FormatChooseCameraControlsStatus();
            return;
        }

        var controls = _cameraControlService.GetControls(camera);
        if (controls.Count == 0)
        {
            CameraControlStatusText.Text = CameraControlText.FormatNoCameraControlsStatus();
            return;
        }

        CameraControlStatusText.Text = CameraControlText.FormatCameraControlsLoadedStatus(camera, controls.Count);
        RenderCameraControls(camera, controls);
    }

    private void ResetCameraControlPanel(string status)
    {
        CameraControlPanel?.Children.Clear();
        if (CameraControlStatusText is not null)
        {
            CameraControlStatusText.Text = status;
        }
    }

    private void UpdateAdvancedCameraControlsVisibility(bool loadWhenOpened)
    {
        if (AdvancedCameraControlsPanel is null || AdvancedCameraControlsCheckBox is null)
        {
            return;
        }

        var isVisible = AdvancedCameraControlsCheckBox.IsChecked == true;
        AdvancedCameraControlsPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (isVisible && loadWhenOpened && CameraControlPanel is not null && CameraControlPanel.Children.Count == 0)
        {
            LoadCameraControls();
        }
    }

    private void RenderCameraControls(CameraDevice camera, IReadOnlyList<CameraControlItem> controls)
    {
        if (CameraControlPanel is null)
        {
            return;
        }

        _isUpdatingCameraControls = true;
        try
        {
            foreach (var control in controls)
            {
                CameraControlPanel.Children.Add(CreateCameraControlEditor(camera, control));
            }
        }
        finally
        {
            _isUpdatingCameraControls = false;
        }
    }

    private FrameworkElement CreateCameraControlEditor(CameraDevice camera, CameraControlItem control)
    {
        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 79, 93, 107)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(80, 5, 7, 10)),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = control.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        layout.Children.Add(title);

        var valueText = new TextBlock
        {
            Text = CameraControlText.FormatCameraControlValue(control.Value),
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 44,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valueText, 1);
        layout.Children.Add(valueText);

        var slider = new Slider
        {
            Minimum = control.Minimum,
            Maximum = control.Maximum,
            Value = Math.Clamp(control.Value, control.Minimum, control.Maximum),
            TickFrequency = control.Step,
            TickPlacement = TickPlacement.BottomRight,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 5, 0, 4),
            SmallChange = CameraControlText.GetCameraControlNudgeStep(control),
            ToolTip = CreateWrappedToolTip($"{control.Name}. Default: {control.DefaultValue}. Range: {control.Minimum} to {control.Maximum}. Drag near the tick to snap home.")
        };
        slider.Ticks.Add(control.DefaultValue);
        Grid.SetRow(slider, 1);
        Grid.SetColumnSpan(slider, 2);
        layout.Children.Add(slider);

        var bottomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var autoCheckBox = new CheckBox
        {
            Content = "Auto",
            Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 217)),
            IsChecked = control.IsAuto,
            IsEnabled = control.SupportsAuto,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = CreateWrappedToolTip("Lets the camera choose this value automatically when the driver supports it.")
        };

        if (CameraControlText.UsesCameraControlNudgeButtons(control))
        {
            var decreaseButton = CreateCameraNudgeButton("-", $"Nudge {control.Name} down/left slowly.");
            var increaseButton = CreateCameraNudgeButton("+", $"Nudge {control.Name} up/right slowly.");
            decreaseButton.Click += (_, _) => NudgeCameraControl(camera, control, slider, valueText, autoCheckBox, -1);
            increaseButton.Click += (_, _) => NudgeCameraControl(camera, control, slider, valueText, autoCheckBox, 1);
            bottomPanel.Children.Add(decreaseButton);
            bottomPanel.Children.Add(increaseButton);
        }

        bottomPanel.Children.Add(autoCheckBox);

        var defaultButton = new Button
        {
            Content = "Default",
            Padding = new Thickness(8, 3, 8, 3),
            ToolTip = CreateWrappedToolTip($"Returns {control.Name} to its driver default of {control.DefaultValue}.")
        };
        bottomPanel.Children.Add(defaultButton);

        Grid.SetRow(bottomPanel, 2);
        Grid.SetColumnSpan(bottomPanel, 2);
        layout.Children.Add(bottomPanel);

        slider.ValueChanged += (_, e) =>
        {
            if (_isUpdatingCameraControls)
            {
                return;
            }

            var rounded = CameraControlText.ApplyCameraControlDefaultMagnet(CameraControlText.RoundCameraControlToStep(e.NewValue, control), control);
            valueText.Text = CameraControlText.FormatCameraControlValue(rounded);
            if (SetCameraControl(camera, control, rounded, isAuto: false))
            {
                control.Value = rounded;
                control.IsAuto = false;
                _isUpdatingCameraControls = true;
                autoCheckBox.IsChecked = false;
                _isUpdatingCameraControls = false;
                if (Math.Abs(slider.Value - rounded) > 0.001d && rounded == control.DefaultValue)
                {
                    _isUpdatingCameraControls = true;
                    slider.Value = rounded;
                    _isUpdatingCameraControls = false;
                }
            }
        };

        autoCheckBox.Checked += (_, _) =>
        {
            if (!_isUpdatingCameraControls && !SetCameraControl(camera, control, control.Value, isAuto: true))
            {
                RestoreCameraAutoCheckBox(autoCheckBox, control);
            }
        };

        autoCheckBox.Unchecked += (_, _) =>
        {
            if (!_isUpdatingCameraControls && !SetCameraControl(camera, control, control.Value, isAuto: false))
            {
                RestoreCameraAutoCheckBox(autoCheckBox, control);
            }
        };

        defaultButton.Click += (_, _) =>
        {
            if (SetCameraControl(camera, control, control.DefaultValue, isAuto: false))
            {
                _isUpdatingCameraControls = true;
                control.Value = control.DefaultValue;
                control.IsAuto = false;
                slider.Value = control.DefaultValue;
                autoCheckBox.IsChecked = false;
                valueText.Text = CameraControlText.FormatCameraControlValue(control.DefaultValue);
                _isUpdatingCameraControls = false;
            }
        };

        container.Child = layout;
        return container;
    }

    private void RestoreCameraAutoCheckBox(CheckBox autoCheckBox, CameraControlItem control)
    {
        _isUpdatingCameraControls = true;
        autoCheckBox.IsChecked = control.IsAuto;
        _isUpdatingCameraControls = false;
    }

    private RepeatButton CreateCameraNudgeButton(string content, string toolTip)
    {
        return new RepeatButton
        {
            Content = content,
            Width = 34,
            Padding = new Thickness(0, 3, 0, 3),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Delay = 450,
            Interval = 180,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = CreateWrappedToolTip(toolTip)
        };
    }

    private void NudgeCameraControl(
        CameraDevice camera,
        CameraControlItem control,
        Slider slider,
        TextBlock valueText,
        CheckBox autoCheckBox,
        int direction)
    {
        var nextValue = Math.Clamp(control.Value + direction * CameraControlText.GetCameraControlNudgeStep(control), control.Minimum, control.Maximum);
        if (nextValue == control.Value)
        {
            return;
        }

        if (!SetCameraControl(camera, control, nextValue, isAuto: false))
        {
            return;
        }

        _isUpdatingCameraControls = true;
        slider.Value = nextValue;
        autoCheckBox.IsChecked = false;
        valueText.Text = CameraControlText.FormatCameraControlValue(nextValue);
        _isUpdatingCameraControls = false;
    }

    private bool SetCameraControl(CameraDevice camera, CameraControlItem control, int value, bool isAuto)
    {
        var success = _cameraControlService.SetControl(camera, control, value, isAuto);
        if (!success)
        {
            CameraControlStatusText.Text = CameraControlText.FormatCameraControlSetStatus(control, value, isAuto, success: false);
            return false;
        }

        control.Value = value;
        control.IsAuto = isAuto;
        CameraControlStatusText.Text = CameraControlText.FormatCameraControlSetStatus(control, value, isAuto, success: true);
        return true;
    }

    private void UpdateVideoDenoiseSettings(bool restartPreview)
    {
        if (VideoDenoiseCheckBox is null || VideoDenoiseSlider is null)
        {
            return;
        }

        var strength = Math.Clamp(VideoDenoiseSlider.Value, VideoDenoiseSlider.Minimum, DenoiseMaximumStrength);
        if (!_isSnappingVideoDenoiseSlider
            && Math.Abs(strength - DenoiseDefaultStrength) <= 0.25d
            && Math.Abs(strength - DenoiseDefaultStrength) > 0.001d)
        {
            _isSnappingVideoDenoiseSlider = true;
            VideoDenoiseSlider.Value = DenoiseDefaultStrength;
            _isSnappingVideoDenoiseSlider = false;
            strength = DenoiseDefaultStrength;
        }

        VideoDenoiseSlider.Ticks.Clear();
        VideoDenoiseSlider.Ticks.Add(DenoiseDefaultStrength);
        VideoDenoiseSlider.Ticks.Add(DenoiseMaximumStrength);
        _videoDenoiseSliderStrength = strength;
        _pendingVideoDenoiseEnabled = VideoDenoiseCheckBox.IsChecked == true;
        if (VideoDenoiseControlsPanel is not null)
        {
            VideoDenoiseControlsPanel.Visibility = _pendingVideoDenoiseEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        _pendingVideoDenoiseStrength = strength;
        _dx12Camera?.Denoise(_pendingVideoDenoiseEnabled, _pendingVideoDenoiseStrength);
        ConfigureCpuPreviewServices();

        UpdateVideoDenoiseValueText(strength);

        if (restartPreview && _isCameraEnabled)
        {
            StartCameraPreview();
            return;
        }

        if (_isCameraEnabled)
        {
            CameraControlStatusText.Text = CameraStatusText.FormatVideoDenoiseStatus(
                _dx12Camera?.IsTextureNative == true,
                _pendingVideoDenoiseEnabled);
        }
    }

    private void UpdateVideoColorPolishSettings()
    {
        if (VideoColorPolishCheckBox is null
            || VideoExposureSlider is null
            || VideoContrastSlider is null
            || VideoSaturationSlider is null
            || VideoWarmthSlider is null)
        {
            return;
        }

        var enabled = VideoColorPolishCheckBox.IsChecked == true;
        if (VideoColorPolishControlsPanel is not null)
        {
            VideoColorPolishControlsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        _pendingVideoColorSettings = new VideoFrameColorSettings(
            enabled,
            Math.Clamp(VideoExposureSlider.Value, VideoExposureSlider.Minimum, VideoExposureSlider.Maximum),
            Math.Clamp(VideoContrastSlider.Value, VideoContrastSlider.Minimum, VideoContrastSlider.Maximum),
            Math.Clamp(VideoSaturationSlider.Value, VideoSaturationSlider.Minimum, VideoSaturationSlider.Maximum),
            Math.Clamp(VideoWarmthSlider.Value, VideoWarmthSlider.Minimum, VideoWarmthSlider.Maximum));
        ApplyVideoColorSettingsToCpuServices();
        UpdateVideoColorValueText();

        if (!_isCameraEnabled || CameraControlStatusText is null)
        {
            return;
        }

        CameraControlStatusText.Text = CameraStatusText.FormatVideoColorPolishStatus(
            _dx12Camera?.IsTextureNative == true,
            _pendingVideoColorSettings);
    }

    private void UpdateVideoDenoiseValueText(double strength)
    {
        if (VideoDenoiseValueText is not null)
        {
            VideoDenoiseValueText.Text = $"{strength:0.0}";
        }
    }

    private void ApplyVideoColorSettingsToCpuServices()
    {
        ConfigureCpuPreviewServices();
    }

    private void UpdateVideoColorValueText()
    {
        if (VideoExposureValueText is not null)
        {
            VideoExposureValueText.Text = _pendingVideoColorSettings.Exposure.ToString("+0;-0;0");
        }

        if (VideoContrastValueText is not null)
        {
            VideoContrastValueText.Text = _pendingVideoColorSettings.Contrast.ToString("+0;-0;0");
        }

        if (VideoSaturationValueText is not null)
        {
            VideoSaturationValueText.Text = _pendingVideoColorSettings.Saturation.ToString("+0;-0;0");
        }

        if (VideoWarmthValueText is not null)
        {
            VideoWarmthValueText.Text = _pendingVideoColorSettings.Warmth.ToString("+0;-0;0");
        }
    }

    private void UpdateOutputFolderText()
    {
        if (OutputFolderText is not null)
        {
            OutputFolderText.Text = _outputFolder;
        }

        if (NextSessionFolderText is not null)
        {
            var previewFolder = SessionRecordingCatalog.ResolvePodcastSessionFolderForPreview(_outputFolder);
            NextSessionFolderText.Text = previewFolder;
            if (NextRecordingFilesText is not null)
            {
                var setNumber = Directory.Exists(previewFolder)
                    ? SessionRecordingCatalog.GetNextRecordingSetNumber(previewFolder)
                    : 1;
                NextRecordingFilesText.Text = SessionRecordingCatalog.FormatRecordingFileSet(setNumber);
            }
        }

        ConfigureSessionFolderWatcher();
        RefreshSessionRecordings(_lastSessionRecordingPath);
    }

    private string? GetActiveRecordingVideoPath()
    {
        if (string.IsNullOrWhiteSpace(_activeRecordingSessionFolder) || _activeRecordingSetNumber <= 0)
        {
            return null;
        }

        return System.IO.Path.Combine(
            _activeRecordingSessionFolder,
            $"video_{SessionRecordingCatalog.FormatRecordingSetNumber(_activeRecordingSetNumber)}.mp4");
    }

    private void WriteRecordingSessionMetadata(
        string sessionFolder,
        int setNumber,
        string? videoPath,
        TimeSpan elapsed,
        TextureNativeRecordingResult? textureResult)
    {
        try
        {
            var metadataPath = System.IO.Path.Combine(sessionFolder, "session.json");
            var metadata = new
            {
                createdAt = DateTimeOffset.Now,
                duration = FormatDuration(elapsed),
                setNumber,
                camera = CameraComboBox.SelectedItem is CameraDevice camera ? camera.Name : null,
                mode = CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem).Label,
                denoiseEnabled = _pendingVideoDenoiseEnabled,
                denoiseSliderStrength = _videoDenoiseSliderStrength,
                denoiseStrength = _pendingVideoDenoiseStrength,
                video = string.IsNullOrWhiteSpace(videoPath) ? null : System.IO.Path.GetFileName(videoPath),
                engine = textureResult is not null
                    ? _dx12Camera?.IsTextureNative == true
                        ? "Windows Media Foundation shared texture-native GPU stream"
                        : "Windows Media Foundation texture-native GPU samples"
                    : CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice)
                        ? "DirectShow CPU frames with Media Foundation MP4 writer"
                        : "Windows Media Foundation CPU frames",
                videoProcessing = CameraStatusText.BuildVideoProcessingMetadata(
                    textureResult,
                    _dx12Camera,
                    _pendingVideoDenoiseEnabled,
                    _videoDenoiseSliderStrength,
                    _pendingVideoDenoiseStrength,
                    _pendingVideoColorSettings,
                    CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice),
                    _dx12Camera?.IsReady == true),
                textureNative = textureResult is null
                    ? null
                    : new
                    {
                        textureResult.Success,
                        textureResult.DeviceMode,
                        textureResult.MediaSubtype,
                        textureResult.Width,
                        textureResult.Height,
                        textureResult.FramesPerSecond,
                        textureResult.SamplesWritten,
                        textureResult.BytesWritten,
                        previewPath = _dx12Camera?.PreviewPathDescription,
                        textureResult.Status
                    }
            };
            AtomicFile.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, UserPresetJsonOptions));
        }
        catch (Exception ex)
        {
            RecordingStatusText.Text = $"Recording stopped, but session metadata could not be written: {ex.Message}";
        }
    }

    private string CreateAudioRecordingFilePath(DateTime timestamp)
    {
        return AudioRecordingCatalog.CreateRecordingFilePath(
            _audioRecordingFolder,
            timestamp,
            _audioRecordingSource,
            _activeMicChannel?.ChannelNumber ?? 1);
    }

    private void SpectrumViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform3D = false;
        _spectrumService.StereoInputAnalysisEnabled = true;
        EnsureSelectedMicSpectrumGraphView();
        InlineWaveform3DHost.Content = _selectedMicSpectrumGraphHost;
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Visible;

        if (_latestFrame is not null)
        {
            _selectedMicSpectrumGraphHost?.AcceptFrame(CreateSelectedMicFrame(_latestFrame));
        }
    }

    private void Waveform3DClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform3D = true;
        _spectrumService.StereoInputAnalysisEnabled = false;
        EnsureInlineWaveform3DView();
        ApplyWaterfallLineDensity();
        InlineWaveform3DHost.Content = _waveform3DGraphHost;
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Collapsed;

        if (_latestFrame is not null)
        {
            _waveform3DGraphHost?.AcceptFrame(CreateSelectedMicFrame(_latestFrame));
        }
    }

    private void UpdateGraphSurfaceVisibility()
    {
        var showDx12Spectrum = !_showWaveform3D;
        SpectrumCanvas.Visibility = Visibility.Collapsed;
        InlineWaveform3DHost.Visibility = _showWaveform3D || showDx12Spectrum
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (showDx12Spectrum)
        {
            EnsureSelectedMicSpectrumGraphView();
            InlineWaveform3DHost.Content = _selectedMicSpectrumGraphHost;
        }
        else if (_showWaveform3D)
        {
            EnsureInlineWaveform3DView();
            InlineWaveform3DHost.Content = _waveform3DGraphHost;
        }

        UpdateGraphViewButtonStates();
        UpdateEqualizerHoverRegion();
        UpdateDx12EqualizerHoverRegion();
        WaterfallLinesPanel.Visibility = _showWaveform3D ? Visibility.Visible : Visibility.Collapsed;
        RefreshActiveSpectrumWaterfallHosts();

    }

    private void UpdateGraphViewButtonStates()
    {
        SpectrumViewButton.IsChecked = !_showWaveform3D;
        Waveform3DButton.IsChecked = _showWaveform3D;
    }

    private void EnsureInlineWaveform3DView()
    {
        if (_waveform3DGraphHost is not null)
        {
            return;
        }

        var graphHost = new Direct3D12AudioGraphHost();
        graphHost.WaterfallLinesPerGap = _waterfallLinesPerGap;
        graphHost.StatusChanged += Dx12AudioGraphStatusChanged;
        InlineWaveform3DHost.Content = graphHost;
        _waveform3DGraphHost = graphHost;
        if (_showWaveform3D)
        {
            InlineWaveform3DHost.Content = graphHost;
        }
    }

    private void EnsureSelectedMicSpectrumGraphView()
    {
        if (_selectedMicSpectrumGraphHost is not null)
        {
            return;
        }

        var graphHost = new Direct3D12AudioGraphHost
        {
            GraphMode = ResolveSelectedMicSpectrumGraphMode()
        };
        graphHost.StatusChanged += Dx12AudioGraphStatusChanged;
        InlineWaveform3DHost.Content = graphHost;
        _selectedMicSpectrumGraphHost = graphHost;
        if (!_showWaveform3D)
        {
            InlineWaveform3DHost.Content = graphHost;
        }
        UpdateDx12EqualizerHoverRegion();
    }

    private void Dx12AudioGraphStatusChanged(object? sender, string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (StatusText is not null)
            {
                StatusText.Text = status;
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshActiveSpectrumWaterfallHosts()
    {
        var micDspSpectrumActive = IsMicDspTabSelected() && !_showWaveform3D;
        var micDspActive = IsMicDspTabSelected() && _showWaveform3D;
        var podcastActive = IsPodcastTabSelected();
        var karaokeActive = IsKaraokeTabSelected();
        var mixingActive = IsMixingTabSelected();

        _isMicDspSelectedMicSpectrumActive = micDspSpectrumActive;
        _isMicDspSpectrumWaterfallActive = micDspActive;
        _isPodcastSpectrumWaterfallActive = podcastActive;
        _isKaraokeSpectrumWaterfallActive = karaokeActive;
        _isMixingMicSpectrumGraphActive = mixingActive;
        _isMixingOutputWaveform3DActive = mixingActive;
        _spectrumService.StereoInputAnalysisEnabled = micDspSpectrumActive;

        if (micDspActive)
        {
            EnsureInlineWaveform3DView();
        }

        if (micDspSpectrumActive)
        {
            EnsureSelectedMicSpectrumGraphView();
        }

        if (podcastActive)
        {
            EnsurePodcastSpectrumWaterfallView();
        }

        if (karaokeActive)
        {
            EnsureKaraokeSpectrumWaterfallView();
        }

        if (mixingActive)
        {
            EnsureMixingMicSpectrumGraphView();
            EnsureMixingOutputWaveform3DView();
        }
    }

    private void EnsurePodcastSpectrumWaterfallView()
    {
        if (_podcastSpectrumWaterfallGraphHost is not null)
        {
            return;
        }

        var graphHost = new Direct3D12AudioGraphHost();
        PodcastSpectrumWaterfallHost.Content = graphHost;
        _podcastSpectrumWaterfallGraphHost = graphHost;
    }

    private void EnsureKaraokeSpectrumWaterfallView()
    {
        if (_karaokeSpectrumWaterfallGraphHost is not null)
        {
            return;
        }

        var graphHost = new Direct3D12AudioGraphHost();
        KaraokeSpectrumWaterfallHost.Content = graphHost;
        _karaokeSpectrumWaterfallGraphHost = graphHost;
    }

    private void EnsureMixingOutputWaveform3DView()
    {
        if (_mixingOutputWaveform3DGraphHost is not null)
        {
            return;
        }

        var graphHost = new Direct3D12AudioGraphHost();
        MixingOutputWaveform3DHost.Content = graphHost;
        _mixingOutputWaveform3DGraphHost = graphHost;
    }

    private void EnsureMixingMicSpectrumGraphView()
    {
        if (_mixingMicSpectrumGraphHost is not null)
        {
            return;
        }

        var graphHost = new Direct3D12AudioGraphHost
        {
            GraphMode = ResolveMixingSpectrumGraphMode()
        };
        MixingMicSpectrumGraphHost.Content = graphHost;
        _mixingMicSpectrumGraphHost = graphHost;
    }

    private static Direct3D12AudioGraphMode ResolveSelectedMicSpectrumGraphMode()
    {
        return Direct3D12AudioGraphMode.SelectedMicSpectrum;
    }

    private static Direct3D12AudioGraphMode ResolveMixingSpectrumGraphMode()
    {
        return Direct3D12AudioGraphMode.ProgramOutputSpectrum;
    }

    private void RecordProcessedAudioChanged(object sender, RoutedEventArgs e)
    {
        UpdateRecordingDspIndicator();
    }

    private void UpdateRecordingDspIndicator()
    {
        if (RecordProcessedAudioCheckBox is null
            || RecordingDspStatusText is null
            || RecordingDspIndicator is null)
        {
            return;
        }

        var processed = RecordProcessedAudioCheckBox.IsChecked == true;
        RecordingDspStatusText.Text = processed
            ? "DSP active"
            : "Recording natural audio";
        RecordingDspIndicator.Fill = processed
            ? _recordingDspActiveBrush
            : _recordingNaturalAudioBrush;
    }

    private void WaterfallLinesChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WaterfallLinesSlider is null)
        {
            return;
        }

        _waterfallLinesPerGap = Math.Clamp(
            (int)Math.Round(WaterfallLinesSlider.Value),
            Direct3D12AudioGraphHost.MinimumWaterfallLinesPerGap,
            Direct3D12AudioGraphHost.MaximumWaterfallLinesPerGap);
        if (Math.Abs(WaterfallLinesSlider.Value - _waterfallLinesPerGap) > 0.001d)
        {
            WaterfallLinesSlider.Value = _waterfallLinesPerGap;
        }

        ApplyWaterfallLineDensity();
        if (_showWaveform3D && _latestFrame is not null)
        {
            _waveform3DGraphHost?.AcceptFrame(CreateSelectedMicFrame(_latestFrame));
        }
    }

    private void ApplyWaterfallLineDensity()
    {
        if (_waveform3DGraphHost is not null)
        {
            _waveform3DGraphHost.WaterfallLinesPerGap = _waterfallLinesPerGap;
        }
    }

    private void EqSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.ToolTip = "0 dB is the yellow center mark. Drag near it to snap back to flat.";
        }
    }

    private void EqBandMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EqualizerBand band })
        {
            _hoveredEqualizerBand = band;
            UpdateEqualizerHoverRegion();
            UpdateDx12EqualizerHoverRegion();
        }
    }

    private void EqBandMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EqualizerBand band }
            && ReferenceEquals(_hoveredEqualizerBand, band))
        {
            _hoveredEqualizerBand = null;
            UpdateEqualizerHoverRegion();
            UpdateDx12EqualizerHoverRegion();
        }
    }

    private void EqSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider slider || Math.Abs(e.NewValue) > 0.35d || Math.Abs(e.NewValue) < 0.001d)
        {
            return;
        }

        slider.Value = 0d;
    }

    private void ProcessingSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSnappingProcessingSlider || sender is not Slider slider || !_processingSliderDefaults.TryGetValue(slider, out var defaultValue))
        {
            return;
        }

        var snapDistance = Math.Max(0.08d, (slider.Maximum - slider.Minimum) * 0.025d);
        if (Math.Abs(e.NewValue - defaultValue) > snapDistance || Math.Abs(e.NewValue - defaultValue) < 0.001d)
        {
            return;
        }

        _isSnappingProcessingSlider = true;
        slider.Value = defaultValue;
        _isSnappingProcessingSlider = false;
    }

    private async void MicrophoneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMicChannelUi)
        {
            return;
        }

        ClearAsioNoCallbackAutoStartSuppression();
        _selectedDevice = MicrophoneComboBox.SelectedItem as AudioInputDevice;
        if (_selectedDevice is not null && !IsEqualizerInputDevice(_selectedDevice))
        {
            StatusText.Text = "Computer audio and loopback inputs are mixer-only sources.";
            return;
        }

        _activeMicChannel.SelectedDevice = _selectedDevice;
        var selectedDevice = _selectedDevice;
        var selectedDeviceFormat = await GetDeviceFormatAsync(selectedDevice);
        if (!Equals(_selectedDevice, selectedDevice))
        {
            return;
        }

        _selectedDeviceFormat = selectedDeviceFormat;
        _isUpdatingMicChannelUi = true;
        try
        {
            RefreshInputChannelOptionsForActiveDevice(selectedDeviceFormat);
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }

        ConfigureLiveMixFromChannels();
        UpdateMixingMicLegend();
        await RestartSelectedAudioStreamAsync("Listening");
        PersistAppState();
    }

    private async void InputChannelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMicChannelUi)
        {
            return;
        }

        if (InputChannelComboBox.SelectedItem is InputChannelOption option)
        {
            _selectedInputChannelMode = option.Mode;
            _activeMicChannel.InputChannelMode = option.Mode;
        }

        ConfigureLiveMixFromChannels();
        UpdateMixingMicLegend();
        await RestartSelectedAudioStreamAsync("Listening");
        PersistAppState();
    }

    private async void MicChannelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMicChannelUi || MicChannelComboBox.SelectedItem is not MicChannelStrip channel)
        {
            return;
        }

        if (!IsEqualizerEditableMicChannel(channel))
        {
            StatusText.Text = "Computer audio and loopback inputs are controlled from the Mixing tab.";
            return;
        }

        await SetActiveMicChannelAsync(channel, restartAudio: true);
        PersistAppState();
    }

    private async void MixerChannelStripPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MicChannelStrip channel)
        {
            return;
        }

        await SetActiveMicChannelAsync(channel, restartAudio: false, refreshEditor: false);
        PersistAppState();
    }

    private async Task SetActiveMicChannelAsync(MicChannelStrip channel, bool restartAudio, bool refreshEditor = true)
    {
        if (ReferenceEquals(_activeMicChannel, channel))
        {
            UpdateActiveMicSelectionFlags();
            if (refreshEditor)
            {
                ApplyActiveMicChannelToUi();
            }
            else
            {
                ApplyActiveMicChannelSelectionToMixerUi();
            }

            return;
        }

        DetachEqualizerBandHandlers(_activeMicChannel);
        _activeMicChannel = channel;
        UpdateActiveMicSelectionFlags();
        AttachEqualizerBandHandlers(_activeMicChannel);
        if (refreshEditor)
        {
            DataContext = Settings;
            EqBandPanel.ItemsSource = Bands;
            UpdateEqVoiceZoneGuide();
            SyncEqualizerSettings();
            ApplyActiveMicChannelToUi();
            ApplyActiveMicPresetUiState();
            ConfigureLiveMixFromChannels();
        }
        else
        {
            ApplyActiveMicChannelSelectionToMixerUi();
            UpdateLiveMixControlsFromChannels();
        }

        UpdateMixingMicLegend();
        UpdateAudioRecordingReadyStatus();
        if (restartAudio)
        {
            await RestartSelectedAudioStreamAsync("Listening");
        }
    }

    private void ApplyActiveMicChannelToUi()
    {
        if (!IsEqualizerEditableMicChannel(_activeMicChannel))
        {
            ApplySelectedMixerInputPanelToUi();
            return;
        }

        var previousSelectedDevice = _selectedDevice;
        var previousSelectedDeviceFormat = _selectedDeviceFormat;
        _isUpdatingMicChannelUi = true;
        try
        {
            RefreshEqualizerMicChannelList();
            if (MicChannelComboBox is not null
                && IsEqualizerEditableMicChannel(_activeMicChannel)
                && !ReferenceEquals(MicChannelComboBox.SelectedItem, _activeMicChannel))
            {
                MicChannelComboBox.SelectedItem = _activeMicChannel;
            }

            _selectedDevice = _activeMicChannel.SelectedDevice;
            _selectedInputChannelMode = _activeMicChannel.InputChannelMode;
            _selectedDeviceFormat = previousSelectedDevice is not null
                && _selectedDevice is not null
                && AudioInputDevicesMatch(previousSelectedDevice, _selectedDevice)
                    ? previousSelectedDeviceFormat
                    : null;
            MicrophoneComboBox.SelectedItem = _selectedDevice;
            ApplySelectedMixerInputPanelToUi();
            RefreshInputChannelOptionsForActiveDevice(_selectedDeviceFormat);
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }

        QueueSelectedDeviceFormatRefresh(_selectedDevice);
        UpdateAudioFormatRouteText();
    }

    private void ApplyActiveMicChannelSelectionToMixerUi()
    {
        var previousSelectedDevice = _selectedDevice;
        var previousSelectedDeviceFormat = _selectedDeviceFormat;
        _isUpdatingMicChannelUi = true;
        try
        {
            RefreshEqualizerMicChannelList();
            if (MicChannelComboBox is not null
                && IsEqualizerEditableMicChannel(_activeMicChannel)
                && !ReferenceEquals(MicChannelComboBox.SelectedItem, _activeMicChannel))
            {
                MicChannelComboBox.SelectedItem = _activeMicChannel;
            }

            _selectedDevice = _activeMicChannel.SelectedDevice;
            _selectedInputChannelMode = _activeMicChannel.InputChannelMode;
            _selectedDeviceFormat = previousSelectedDevice is not null
                && _selectedDevice is not null
                && AudioInputDevicesMatch(previousSelectedDevice, _selectedDevice)
                    ? previousSelectedDeviceFormat
                    : null;
            ApplySelectedMixerInputPanelToUi();
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }
    }

    private void ApplySelectedMixerInputPanelToUi()
    {
        if (SelectedMixerInputPanel is not null && !ReferenceEquals(SelectedMixerInputPanel.DataContext, _activeMicChannel))
        {
            SelectedMixerInputPanel.DataContext = _activeMicChannel;
        }
    }

    private void QueueSelectedDeviceFormatRefresh(AudioInputDevice? selectedDevice)
    {
        if (selectedDevice is null || _selectedDeviceFormat is not null || _isClosing)
        {
            return;
        }

        var refreshVersion = ++_selectedDeviceFormatRefreshVersion;
        _ = RefreshSelectedDeviceFormatAsync(selectedDevice, refreshVersion);
    }

    private async Task RefreshSelectedDeviceFormatAsync(AudioInputDevice selectedDevice, int refreshVersion)
    {
        AudioDeviceFormat? selectedDeviceFormat;
        try
        {
            selectedDeviceFormat = await GetDeviceFormatAsync(selectedDevice);
        }
        catch
        {
            selectedDeviceFormat = null;
        }

        if (_isClosing
            || refreshVersion != _selectedDeviceFormatRefreshVersion
            || _selectedDevice is null
            || !AudioInputDevicesMatch(_selectedDevice, selectedDevice))
        {
            return;
        }

        _selectedDeviceFormat = selectedDeviceFormat;
        RefreshInputChannelOptionsWithoutSelectionEvents(selectedDeviceFormat);
        UpdateAudioFormatRouteText();
    }

    private void RefreshInputChannelOptionsForActiveDevice(AudioDeviceFormat? selectedDeviceFormat)
    {
        if (InputChannelComboBox is null || _activeMicChannel is null)
        {
            return;
        }

        var options = CreateInputChannelOptions(_activeMicChannel.SelectedDevice, selectedDeviceFormat);
        InputChannelComboBox.ItemsSource = options;
        var selectedOption = options.FirstOrDefault(option => option.Mode == _activeMicChannel.InputChannelMode)
            ?? options.FirstOrDefault(option => option.Mode == InputChannelMode.MonoSum)
            ?? options.FirstOrDefault();
        if (selectedOption is not null)
        {
            _activeMicChannel.InputChannelMode = selectedOption.Mode;
            _selectedInputChannelMode = selectedOption.Mode;
            InputChannelComboBox.SelectedItem = selectedOption;
        }
    }

    private void RefreshInputChannelOptionsWithoutSelectionEvents(AudioDeviceFormat? selectedDeviceFormat)
    {
        var previousMode = _selectedInputChannelMode;
        _isUpdatingMicChannelUi = true;
        try
        {
            RefreshInputChannelOptionsForActiveDevice(selectedDeviceFormat);
        }
        finally
        {
            _isUpdatingMicChannelUi = false;
        }

        if (_selectedInputChannelMode != previousMode)
        {
            ConfigureLiveMixFromChannels();
        }
    }

    private void OutputDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingOutputRoutingUi)
        {
            return;
        }

        var selectedDevice = sender == KaraokeMonitorOutputDeviceComboBox
            ? KaraokeMonitorOutputDeviceComboBox.SelectedItem as AudioOutputDevice
            : OutputDeviceComboBox.SelectedItem as AudioOutputDevice;
        SetSelectedOutputDevice(selectedDevice);
        UpdateOutputRouting();
        PersistAppState();
    }

    private void ProcessedOutputChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingOutputRoutingUi)
        {
            return;
        }

        if (sender is ToggleButton toggle)
        {
            SetProcessedOutputToggleState(toggle.IsChecked == true);
        }

        UpdateOutputRouting();
        PersistAppState();
    }

    private bool TrySnapMixerUnitySlider(object sender)
    {
        if (_isSnappingMixerUnitySlider
            || sender is not Slider slider
            || !IsMixerUnityVolumeSlider(slider))
        {
            return false;
        }

        var snappedValue = SnapMixerUnityVolumePercent(slider.Value);
        if (Math.Abs(snappedValue - slider.Value) < 0.001d)
        {
            return false;
        }

        _isSnappingMixerUnitySlider = true;
        try
        {
            slider.Value = snappedValue;
        }
        finally
        {
            _isSnappingMixerUnitySlider = false;
        }

        return true;
    }

    private static bool IsMixerUnityVolumeSlider(Slider slider)
    {
        return Math.Abs(slider.Minimum) < 0.001d
            && Math.Abs(slider.Maximum - 150d) < 0.001d;
    }

    private static double SnapMixerUnityVolumePercent(double value)
    {
        if (!double.IsFinite(value)
            || Math.Abs(value - MixerUnityVolumePercent) > MixerUnityMagnetismPercent)
        {
            return value;
        }

        return MixerUnityVolumePercent;
    }

    private void MixerChannelControlChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        if (TrySnapMixerUnitySlider(sender))
        {
            return;
        }

        UpdateLiveMixControlsFromChannels();
        ScheduleAppStatePersist();
    }

    private void MixerChannelControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        if (TrySnapMixerUnitySlider(sender))
        {
            return;
        }

        UpdateLiveMixControlsFromChannels();
        ScheduleAppStatePersist();
    }

    private void ResetSelectedMixerChannelClicked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMixerUi || _activeMicChannel is not MicChannelStrip channel)
        {
            return;
        }

        channel.ResetMixerControls();
        ConfigureLiveMixFromChannels();
        PersistAppState();
    }

    private void MixerChannelNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        UpdateMixingMicLegend();
        PersistAppState();
    }

    private void MasterMixControlChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        if (TrySnapMixerUnitySlider(sender))
        {
            return;
        }

        ReadMasterMixControls();
        ConfigureLiveMixFromChannels();
        PersistAppState();
    }

    private void MasterMixControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        if (TrySnapMixerUnitySlider(sender))
        {
            return;
        }

        ReadMasterMixControls();
        ConfigureLiveMixFromChannels();
        PersistAppState();
    }

    private void MasterOutputModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        ReadMasterMixControls();
        ConfigureLiveMixFromChannels();
        PersistAppState();
    }

    private void AudioRecordingSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMixerUi)
        {
            return;
        }

        ReadAudioRecordingSourceControl();
        ConfigureLiveMixFromChannels();
        UpdateMixingMicLegend();
        UpdateAudioRecordingReadyStatus();
        PersistAppState();
    }

    private void UpdateAudioRecordingReadyStatus()
    {
        if (AudioRecordingStatusText is null || _isStandaloneAudioRecording)
        {
            return;
        }

        AudioRecordingStatusText.Text = $"Ready to record {GetAudioRecordingSourceLabel()}.";
    }

    private string GetAudioRecordingSourceLabel()
    {
        var selectedMicName = string.IsNullOrWhiteSpace(_activeMicChannel?.DisplayName)
            ? $"Mic {_activeMicChannel?.ChannelNumber ?? 1}"
            : _activeMicChannel.DisplayName;
        return _audioRecordingSource switch
        {
            ProcessedRecordingSource.SelectedMicProcessed => $"{selectedMicName} processed",
            ProcessedRecordingSource.SelectedMicRawBackup => $"{selectedMicName} raw backup",
            _ => "program mix"
        };
    }

    private void ApplyMixerStateToUi()
    {
        _masterMixVolumePercent = Math.Clamp(_appSettings.MixerMasterVolumePercent ?? 100d, 0d, 150d);
        _masterMixNormalizeEnabled = !HasPersistedAppState() || _appSettings.MixerAutoNormalizeEnabled;
        _masterMixLimiterEnabled = !HasPersistedAppState() || _appSettings.MixerLimiterEnabled;
        _masterMixLimiterCeilingDb = Math.Clamp(_appSettings.MixerLimiterCeilingDb ?? -1d, -12d, 0d);
        _masterMixOutputMode = Enum.TryParse<MixBusOutputMode>(_appSettings.MixerOutputMode, ignoreCase: true, out var outputMode)
            ? outputMode
            : MixBusOutputMode.Stereo;
        _audioRecordingSource = Enum.TryParse<ProcessedRecordingSource>(_appSettings.AudioRecordingSource, ignoreCase: true, out var recordingSource)
            ? recordingSource
            : ProcessedRecordingSource.ProgramMix;

        _isUpdatingMixerUi = true;
        try
        {
            MasterVolumeSlider.Value = _masterMixVolumePercent;
            MasterOutputModeComboBox.SelectedIndex = _masterMixOutputMode == MixBusOutputMode.Mono ? 1 : 0;
            AudioRecordingSourceComboBox.SelectedIndex = _audioRecordingSource switch
            {
                ProcessedRecordingSource.SelectedMicProcessed => 1,
                ProcessedRecordingSource.SelectedMicRawBackup => 2,
                _ => 0
            };
            MasterNormalizeToggle.IsChecked = _masterMixNormalizeEnabled;
            MasterLimiterToggle.IsChecked = _masterMixLimiterEnabled;
            MasterLimiterCeilingSlider.Value = _masterMixLimiterCeilingDb;
        }
        finally
        {
            _isUpdatingMixerUi = false;
        }

        UpdateAudioRecordingReadyStatus();
    }

    private void ReadMasterMixControls()
    {
        _masterMixVolumePercent = MasterVolumeSlider is null
            ? _masterMixVolumePercent
            : Math.Clamp(MasterVolumeSlider.Value, 0d, 150d);
        _masterMixNormalizeEnabled = MasterNormalizeToggle?.IsChecked == true;
        _masterMixLimiterEnabled = MasterLimiterToggle?.IsChecked == true;
        _masterMixLimiterCeilingDb = MasterLimiterCeilingSlider is null
            ? _masterMixLimiterCeilingDb
            : Math.Clamp(MasterLimiterCeilingSlider.Value, -12d, 0d);
        _masterMixOutputMode = MasterOutputModeComboBox?.SelectedIndex == 1
            ? MixBusOutputMode.Mono
            : MixBusOutputMode.Stereo;
    }

    private void ReadAudioRecordingSourceControl()
    {
        _audioRecordingSource = AudioRecordingSourceComboBox?.SelectedIndex switch
        {
            1 => ProcessedRecordingSource.SelectedMicProcessed,
            2 => ProcessedRecordingSource.SelectedMicRawBackup,
            _ => ProcessedRecordingSource.ProgramMix
        };
    }

    private void SetSelectedOutputDevice(AudioOutputDevice? selectedDevice)
    {
        _selectedOutputDevice = selectedDevice;
        _isUpdatingOutputRoutingUi = true;
        try
        {
            if (OutputDeviceComboBox is not null && !Equals(OutputDeviceComboBox.SelectedItem, selectedDevice))
            {
                OutputDeviceComboBox.SelectedItem = selectedDevice;
            }

            if (KaraokeMonitorOutputDeviceComboBox is not null && !Equals(KaraokeMonitorOutputDeviceComboBox.SelectedItem, selectedDevice))
            {
                KaraokeMonitorOutputDeviceComboBox.SelectedItem = selectedDevice;
            }
        }
        finally
        {
            _isUpdatingOutputRoutingUi = false;
        }

        UpdateOutputAudioSessionText();
    }

    private void SetProcessedOutputToggleState(bool enabled)
    {
        _isUpdatingOutputRoutingUi = true;
        try
        {
            if (ProcessedOutputCheckBox is not null)
            {
                ProcessedOutputCheckBox.IsChecked = enabled;
            }

            if (KaraokeMonitorOutputCheckBox is not null)
            {
                KaraokeMonitorOutputCheckBox.IsChecked = enabled;
            }
        }
        finally
        {
            _isUpdatingOutputRoutingUi = false;
        }
    }

    private bool IsProcessedOutputRequested()
    {
        return ProcessedOutputCheckBox?.IsChecked == true || KaraokeMonitorOutputCheckBox?.IsChecked == true;
    }

    private void ApplyWasapiOutputSettingsToUi()
    {
        _isUpdatingWasapiOutputUi = true;
        try
        {
            if (WasapiOutputProfileComboBox is not null)
            {
                WasapiOutputProfileComboBox.SelectedIndex = GetWasapiOutputProfileIndex(_wasapiOutputSettings.Profile);
            }

            if (WasapiExclusiveModeCheckBox is not null)
            {
                WasapiExclusiveModeCheckBox.IsChecked = _wasapiOutputSettings.ExclusiveMode;
            }

            if (WasapiCustomLatencySlider is not null)
            {
                WasapiCustomLatencySlider.Value = _wasapiOutputSettings.CustomLatencyMilliseconds;
                WasapiCustomLatencySlider.IsEnabled = _wasapiOutputSettings.Profile == WasapiOutputLatencyProfile.Custom;
            }
        }
        finally
        {
            _isUpdatingWasapiOutputUi = false;
        }

        UpdateWasapiOutputModeText();
    }

    private void WasapiOutputProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyWasapiOutputSettingsFromUi();
    }

    private void WasapiOutputOptionChanged(object sender, RoutedEventArgs e)
    {
        ApplyWasapiOutputSettingsFromUi();
    }

    private void WasapiOutputLatencyChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WasapiCustomLatencyValueText is not null)
        {
            WasapiCustomLatencyValueText.Text = $"{WasapiOutputSettings.ClampCustomLatency((int)Math.Round(e.NewValue))} ms";
        }

        ApplyWasapiOutputSettingsFromUi();
    }

    private void ApplyWasapiOutputSettingsFromUi()
    {
        if (_isUpdatingWasapiOutputUi)
        {
            return;
        }

        _wasapiOutputSettings = new WasapiOutputSettings(
            GetWasapiOutputProfileFromIndex(WasapiOutputProfileComboBox?.SelectedIndex ?? 0),
            WasapiExclusiveModeCheckBox?.IsChecked == true,
            WasapiOutputSettings.ClampCustomLatency((int)Math.Round(WasapiCustomLatencySlider?.Value ?? WasapiOutputSettings.StabilityLatencyMilliseconds)));
        ApplyWasapiOutputSettingsToUi();
        try
        {
            _spectrumService.ConfigureWasapiOutput(_wasapiOutputSettings);
            if (IsProcessedOutputRequested())
            {
                UpdateOutputRouting();
            }
        }
        catch (Exception ex)
        {
            SetProcessedOutputToggleState(false);
            if (OutputStatusText is not null)
            {
                OutputStatusText.Text = $"WASAPI mode unavailable: {ex.Message}";
            }
        }

        PersistAppState();
        UpdateAudioFormatRouteText();
    }

    private void UpdateWasapiOutputModeText()
    {
        if (WasapiOutputModeText is not null)
        {
            WasapiOutputModeText.Text = $"WASAPI: {_wasapiOutputSettings.DisplayText}";
        }

        if (WasapiCustomLatencyValueText is not null)
        {
            WasapiCustomLatencyValueText.Text = $"{_wasapiOutputSettings.CustomLatencyMilliseconds} ms";
        }
    }

    private static int GetWasapiOutputProfileIndex(WasapiOutputLatencyProfile profile)
    {
        return profile switch
        {
            WasapiOutputLatencyProfile.Balanced => 1,
            WasapiOutputLatencyProfile.LowLatency => 2,
            WasapiOutputLatencyProfile.Custom => 3,
            _ => 0
        };
    }

    private static WasapiOutputLatencyProfile GetWasapiOutputProfileFromIndex(int index)
    {
        return index switch
        {
            1 => WasapiOutputLatencyProfile.Balanced,
            2 => WasapiOutputLatencyProfile.LowLatency,
            3 => WasapiOutputLatencyProfile.Custom,
            _ => WasapiOutputLatencyProfile.Stability
        };
    }

    private void ConfigureLiveMixFromChannels()
    {
        var channels = CreateLiveMixChannelSettings(coerceInputModes: true);
        _spectrumService.ConfigureLiveMix(channels, CreateMixBusSettings());
        _spectrumService.ConfigureProcessedRecordingSource(_audioRecordingSource, _activeMicChannel?.ChannelNumber ?? 1);
    }

    private void UpdateLiveMixControlsFromChannels()
    {
        var channels = CreateLiveMixChannelSettings(coerceInputModes: false);
        if (!_spectrumService.TryUpdateLiveMixControls(channels, CreateMixBusSettings()))
        {
            ConfigureLiveMixFromChannels();
            return;
        }

        _spectrumService.ConfigureProcessedRecordingSource(_audioRecordingSource, _activeMicChannel?.ChannelNumber ?? 1);
    }

    private List<MicrophoneLiveChannelSettings> CreateLiveMixChannelSettings(bool coerceInputModes)
    {
        return _micChannels
            .Where(channel => channel.SelectedDevice is not null)
            .Select(channel =>
            {
                var inputChannelMode = channel.InputChannelMode;
                if (coerceInputModes)
                {
                    var selectedDeviceFormat = ReferenceEquals(channel, _activeMicChannel)
                        ? _selectedDeviceFormat
                        : null;
                    inputChannelMode = CoerceInputChannelModeForDevice(
                        channel.SelectedDevice,
                        selectedDeviceFormat,
                        channel.InputChannelMode);
                    if (inputChannelMode != channel.InputChannelMode)
                    {
                        channel.InputChannelMode = inputChannelMode;
                        if (ReferenceEquals(channel, _activeMicChannel))
                        {
                            _selectedInputChannelMode = inputChannelMode;
                        }
                    }
                }

                return new MicrophoneLiveChannelSettings(
                    channel.ChannelNumber,
                    channel.SelectedDevice!.DeviceNumber,
                    inputChannelMode,
                    channel.ProcessorSettings,
                    channel.VolumePercent,
                    channel.InputGainDb,
                    channel.Pan,
                    channel.PolarityInverted,
                    channel.IsSoloed,
                    channel.DelayMilliseconds,
                    true,
                    channel.IsMuted,
                    channel.SelectedDevice!.EndpointId,
                    channel.SelectedDevice.Backend);
            })
            .ToList();
    }

    private List<MidiControlMappingSettingsState> CaptureMidiControlMappingStates()
    {
        return _midiControlMappings
            .Select(mapping => new MidiControlMappingSettingsState
            {
                ActionName = mapping.ActionName,
                MessageType = mapping.MessageType,
                Channel = mapping.Channel,
                Data1 = mapping.Data1
            })
            .ToList();
    }

    private MixBusSettings CreateMixBusSettings()
    {
        return new MixBusSettings(
            _masterMixVolumePercent,
            _masterMixNormalizeEnabled,
            _masterMixLimiterEnabled,
            _masterMixLimiterCeilingDb,
            _masterMixOutputMode);
    }

    private void UpdateOutputRouting()
    {
        if (_selectedOutputDevice is null || ProcessedOutputCheckBox is null || OutputStatusText is null)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        var enabled = IsProcessedOutputRequested();
        try
        {
            if (enabled && !_spectrumService.IsRunning)
            {
                StartSelectedDevice();
            }

            _spectrumService.ConfigureWasapiOutput(_wasapiOutputSettings);
            _spectrumService.ConfigureProcessedOutput(enabled, _selectedOutputDevice);
            var outputFormatStatus = BuildOutputFormatStatus();
            var loopbackFeedbackWarning = enabled ? BuildLoopbackFeedbackWarning() : null;
            OutputStatusText.Text = enabled
                ? loopbackFeedbackWarning is null
                    ? $"Sending program mix to selected output: {_selectedOutputDevice.Name}. {outputFormatStatus}"
                    : $"{loopbackFeedbackWarning} {outputFormatStatus}"
                : "Output off. Pick a virtual cable input when you want live or podcast routing.";
            if (KaraokeMonitorStatusText is not null)
            {
                KaraokeMonitorStatusText.Text = enabled
                    ? loopbackFeedbackWarning is null
                        ? $"Live vocal monitor is feeding {_selectedOutputDevice.Name}. {outputFormatStatus}"
                        : $"{loopbackFeedbackWarning} {outputFormatStatus}"
                    : "Live vocal monitor is off.";
            }

            UpdateAudioFormatRouteText();
        }
        catch (Exception ex)
        {
            SetProcessedOutputToggleState(false);
            OutputStatusText.Text = $"Output unavailable: {ex.Message}";
            if (KaraokeMonitorStatusText is not null)
            {
                KaraokeMonitorStatusText.Text = $"Monitor unavailable: {ex.Message}";
            }

            UpdateAudioFormatRouteText();
        }
    }

    private string? BuildLoopbackFeedbackWarning()
    {
        if (!HasAudibleSystemAudioLoopbackChannel() || !IsSelectedOutputCapturedBySystemLoopback())
        {
            return null;
        }

        return "Loopback warning: Computer Audio captures the default playback device, and the program mix is routed back to that same playback path. Choose a virtual cable, headphones, or a second output to avoid recapturing the mix.";
    }

    private bool HasAudibleSystemAudioLoopbackChannel()
    {
        return _micChannels.Any(channel =>
            channel.SelectedDevice?.IsSystemAudioLoopback == true
            && !channel.IsMuted
            && channel.VolumePercent > 0.1d);
    }

    private bool IsSelectedOutputCapturedBySystemLoopback()
    {
        if (_selectedOutputDevice is null)
        {
            return false;
        }

        if (_selectedOutputDevice.DeviceNumber < 0 && string.IsNullOrWhiteSpace(_selectedOutputDevice.EndpointId))
        {
            return true;
        }

        var defaultEndpointId = MicrophoneSpectrumService.GetDefaultPlaybackEndpointId();
        return !string.IsNullOrWhiteSpace(defaultEndpointId)
            && _selectedOutputDevice.EndpointId?.Equals(defaultEndpointId, StringComparison.OrdinalIgnoreCase) == true;
    }

    private string BuildOutputFormatStatus()
    {
        var inputFormat = _selectedDeviceFormat;
        var outputFormat = _selectedOutputDevice is null
            ? null
            : MicrophoneSpectrumService.TryGetOutputDeviceFormat(_selectedOutputDevice);
        var wasapiText = _selectedOutputDevice?.IsAsio == true
            ? " ASIO output bypasses WASAPI profiles."
            : $" WASAPI: {_spectrumService.WasapiOutputModeStatus}.";
        if (inputFormat is null || outputFormat is null)
        {
            return $"{_spectrumService.ProcessedOutputFormatStatus} Virtual audio cables are supported; choose the matching cable output as the mic in your DAW or podcast app.{wasapiText}";
        }

        return inputFormat.Value.SampleRate == outputFormat.Value.SampleRate
            ? $"{_spectrumService.ProcessedOutputFormatStatus} Direct-rate path: mic {inputFormat.Value}, output {outputFormat.Value}. No output resampling.{wasapiText}"
            : $"{_spectrumService.ProcessedOutputFormatStatus} High-quality output resampling: mic {inputFormat.Value}, output {outputFormat.Value}. Match Windows sample rates for the cleanest possible path.{wasapiText}";
    }

    private void UpdateOutputAudioSessionText()
    {
        if (OutputAudioSessionsText is null)
        {
            return;
        }

        var sessions = _selectedOutputDevice?.IsAsio == true
            ? []
            : MicrophoneSpectrumService.GetOutputAudioSessions(_selectedOutputDevice);
        OutputAudioSessionsText.Text = BuildOutputAudioSessionText(_selectedOutputDevice, sessions);
        RefreshCoreAudioSessionItems(sessions);
    }

    private void RefreshCoreAudioSessionItems(IReadOnlyList<CoreAudioSessionSnapshot> sessions)
    {
        if (CoreAudioSessionsItemsControl is null)
        {
            return;
        }

        _isUpdatingCoreAudioSessionUi = true;
        try
        {
            _coreAudioSessionItems.Clear();
            if (_selectedOutputDevice is null || _selectedOutputDevice.IsAsio)
            {
                return;
            }

            foreach (var session in sessions.Take(10))
            {
                _coreAudioSessionItems.Add(new CoreAudioSessionControlItem(session));
            }
        }
        finally
        {
            _isUpdatingCoreAudioSessionUi = false;
        }
    }

    private static string BuildOutputAudioSessionText(
        AudioOutputDevice? selectedOutputDevice,
        IReadOnlyList<CoreAudioSessionSnapshot> sessions)
    {
        if (selectedOutputDevice is null)
        {
            return "Apps on selected output: no output selected.";
        }

        if (selectedOutputDevice.IsAsio)
        {
            return "Apps on selected output: ASIO bypasses Windows app sessions.";
        }

        var activeSessions = sessions
            .Where(session => session.State.Equals("Active", StringComparison.OrdinalIgnoreCase)
                || session.PeakLevel > 0.0001f)
            .Take(5)
            .Select(CoreAudioSessionCatalog.FormatSessionSummary)
            .ToArray();

        return activeSessions.Length == 0
            ? "Apps on selected output: none active."
            : $"Apps on selected output: {string.Join("; ", activeSessions)}";
    }

    private void RefreshCoreAudioSessionsClicked(object sender, RoutedEventArgs e)
    {
        UpdateOutputAudioSessionText();
        StatusText.Text = "CoreAudio app sessions refreshed.";
    }

    private void CoreAudioSessionMuteChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCoreAudioSessionUi || sender is not CheckBox { DataContext: CoreAudioSessionControlItem item })
        {
            return;
        }

        ApplyCoreAudioSessionControls(item, null, item.IsMuted);
    }

    private void CoreAudioSessionVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingCoreAudioSessionUi || sender is not Slider { DataContext: CoreAudioSessionControlItem item } slider || !slider.IsLoaded)
        {
            return;
        }

        item.VolumePercent = Math.Clamp(e.NewValue, 0d, 100d);
        ApplyCoreAudioSessionControls(item, (float)(item.VolumePercent / 100d), null);
    }

    private void ApplyCoreAudioSessionControls(CoreAudioSessionControlItem item, float? volume, bool? isMuted)
    {
        if (MicrophoneSpectrumService.TrySetOutputAudioSessionControls(
                _selectedOutputDevice,
                item.ControlTargets,
                volume,
                isMuted,
                out var status))
        {
            if (volume.HasValue)
            {
                item.VolumePercent = Math.Clamp(volume.Value * 100d, 0d, 100d);
            }

            if (isMuted.HasValue)
            {
                item.IsMuted = isMuted.Value;
            }

            StatusText.Text = status;
            UpdateOutputAudioSessionText();
            return;
        }

        StatusText.Text = status;
        UpdateOutputAudioSessionText();
    }

    private void UpdateAudioFormatRouteText()
    {
        if (AudioFormatRouteText is null)
        {
            return;
        }

        var inputFormat = _selectedDeviceFormat;
        var receiving = _spectrumService.IsRunning
            ? _spectrumService.ActiveInputFormatStatus
            : inputFormat?.ToString() ?? "not open";
        var targetOutput = _spectrumService.IsRunning
            ? _spectrumService.TargetProcessedOutputFormatStatus
            : BuildTargetOutputFormatStatus(inputFormat);
        var routed = IsProcessedOutputRequested() && _spectrumService.IsProcessedOutputEnabled;
        var constrained = routed && _spectrumService.IsProcessedOutputFormatConstrained;
        var text = $"Receiving: {receiving} | Trying output: {targetOutput}";
        if (_selectedOutputDevice?.IsAsio != true)
        {
            text += $" | WASAPI: {_spectrumService.WasapiOutputModeStatus}";
        }

        if (routed)
        {
            text += constrained
                ? $" -> Actual: {_spectrumService.ActualProcessedOutputFormatStatus}"
                : " (opened)";
        }
        else
        {
            text += " (routing off)";
        }

        AudioFormatRouteText.Text = text;
        AudioFormatRouteText.Foreground = constrained ? _meterWarnBrush : _meterTextMutedBrush;
    }

    private static string BuildTargetOutputFormatStatus(AudioDeviceFormat? inputFormat)
    {
        return inputFormat is null
            ? "not ready"
            : $"{inputFormat.Value.SampleRate / 1000d:0.#} kHz, 2 ch, 32-bit float";
    }

    private MicChannelStrip? ResolvePrimaryCaptureChannel()
    {
        var candidates = _micChannels
            .Where(channel => channel.SelectedDevice is not null)
            .Select(channel => new PrimaryCaptureCandidate(
                channel.ChannelNumber,
                channel.SelectedDevice!.DeviceNumber,
                ReferenceEquals(channel, _activeMicChannel),
                channel.IsMuted,
                channel.SelectedDevice.EndpointId,
                channel.SelectedDevice.Backend))
            .ToArray();
        var channelNumber = PrimaryCaptureSelector.ResolveChannelNumber(
            candidates,
            _selectedDevice?.DeviceNumber,
            _selectedDevice?.EndpointId,
            _selectedDevice?.Backend ?? AudioInputBackend.Windows);
        return channelNumber is null ? null : FindMicChannel(channelNumber.Value);
    }

    private void StartSelectedDevice()
    {
        if (_isRestartingAudioStream)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        var primaryCaptureChannel = ResolvePrimaryCaptureChannel();
        var selectedDevice = primaryCaptureChannel?.SelectedDevice ?? _selectedDevice;
        if (selectedDevice is null)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        if (IsAsioNoCallbackAutoStartSuppressed(selectedDevice))
        {
            StatusText.Text = CreateAsioNoCallbackSuppressedStatus();
            UpdateAudioFormatRouteText();
            return;
        }

        var previousSelectedDevice = _selectedDevice;
        _selectedDevice = selectedDevice;
        if (!Equals(previousSelectedDevice, selectedDevice))
        {
            _selectedDeviceFormat = null;
            _lastShownAsioNoCallbackDiagnosticKey = null;
            ClearAsioNoCallbackAutoStartSuppression();
        }

        if (primaryCaptureChannel is not null)
        {
            _selectedInputChannelMode = primaryCaptureChannel.InputChannelMode;
        }

        try
        {
            ClearLiveSpectrumDisplay();
            ConfigureLiveMixFromChannels();
            _spectrumService.Start(
                selectedDevice,
                primaryCaptureChannel?.ProcessorSettings ?? Settings,
                _selectedInputChannelMode);
            ResetAudioStreamRestartBackoff();
            StatusText.Text = selectedDevice.IsAsio
                ? CreateWaitingForAudioCallbacksStatus(selectedDevice)
                : "Listening";
            _selectedDeviceFormat ??= GetSelectedDeviceFormat();
            RefreshInputChannelOptionsWithoutSelectionEvents(_selectedDeviceFormat);
            UpdateAudioFormatRouteText();
        }
        catch (Exception ex)
        {
            RegisterAudioStreamRestartFailure(ex, "Mic unavailable");
            UpdateAudioFormatRouteText();
        }
    }

    private void SpectrumServiceStreamStatusChanged(object? sender, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = message;
            if (message.Contains("recovered", StringComparison.OrdinalIgnoreCase))
            {
                ResetAudioStreamRestartBackoff();
                _selectedDeviceFormat = GetSelectedDeviceFormat();
                RefreshInputChannelOptionsWithoutSelectionEvents(_selectedDeviceFormat);
            }

            UpdateAudioFormatRouteText();
        });
    }

    private async void AudioDeviceFormatTimerTick(object? sender, EventArgs e)
    {
        var selectedDevice = _selectedDevice;
        if (selectedDevice is null || _isRestartingAudioStream || _isCheckingAudioDeviceFormat || _isClosing)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        if (IsAudioStreamRestartBackoffActive())
        {
            UpdateAudioFormatRouteText();
            return;
        }

        if (IsAsioNoCallbackAutoStartSuppressed(selectedDevice))
        {
            ClearLiveSpectrumDisplay();
            StatusText.Text = CreateAsioNoCallbackSuppressedStatus();
            UpdateAudioFormatRouteText();
            return;
        }

        if (!_spectrumService.IsRunning)
        {
            await RestartSelectedAudioStreamAsync("Audio stream stopped; reopened mic stream.");
            return;
        }

        if (_spectrumService.IsWaitingForFirstAudioCallback(AudioCallbackStartupGrace))
        {
            ClearLiveSpectrumDisplay();
            if (!StatusText.Text.Contains("no audio callbacks", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = CreateWaitingForAudioCallbacksStatus(selectedDevice);
            }

            UpdateAudioFormatRouteText();
            return;
        }

        if (_spectrumService.AreAudioCallbacksStale(AudioCallbackStartupGrace, AudioCallbackStaleDuration))
        {
            var preferWaveInFallback = _spectrumService.IsWasapiCaptureActive;
            var asioNoCallbacks = !_spectrumService.HasReceivedAudioCallbacks && selectedDevice.IsAsio;
            if (asioNoCallbacks)
            {
                ShowAsioNoCallbackDiagnosticsOnce(selectedDevice);
                _asioNoCallbackAutoStartSuppressedDeviceKey = CreateAsioNoCallbackSuppressionKey(selectedDevice);
                _spectrumService.Stop();
                ClearLiveSpectrumDisplay();
                StatusText.Text = CreateAsioNoCallbackSuppressedStatus();
                UpdateAudioFormatRouteText();
                return;
            }

            ClearLiveSpectrumDisplay();
            await RestartSelectedAudioStreamAsync(
                preferWaveInFallback
                    ? "Audio callback stopped on WASAPI; reopened mic stream with WaveIn fallback."
                    : "Audio callback stopped; reopened mic stream.",
                preferWaveInFallback);
            return;
        }

        _isCheckingAudioDeviceFormat = true;
        AudioDeviceFormat? currentFormat;
        try
        {
            currentFormat = await GetDeviceFormatAsync(selectedDevice);
        }
        finally
        {
            _isCheckingAudioDeviceFormat = false;
        }

        if (_isClosing || !Equals(_selectedDevice, selectedDevice))
        {
            return;
        }

        if (currentFormat is null)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        if (_selectedDeviceFormat is null)
        {
            _selectedDeviceFormat = currentFormat;
            RefreshInputChannelOptionsWithoutSelectionEvents(currentFormat);
            UpdateAudioFormatRouteText();
            return;
        }

        if (currentFormat.Value == _selectedDeviceFormat.Value)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        var previousFormat = _selectedDeviceFormat.Value;
        _selectedDeviceFormat = currentFormat;
        RefreshInputChannelOptionsWithoutSelectionEvents(currentFormat);
        await RestartSelectedAudioStreamAsync($"Audio device changed from {previousFormat} to {currentFormat.Value}; reopened mic stream.");
    }

    private AudioDeviceFormat? GetSelectedDeviceFormat()
    {
        return _selectedDevice is null
            ? null
            : MicrophoneSpectrumService.TryGetInputDeviceFormat(_selectedDevice);
    }

    private static Task<AudioDeviceFormat?> GetDeviceFormatAsync(AudioInputDevice? device)
    {
        return device is null
            ? Task.FromResult<AudioDeviceFormat?>(null)
            : Task.Run(() => MicrophoneSpectrumService.TryGetInputDeviceFormat(device));
    }

    private bool IsAudioStreamRestartBackoffActive()
    {
        return DateTime.UtcNow < _nextAudioStreamRestartAttemptUtc;
    }

    private void ResetAudioStreamRestartBackoff()
    {
        _audioStreamRestartFailureCount = 0;
        _nextAudioStreamRestartAttemptUtc = DateTime.MinValue;
        _lastAudioStreamRestartFailureMessage = null;
    }

    private void RegisterAudioStreamRestartFailure(Exception exception, string statusPrefix = "Audio stream refresh failed")
    {
        _audioStreamRestartFailureCount++;
        var retryDelay = GetAudioStreamRestartBackoff();
        _nextAudioStreamRestartAttemptUtc = DateTime.UtcNow.Add(retryDelay);
        StatusText.Text = $"{statusPrefix}: {exception.Message}. Auto-retry in {FormatAudioStreamRestartDelay(retryDelay)}.";

        var failureMessage = exception.ToString();
        if (!string.Equals(_lastAudioStreamRestartFailureMessage, failureMessage, StringComparison.Ordinal)
            || _audioStreamRestartFailureCount == 1
            || _audioStreamRestartFailureCount % 5 == 0)
        {
            AppStateStore.LogDiagnostic(
                "audio-stream-refresh-failed",
                $"Attempt={_audioStreamRestartFailureCount}; NextRetryUtc={_nextAudioStreamRestartAttemptUtc:O}{Environment.NewLine}{failureMessage}");
        }

        _lastAudioStreamRestartFailureMessage = failureMessage;
    }

    private TimeSpan GetAudioStreamRestartBackoff()
    {
        var exponent = Math.Min(Math.Max(_audioStreamRestartFailureCount, 1) - 1, 4);
        var retrySeconds = Math.Min(
            AudioStreamRestartMaximumBackoff.TotalSeconds,
            AudioStreamRestartBaseBackoff.TotalSeconds * Math.Pow(2d, exponent));
        return TimeSpan.FromSeconds(retrySeconds);
    }

    private static string FormatAudioStreamRestartDelay(TimeSpan delay)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }

    private async Task RestartSelectedAudioStreamAsync(string statusMessage, bool preferWaveInFallback = false)
    {
        var primaryCaptureChannel = ResolvePrimaryCaptureChannel();
        var selectedDevice = primaryCaptureChannel?.SelectedDevice ?? _selectedDevice;
        if (selectedDevice is null || _isClosing)
        {
            return;
        }

        var previousSelectedDevice = _selectedDevice;
        _selectedDevice = selectedDevice;
        if (!Equals(previousSelectedDevice, selectedDevice))
        {
            _selectedDeviceFormat = null;
            _lastShownAsioNoCallbackDiagnosticKey = null;
        }

        var operationVersion = ++_audioStreamOperationVersion;
        var processorSettings = primaryCaptureChannel?.ProcessorSettings ?? Settings;
        var inputChannelMode = primaryCaptureChannel?.InputChannelMode ?? _selectedInputChannelMode;
        _selectedInputChannelMode = inputChannelMode;
        _isRestartingAudioStream = true;
        StatusText.Text = "Reopening audio stream...";
        ClearLiveSpectrumDisplay();
        UpdateAudioFormatRouteText();
        try
        {
            if (preferWaveInFallback)
            {
                _spectrumService.PreferWaveInCaptureForCurrentDevice();
            }

            ConfigureLiveMixFromChannels();
            await Task.Run(() =>
            {
                _spectrumService.RestartCapture(selectedDevice, processorSettings, inputChannelMode, TimeSpan.FromMilliseconds(850));
            });
            if (_isClosing || operationVersion != _audioStreamOperationVersion || !Equals(_selectedDevice, selectedDevice))
            {
                return;
            }

            ResetAudioStreamRestartBackoff();
            StatusText.Text = selectedDevice.IsAsio
                && !_spectrumService.HasReceivedAudioCallbacks
                && !statusMessage.Contains("no audio callbacks", StringComparison.OrdinalIgnoreCase)
                ? CreateWaitingForAudioCallbacksStatus(selectedDevice)
                : statusMessage;
            _selectedDeviceFormat ??= await GetDeviceFormatAsync(selectedDevice);
            RefreshInputChannelOptionsWithoutSelectionEvents(_selectedDeviceFormat);
            UpdateAudioFormatRouteText();
        }
        catch (Exception ex)
        {
            if (_isClosing || operationVersion != _audioStreamOperationVersion)
            {
                return;
            }

            RegisterAudioStreamRestartFailure(ex);
            UpdateAudioFormatRouteText();
        }
        finally
        {
            if (operationVersion == _audioStreamOperationVersion)
            {
                _isRestartingAudioStream = false;
            }
        }
    }

    private void SpectrumAvailable(object? sender, SpectrumFrame frame)
    {
        AcceptSpectrumFrame(frame);
        var selectedMicFrame = CreateSelectedMicFrame(frame);

        if (_isMicDspSelectedMicSpectrumActive)
        {
            _selectedMicSpectrumGraphHost?.AcceptFrame(selectedMicFrame);
        }

        if (_isMicDspSpectrumWaterfallActive)
        {
            _waveform3DGraphHost?.AcceptFrame(selectedMicFrame);
        }

        if (_isPodcastSpectrumWaterfallActive)
        {
            _podcastSpectrumWaterfallGraphHost?.AcceptFrame(frame);
        }

        if (_isKaraokeSpectrumWaterfallActive)
        {
            _karaokeSpectrumWaterfallGraphHost?.AcceptFrame(frame);
        }

        if (_isMixingMicSpectrumGraphActive)
        {
            _mixingMicSpectrumGraphHost?.AcceptFrame(CreateMixerOutputFrame(frame));
        }

        if (_isMixingOutputWaveform3DActive)
        {
            _mixingOutputWaveform3DGraphHost?.AcceptFrame(CreateProgramOutputFrame(frame));
        }
    }

    private void AcceptSpectrumFrame(SpectrumFrame frame)
    {
        _latestFrame = frame;
    }

    private void ClearLiveSpectrumDisplay()
    {
        var emptyFrame = CreateEmptySpectrumFrame();
        _latestFrame = emptyFrame;
        _silentFrameCount = 0;

        ClearDirect3D12GraphHosts();

        var selectedMicFrame = CreateSelectedMicFrame(emptyFrame);
        _selectedMicSpectrumGraphHost?.AcceptFrame(selectedMicFrame);
        _waveform3DGraphHost?.AcceptFrame(selectedMicFrame);
        _podcastSpectrumWaterfallGraphHost?.AcceptFrame(emptyFrame);
        _karaokeSpectrumWaterfallGraphHost?.AcceptFrame(emptyFrame);
        _mixingMicSpectrumGraphHost?.AcceptFrame(CreateMixerOutputFrame(emptyFrame));
        _mixingOutputWaveform3DGraphHost?.AcceptFrame(CreateProgramOutputFrame(emptyFrame));
    }

    private void ClearDirect3D12GraphHosts()
    {
        _selectedMicSpectrumGraphHost?.ClearFrame();
        _waveform3DGraphHost?.ClearFrame();
        _podcastSpectrumWaterfallGraphHost?.ClearFrame();
        _karaokeSpectrumWaterfallGraphHost?.ClearFrame();
        _mixingMicSpectrumGraphHost?.ClearFrame();
        _mixingOutputWaveform3DGraphHost?.ClearFrame();
    }

    private SpectrumFrame CreateEmptySpectrumFrame()
    {
        return new SpectrumFrame(
            [],
            [],
            [],
            [],
            0d,
            0d,
            new VoiceProcessingTelemetry(),
            _selectedDeviceFormat?.SampleRate ?? 44100);
    }

    private static string CreateWaitingForAudioCallbacksStatus(AudioInputDevice selectedDevice)
    {
        return selectedDevice.IsAsio
            ? "ASIO input opened; waiting for driver audio callbacks."
            : "Listening; waiting for audio callbacks.";
    }

    private SpectrumFrame CreateSelectedMicFrame(SpectrumFrame frame)
    {
        var activeChannelNumber = _activeMicChannel?.ChannelNumber ?? 1;
        var selectedInputMagnitudes = ResolveActiveInputChannelMagnitudes(frame);
        return SpectrumFrameRouter.CreateSelectedMicFrame(
            frame,
            activeChannelNumber,
            _activeMicChannel?.SelectedDevice is not null,
            selectedInputMagnitudes);
    }

    private static SpectrumFrame CreateProgramOutputFrame(SpectrumFrame frame)
    {
        return SpectrumFrameRouter.CreateProgramOutputFrame(frame);
    }

    private SpectrumFrame CreateMixerOutputFrame(SpectrumFrame frame)
    {
        return SpectrumFrameRouter.CreateProgramOutputFrame(frame, ResolveRecordingSpectrumMagnitudes(frame));
    }

    private double[] ResolveRecordingSpectrumMagnitudes(SpectrumFrame frame)
    {
        if (_audioRecordingSource == ProcessedRecordingSource.ProgramMix)
        {
            return [];
        }

        var selectedChannelNumber = _activeMicChannel?.ChannelNumber ?? 1;
        var line = frame.MicrophoneLines.FirstOrDefault(candidate => candidate.ChannelNumber == selectedChannelNumber);
        return _audioRecordingSource == ProcessedRecordingSource.SelectedMicRawBackup
            ? line?.RawMagnitudes ?? []
            : line?.Magnitudes ?? [];
    }

    private double[] ResolveActiveInputChannelMagnitudes(SpectrumFrame frame)
    {
        var activeChannel = _activeMicChannel;
        if (activeChannel?.SelectedDevice is null
            || _selectedDevice is null
            || !AudioInputDevicesMatch(activeChannel.SelectedDevice, _selectedDevice))
        {
            return [];
        }

        return activeChannel.InputChannelMode switch
        {
            InputChannelMode.Input1Left when frame.Input1Magnitudes.Length > 0 => frame.Input1Magnitudes,
            InputChannelMode.Input2Right when frame.Input2Magnitudes.Length > 0 => frame.Input2Magnitudes,
            _ => []
        };
    }

    private void CompositionTargetRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_isRecordingSession && now - _lastRecordingTimerUpdateUtc >= TimeSpan.FromMilliseconds(250))
        {
            _lastRecordingTimerUpdateUtc = now;
            UpdateRecordingTimer();
        }

        if (now - _lastRecordingHealthUpdateUtc >= TimeSpan.FromMilliseconds(250))
        {
            _lastRecordingHealthUpdateUtc = now;
            UpdateRecordingHealthPanel();
        }

        if (_isStandaloneAudioRecording && now - _lastStandaloneRecordingSyncUtc >= TimeSpan.FromMilliseconds(100))
        {
            _lastStandaloneRecordingSyncUtc = now;
            SyncStandaloneAudioRecordingState();
        }

        if (_latestFrame is null)
        {
            return;
        }

        var selectedMicFrame = CreateSelectedMicFrame(_latestFrame);
        UpdateAudioStability(_latestFrame);
        UpdateInputCoach(selectedMicFrame.RawPeakLevel);
        UpdateSignalStatus(selectedMicFrame.PeakLevel);
        UpdateOutputSignalStatus(_latestFrame);
        UpdateMixerChannelMeters(_latestFrame);
        UpdateMixBusStatus(_latestFrame);

        var isMicDspTabSelected = IsMicDspTabSelected();
        var shouldRenderSpectrum = isMicDspTabSelected
            && SpectrumCanvas.IsVisible
            && SpectrumCanvas.ActualWidth > 1d
            && SpectrumCanvas.ActualHeight > 1d;
        var isMixingTabSelected = IsMixingTabSelected();
        var shouldRenderMixingSpectrum = isMixingTabSelected
            && MixingMicSpectrumCanvas.IsVisible
            && MixingMicSpectrumCanvas.ActualWidth > 1d
            && MixingMicSpectrumCanvas.ActualHeight > 1d;
        if (!shouldRenderSpectrum && !shouldRenderMixingSpectrum)
        {
            return;
        }

        if (!isMicDspTabSelected)
        {
            if (now - _lastAudioVisualRenderUtc < TimeSpan.FromMilliseconds(16))
            {
                return;
            }

            _lastAudioVisualRenderUtc = now;
        }

        if (shouldRenderSpectrum)
        {
            RenderSpectrum(selectedMicFrame);
        }

        if (shouldRenderMixingSpectrum)
        {
            RenderMixingMicSpectrum(CreateMixerOutputFrame(_latestFrame));
        }

    }

    private void UpdateRecordingTimer()
    {
        if (!_isRecordingSession)
        {
            return;
        }

        var timerText = FormatDuration(GetRecordingElapsed());
        if (!string.Equals(RecordingTimerText.Text, timerText, StringComparison.Ordinal))
        {
            RecordingTimerText.Text = timerText;
        }
    }

    private void UpdateRecordingHealthPanel()
    {
        if (RecordingHealthText is null || PreviewRecordParityText is null || VideoPipelineText is null)
        {
            return;
        }

        var pipeline = CameraStatusText.FormatActiveVideoPipeline(
            _cameraAvailable,
            _isCameraEnabled,
            _dx12Camera,
            _pendingVideoDenoiseEnabled,
            _pendingVideoColorSettings.HasVisibleAdjustments,
            CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice),
            _dx12Camera?.IsReady == true,
            _lastTextureNativeCameraError);
        var pumpWarningActive = !string.IsNullOrWhiteSpace(_cameraPumpWarningText)
            && DateTime.UtcNow <= _cameraPumpWarningExpiresUtc;
        var pipelineText = pumpWarningActive
            ? $"Pipeline: {pipeline}{Environment.NewLine}{_cameraPumpWarningText}"
            : $"Pipeline: {pipeline}";
        if (!pumpWarningActive)
        {
            _cameraPumpWarningText = null;
        }

        if (!string.Equals(VideoPipelineText.Text, pipelineText, StringComparison.Ordinal))
        {
            VideoPipelineText.Text = pipelineText;
        }
        var parity = CameraStatusText.FormatPreviewRecordParity(
            _isCameraEnabled,
            _dx12Camera,
            _pendingVideoDenoiseEnabled,
            _pendingVideoColorSettings.HasVisibleAdjustments,
            out var parityIsGood);
        var parityText = $"Preview/record: {parity}";
        if (!string.Equals(PreviewRecordParityText.Text, parityText, StringComparison.Ordinal))
        {
            PreviewRecordParityText.Text = parityText;
        }

        var parityForeground = parityIsGood ? _meterGoodBrush : _meterWarnBrush;
        if (!ReferenceEquals(PreviewRecordParityText.Foreground, parityForeground))
        {
            PreviewRecordParityText.Foreground = parityForeground;
        }

        if (!_isRecordingSession)
        {
            if (!string.Equals(RecordingHealthText.Text, "Idle", StringComparison.Ordinal))
            {
                RecordingHealthText.Text = "Idle";
            }

            if (!ReferenceEquals(RecordingHealthText.Foreground, _meterTextMutedBrush))
            {
                RecordingHealthText.Foreground = _meterTextMutedBrush;
            }

            return;
        }

        var (offered, written, skipped) = GetActiveRecordingCounters();
        var encoderDelay = Math.Max(0, offered - written);
        var audioPeak = _latestFrame?.PeakLevel ?? 0d;
        var diskStatus = GetRecordingDiskStatus();
        var recordingHealthText = $"Frames {written}/{Math.Max(offered, written)}  skipped {skipped}  enc lag {encoderDelay}  audio {audioPeak:P0}  disk {diskStatus}";
        if (!string.Equals(RecordingHealthText.Text, recordingHealthText, StringComparison.Ordinal))
        {
            RecordingHealthText.Text = recordingHealthText;
        }

        var recordingHealthForeground = skipped > 0 || encoderDelay > 3
            ? _meterWarnBrush
            : _meterGoodBrush;
        if (!ReferenceEquals(RecordingHealthText.Foreground, recordingHealthForeground))
        {
            RecordingHealthText.Foreground = recordingHealthForeground;
        }
    }

    private void CameraPumpWarningRaised(string warning)
    {
        _cameraPumpWarningText = warning;
        _cameraPumpWarningExpiresUtc = DateTime.UtcNow + CameraPumpWarningDisplayDuration;
        if (CameraPreviewStatusText is not null && _isCameraEnabled)
        {
            CameraPreviewStatusText.Text = warning;
        }
    }

    private (int Offered, int Written, int Skipped) GetActiveRecordingCounters()
    {
        if (_dx12Camera?.IsTextureNative == true && _dx12Camera.IsRecording)
        {
            var written = _dx12Camera.SamplesWritten;
            return (written, written, 0);
        }

        if (_textureNativeRecordingSession is not null)
        {
            var written = _textureNativeRecordingSession.SamplesWritten;
            return (written, written, 0);
        }

        return CameraSourceSelection.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice)
            ? (_directShowPreviewService.RecordingFramesOffered, _directShowPreviewService.RecordingFramesWritten, _directShowPreviewService.RecordingFramesSkipped)
            : (_cameraPreviewService.RecordingFramesOffered, _cameraPreviewService.RecordingFramesWritten, _cameraPreviewService.RecordingFramesSkipped);
    }

    private string GetRecordingDiskStatus()
    {
        var path = GetActiveRecordingVideoPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return "pending";
        }

        try
        {
            var folder = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return "folder missing";
            }

            return File.Exists(path)
                ? AudioRecordingCatalog.FormatFileSize(new FileInfo(path).Length)
                : "folder OK";
        }
        catch
        {
            return "unknown";
        }
    }

    private TimeSpan GetRecordingElapsed()
    {
        if (!_isRecordingSession)
        {
            return TimeSpan.Zero;
        }

        var now = _isRecordingPaused ? _recordingPausedAt : DateTime.UtcNow;
        return now - _recordingStartedAt - _recordingPausedDuration;
    }

    private void UpdateRecordingTransportControls()
    {
        if (StartRecordingButton is null)
        {
            return;
        }

        StartRecordingButton.IsEnabled = !_isRecordingSession;
        PauseRecordingButton.IsEnabled = _isRecordingSession;
        PauseRecordingButton.Content = _isRecordingPaused ? "Resume" : "Pause";
        StopRecordingButton.IsEnabled = _isRecordingSession;
    }

    private TimeSpan GetStandaloneAudioRecordingElapsed()
    {
        if (!_isStandaloneAudioRecording)
        {
            return TimeSpan.Zero;
        }

        var now = _isStandaloneAudioRecordingPaused ? _audioRecordingPausedAt : DateTime.UtcNow;
        return now - _audioRecordingStartedAt - _audioRecordingPausedDuration;
    }

    private void UpdateStandaloneAudioRecordingTransportControls()
    {
        if (AudioRecordButton is null)
        {
            return;
        }

        var otherProcessedRecordingActive = _spectrumService.IsProcessedAudioRecording && !_isStandaloneAudioRecording;
        AudioRecordButton.IsEnabled = !_isStandaloneAudioRecording && !otherProcessedRecordingActive;
        AudioPauseButton.IsEnabled = _isStandaloneAudioRecording;
        AudioPauseButton.Content = _isStandaloneAudioRecordingPaused ? "Resume" : "Pause";
        AudioStopButton.IsEnabled = _isStandaloneAudioRecording;
        AudioPlayFileButton.IsEnabled = !_isStandaloneAudioRecording && !otherProcessedRecordingActive;
        AudioPlayFileButton.Content = _audioPlaybackOutput is null ? "Play File" : "Stop Play";
        AudioOpenLocationButton.IsEnabled = !_isStandaloneAudioRecording && !otherProcessedRecordingActive;
    }

    private void SyncStandaloneAudioRecordingState()
    {
        if (!_isStandaloneAudioRecording || _spectrumService.IsProcessedAudioRecording)
        {
            return;
        }

        var elapsed = GetStandaloneAudioRecordingElapsed();
        var savedPath = _activeAudioRecordingPath;
        _activeAudioRecordingPath = null;
        _audioRecordingStartedAt = default;
        _audioRecordingPausedAt = default;
        _audioRecordingPausedDuration = TimeSpan.Zero;
        _isStandaloneAudioRecording = false;
        _isStandaloneAudioRecordingPaused = false;

        AudioRecordingStatusText.Text = string.IsNullOrWhiteSpace(savedPath)
            ? "Audio recording stopped."
            : $"Saved {System.IO.Path.GetFileName(savedPath)} ({FormatDuration(elapsed)}).";
        _lastAudioRecordingPath = savedPath;
        RefreshAudioRecordingFiles(savedPath);
        PersistAppState();
        UpdateStandaloneAudioRecordingTransportControls();
        UpdateKaraokeTransportControls();
    }

    private void StartAudioPlayback(string path)
    {
        try
        {
            StopAudioPlayback();

            var reader = new AudioFileReader(path);
            var output = CreateSelectedPlaybackOutput(reader.ToWaveProvider(), desiredLatency: 90);
            output.PlaybackStopped += AudioPlaybackStopped;

            _audioPlaybackReader = reader;
            _audioPlaybackOutput = output;
            _audioPlaybackPath = path;
            _lastAudioRecordingPath = path;
            _isStoppingAudioPlayback = false;
            output.Play();
            PersistAppState();

            AudioRecordingStatusText.Text = $"Playing {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StopAudioPlayback();
            AudioRecordingStatusText.Text = $"Playback failed: {ex.Message}";
        }

        UpdateStandaloneAudioRecordingTransportControls();
    }

    private IWavePlayer CreateSelectedPlaybackOutput(IWaveProvider provider, int desiredLatency)
    {
        if (_selectedOutputDevice?.IsAsio == true
            && MicrophoneSpectrumService.TryGetAsioDriverName(_selectedOutputDevice.EndpointId, out var asioDriverName))
        {
            var asioOutput = new AsioOutputPlayer(asioDriverName);
            asioOutput.Init(provider);
            return asioOutput;
        }

        var output = new WaveOutEvent
        {
            DeviceNumber = _selectedOutputDevice?.DeviceNumber ?? -1,
            DesiredLatency = desiredLatency
        };
        output.Init(provider);
        return output;
    }

    private void AudioPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var path = _audioPlaybackPath;
            var wasStoppedByUser = _isStoppingAudioPlayback;
            StopAudioPlayback();
            if (e.Exception is not null)
            {
                AudioRecordingStatusText.Text = $"Playback failed: {e.Exception.Message}";
            }
            else if (!wasStoppedByUser && !string.IsNullOrWhiteSpace(path))
            {
                AudioRecordingStatusText.Text = $"Finished {System.IO.Path.GetFileName(path)}.";
            }

            UpdateStandaloneAudioRecordingTransportControls();
        }, DispatcherPriority.Background);
    }

    private void StopAudioPlayback()
    {
        var output = _audioPlaybackOutput;
        var reader = _audioPlaybackReader;
        _audioPlaybackOutput = null;
        _audioPlaybackReader = null;
        _audioPlaybackPath = null;

        if (output is not null)
        {
            _isStoppingAudioPlayback = true;
            try
            {
                output.PlaybackStopped -= AudioPlaybackStopped;
                output.Stop();
            }
            catch
            {
            }

            try
            {
                output.Dispose();
            }
            catch
            {
            }
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
        }

        _isStoppingAudioPlayback = false;
    }

    private void LoadKaraokeTrackClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add karaoke backing tracks",
            Filter = KaraokeTrackOpenFileFilter,
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (AddKaraokeTracksToQueue(dialog.FileNames, selectFirstAdded: true) > 0)
        {
            PersistAppState();
        }
    }

    private bool SetKaraokeTrack(string path, bool updateQueueSelection = false)
    {
        try
        {
            var track = _karaokeQueue.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
                ?? CreateKaraokeTrackItem(path);
            var trackPath = track.Path;
            var switchingTracks = !string.Equals(_karaokeTrackPath, trackPath, StringComparison.OrdinalIgnoreCase);
            if (switchingTracks)
            {
                SaveCurrentKaraokeLyricsForCurrentTrack();
            }

            StopKaraokePlayback(clearTrack: false);
            _karaokeTrackPath = trackPath;
            _karaokeTrackDuration = track.Duration;
            if (updateQueueSelection)
            {
                SetSelectedKaraokeQueueTrack(trackPath);
            }

            var lyricsLoaded = switchingTracks && LoadKaraokeLyricsForTrack(trackPath);
            if (!switchingTracks && string.IsNullOrWhiteSpace(KaraokeLyricsTextBox?.Text))
            {
                lyricsLoaded = LoadKaraokeLyricsForTrack(trackPath);
            }

            UpdateKaraokeTrackUi();
            KaraokePlaybackStatusText.Text = $"Loaded {track.Name}.";
            if (!lyricsLoaded)
            {
                KaraokeStatusText.Text = "Backing track ready. Use Load Lyrics or Detect Lyrics when you want lyric timing.";
                if (string.IsNullOrWhiteSpace(KaraokeLyricsTextBox?.Text))
                {
                    UpdateKaraokeLyricsDisplay();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _karaokeTrackPath = null;
            _karaokeTrackDuration = TimeSpan.Zero;
            UpdateKaraokeTrackUi();
            KaraokePlaybackStatusText.Text = $"Track load failed: {ex.Message}";
            KaraokeStatusText.Text = "Backing track could not be loaded.";
            return false;
        }
    }

    private void BrowseKaraokeFolderClicked(object sender, RoutedEventArgs e)
    {
        var initialFolder = Directory.Exists(_karaokeBrowserFolder)
            ? _karaokeBrowserFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var dialog = new OpenFileDialog
        {
            Title = "Choose root song folder",
            InitialDirectory = initialFolder,
            Filter = KaraokeTrackOpenFileFilter,
            FileName = "Select this folder",
            CheckFileExists = false,
            ValidateNames = false,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selectedFolder = Directory.Exists(dialog.FileName)
            ? dialog.FileName
            : System.IO.Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
        {
            return;
        }

        _karaokeBrowserFolder = selectedFolder;
        RefreshKaraokeBrowserFiles();
        PersistAppState();
    }

    private void RefreshKaraokeBrowserFiles()
    {
        if (KaraokeBrowserFolderText is not null)
        {
            KaraokeBrowserFolderText.Text = _karaokeBrowserFolder ?? string.Empty;
        }

        _karaokeBrowserFiles.Clear();
        if (string.IsNullOrWhiteSpace(_karaokeBrowserFolder) || !Directory.Exists(_karaokeBrowserFolder))
        {
            if (KaraokeStatusText is not null)
            {
                KaraokeStatusText.Text = "Choose a song folder to browse backing tracks.";
            }

            return;
        }

        foreach (var path in EnumerateKaraokeTrackFiles(_karaokeBrowserFolder)
                     .OrderBy(path => System.IO.Path.GetRelativePath(_karaokeBrowserFolder, path), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var track = CreateKaraokeTrackItem(path);
                var relativeFolder = System.IO.Path.GetDirectoryName(System.IO.Path.GetRelativePath(_karaokeBrowserFolder, path));
                var location = string.IsNullOrWhiteSpace(relativeFolder)
                    ? string.Empty
                    : $"  {relativeFolder}";
                _karaokeBrowserFiles.Add(new KaraokeBrowserFileItem(
                    path,
                    track.Name,
                    $"{track.Artist}  {track.DurationText}{location}"));
            }
            catch
            {
            }
        }

        KaraokeStatusText.Text = _karaokeBrowserFiles.Count == 0
            ? "No supported backing tracks found under that folder."
            : $"Found {_karaokeBrowserFiles.Count} backing tracks under that folder.";
    }

    private static IEnumerable<string> EnumerateKaraokeTrackFiles(string rootFolder)
    {
        var pending = new Stack<string>();
        var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string rootFullPath;
        try
        {
            rootFullPath = NormalizeKaraokeFolderPath(rootFolder);
        }
        catch
        {
            yield break;
        }

        visitedFolders.Add(rootFullPath);
        pending.Push(rootFullPath);
        var yieldedTrackCount = 0;
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            DirectoryInfo folderInfo;
            try
            {
                folderInfo = new DirectoryInfo(folder);
                if (!folderInfo.Exists
                    || (!string.Equals(NormalizeKaraokeFolderPath(folderInfo.FullName), rootFullPath, StringComparison.OrdinalIgnoreCase)
                        && folderInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            foreach (var file in EnumerateKaraokeFilesInFolder(folder))
            {
                string fullPath;
                try
                {
                    fullPath = System.IO.Path.GetFullPath(file);
                }
                catch
                {
                    continue;
                }

                if (yieldedFiles.Add(fullPath))
                {
                    if (yieldedTrackCount >= MaximumKaraokeBrowserTracks)
                    {
                        yield break;
                    }

                    yieldedTrackCount++;
                    yield return fullPath;
                }
            }

            foreach (var childFolder in EnumerateKaraokeChildFolders(folder)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (visitedFolders.Count >= MaximumKaraokeBrowserFolders)
                {
                    break;
                }

                if (TryNormalizeKaraokeChildFolder(childFolder, out var normalizedChild)
                    && visitedFolders.Add(normalizedChild))
                {
                    pending.Push(normalizedChild);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateKaraokeFilesInFolder(string folder)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files.Where(KaraokePlaybackPolicy.IsSupportedTrackFile))
        {
            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateKaraokeChildFolders(string folder)
    {
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(folder);
        }
        catch
        {
            yield break;
        }

        foreach (var childFolder in folders)
        {
            yield return childFolder;
        }
    }

    private static bool TryNormalizeKaraokeChildFolder(string folder, out string normalizedFolder)
    {
        normalizedFolder = string.Empty;
        try
        {
            var info = new DirectoryInfo(folder);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            normalizedFolder = NormalizeKaraokeFolderPath(info.FullName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetSelectedKaraokeQueueTrack(string path)
    {
        if (KaraokeQueueDataGrid is null)
        {
            return;
        }

        var item = _karaokeQueue.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
        _isUpdatingKaraokeQueueSelection = true;
        try
        {
            KaraokeQueueDataGrid.SelectedItem = item;
            if (item is not null)
            {
                KaraokeQueueDataGrid.ScrollIntoView(item);
            }
        }
        finally
        {
            _isUpdatingKaraokeQueueSelection = false;
        }
    }

    private void SelectQueuedKaraokeTrackWithoutLoading(KaraokeTrackItem track)
    {
        if (KaraokeQueueDataGrid is null)
        {
            return;
        }

        _isUpdatingKaraokeQueueSelection = true;
        try
        {
            KaraokeQueueDataGrid.SelectedItem = track;
            KaraokeQueueDataGrid.ScrollIntoView(track);
        }
        finally
        {
            _isUpdatingKaraokeQueueSelection = false;
        }
    }

    private static string NormalizeKaraokeFolderPath(string folder)
    {
        return System.IO.Path.GetFullPath(folder)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private void AddSelectedKaraokeBrowserFilesClicked(object sender, RoutedEventArgs e)
    {
        var selectedPaths = KaraokeBrowserFilesListBox.SelectedItems
            .OfType<KaraokeBrowserFileItem>()
            .Select(item => item.Path)
            .ToList();
        if (selectedPaths.Count == 0)
        {
            KaraokeStatusText.Text = "Select one or more songs to queue.";
            return;
        }

        if (AddKaraokeTracksToQueue(selectedPaths, selectFirstAdded: true) > 0)
        {
            PersistAppState();
        }
    }

    private void AddAllKaraokeBrowserFilesClicked(object sender, RoutedEventArgs e)
    {
        var paths = _karaokeBrowserFiles
            .Select(item => item.Path)
            .ToList();
        if (paths.Count == 0)
        {
            KaraokeStatusText.Text = "No songs are visible in the disk browser yet.";
            return;
        }

        if (AddKaraokeTracksToQueue(paths, selectFirstAdded: true) > 0)
        {
            PersistAppState();
        }
        else
        {
            KaraokeStatusText.Text = "All visible songs are already in the queue.";
        }
    }

    private void KaraokeBrowserFileDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (KaraokeBrowserFilesListBox.SelectedItem is not KaraokeBrowserFileItem item)
        {
            return;
        }

        if (AddKaraokeTracksToQueue([item.Path], selectFirstAdded: true) > 0)
        {
            PersistAppState();
        }
    }

    private int AddKaraokeTracksToQueue(IEnumerable<string> paths, bool selectFirstAdded)
    {
        var added = new List<KaraokeTrackItem>();
        _isUpdatingKaraokeQueueSelection = true;
        try
        {
            foreach (var path in paths
                         .Where(KaraokePlaybackPolicy.IsSupportedTrackFile)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_karaokeQueue.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    var track = CreateKaraokeTrackItem(path);
                    _karaokeQueue.Add(track);
                    added.Add(track);
                }
                catch (Exception ex)
                {
                    AppStateStore.LogDiagnostic("karaoke-track-add-failed", ex);
                    KaraokeStatusText.Text = $"Could not add {System.IO.Path.GetFileName(path)}: {ex.Message}";
                }
            }
        }
        finally
        {
            _isUpdatingKaraokeQueueSelection = false;
        }

        if (added.Count > 0)
        {
            _karaokeQueueExplicitlyCleared = false;
            KaraokeStatusText.Text = added.Count == 1
                ? $"Queued {added[0].Name}."
                : $"Queued {added.Count} backing tracks.";
            if (selectFirstAdded)
            {
                if (!_isKaraokeTrackPlaying && !_isKaraokeVocalRecording)
                {
                    SetKaraokeTrack(added[0].Path, updateQueueSelection: true);
                }
                else if (!string.IsNullOrWhiteSpace(_karaokeTrackPath))
                {
                    SetSelectedKaraokeQueueTrack(_karaokeTrackPath);
                }
            }
        }

        UpdateKaraokeQueueControls();
        UpdateKaraokeTransportControls();
        return added.Count;
    }

    private void KaraokeQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingKaraokeQueueSelection)
        {
            UpdateKaraokeQueueControls();
            return;
        }

        if (KaraokeQueueDataGrid.SelectedItem is not KaraokeTrackItem item)
        {
            UpdateKaraokeQueueControls();
            return;
        }

        if (!string.Equals(_karaokeTrackPath, item.Path, StringComparison.OrdinalIgnoreCase))
        {
            SetKaraokeTrack(item.Path);
            PersistAppState();
        }

        UpdateKaraokeQueueControls();
    }

    private void KaraokeQueueMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (KaraokeQueueDataGrid.SelectedItem is KaraokeTrackItem item)
        {
            if (SetKaraokeTrack(item.Path))
            {
                StartKaraokePlayback();
            }
        }
    }

    private void RemoveSelectedKaraokeTrackClicked(object sender, RoutedEventArgs e)
    {
        if (KaraokeQueueDataGrid.SelectedItem is not KaraokeTrackItem item)
        {
            return;
        }

        var removedCurrentTrack = string.Equals(_karaokeTrackPath, item.Path, StringComparison.OrdinalIgnoreCase);
        var index = _karaokeQueue.IndexOf(item);
        _karaokeQueue.Remove(item);
        if (_karaokeQueue.Count == 0)
        {
            _karaokeQueueExplicitlyCleared = true;
        }

        if (removedCurrentTrack)
        {
            StopKaraokePlayback(clearTrack: true);
            var next = _karaokeQueue.Count == 0 ? null : _karaokeQueue[Math.Clamp(index, 0, _karaokeQueue.Count - 1)];
            if (next is not null)
            {
                SelectQueuedKaraokeTrackWithoutLoading(next);
                SetKaraokeTrack(next.Path);
            }
            else
            {
                UpdateKaraokeTrackUi();
            }
        }

        KaraokeStatusText.Text = $"Removed {item.Name} from the queue.";
        UpdateKaraokeQueueControls();
        UpdateKaraokeTransportControls();
        PersistAppState();
    }

    private void ClearKaraokeQueueClicked(object sender, RoutedEventArgs e)
    {
        StopKaraokePlayback(clearTrack: true);
        _karaokeQueue.Clear();
        _karaokeQueueExplicitlyCleared = true;
        KaraokeQueueDataGrid.SelectedItem = null;
        KaraokeStatusText.Text = "Karaoke queue cleared.";
        UpdateKaraokeTrackUi();
        UpdateKaraokeQueueControls();
        UpdateKaraokeTransportControls();
        PersistAppState();
    }

    private bool TryAdvanceKaraokeQueue()
    {
        if (_karaokeQueue.Count == 0)
        {
            return false;
        }

        var index = _karaokeQueue.ToList().FindIndex(item => string.Equals(item.Path, _karaokeTrackPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= _karaokeQueue.Count)
        {
            return false;
        }

        var next = _karaokeQueue[index + 1];
        SelectQueuedKaraokeTrackWithoutLoading(next);
        SetKaraokeTrack(next.Path);
        return true;
    }

    private void UpdateKaraokeQueueControls()
    {
        if (KaraokeRemoveTrackButton is null)
        {
            return;
        }

        KaraokeRemoveTrackButton.IsEnabled = KaraokeQueueDataGrid.SelectedItem is KaraokeTrackItem;
    }

    private KaraokeTrackItem CreateKaraokeTrackItem(string path)
    {
        var duration = KaraokeTrackAudioReader.TryReadDuration(path, out var decodedDuration)
            ? decodedDuration
            : TimeSpan.Zero;
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var artist = InferKaraokeArtistFromPath(path);
        var name = fileName;
        var separatorIndex = fileName.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex + 3 < fileName.Length)
        {
            artist = fileName[..separatorIndex].Trim();
            name = fileName[(separatorIndex + 3)..].Trim();
        }

        return new KaraokeTrackItem(
            path,
            string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(path) : name,
            string.IsNullOrWhiteSpace(artist) ? "Unknown" : artist,
            duration,
            duration > TimeSpan.Zero ? FormatDuration(duration) : "Unknown");
    }

    private static string InferKaraokeArtistFromPath(string path)
    {
        try
        {
            var trackFolder = Directory.GetParent(path);
            var artistFolder = trackFolder?.Parent;
            var artist = artistFolder?.Name;
            if (string.IsNullOrWhiteSpace(artist)
                || artist.Equals("Music", StringComparison.OrdinalIgnoreCase)
                || artist.Equals("iTunes Media", StringComparison.OrdinalIgnoreCase)
                || artist.Equals("Compilations", StringComparison.OrdinalIgnoreCase))
            {
                return "Unknown";
            }

            return artist.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    private void KaraokePlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_isKaraokeTrackPlaying)
        {
            PauseKaraokePlayback();
            if (_isKaraokeVocalRecording && !_isKaraokeVocalRecordingPaused)
            {
                PauseKaraokeVocalRecording("Karaoke recording paused with backing track.");
            }

            return;
        }

        if (StartKaraokePlayback() && _isKaraokeVocalRecording && _isKaraokeVocalRecordingPaused)
        {
            ResumeKaraokeVocalRecording("Karaoke recording resumed with backing track.");
        }
    }

    private void KaraokeStopClicked(object sender, RoutedEventArgs e)
    {
        var stoppedRecording = _isKaraokeVocalRecording;
        StopKaraokePlayback(clearTrack: false);
        if (stoppedRecording)
        {
            StopKaraokeVocalRecording();
        }

        ResetKaraokePlaybackProgress();
        KaraokePlaybackStatusText.Text = string.IsNullOrWhiteSpace(_karaokeTrackPath)
            ? "Load a backing track."
            : stoppedRecording
                ? "Backing track stopped; karaoke recording saved."
                : "Backing track stopped.";
        UpdateKaraokeTransportControls();
    }

    private bool StartKaraokePlayback()
    {
        if (string.IsNullOrWhiteSpace(_karaokeTrackPath) || !File.Exists(_karaokeTrackPath))
        {
            KaraokePlaybackStatusText.Text = "Choose a backing track first.";
            UpdateKaraokeTransportControls();
            return false;
        }

        try
        {
            if (_karaokeTrackMediaPlayer is not null)
            {
                if (KaraokePlaybackPolicy.ShouldRestartFromEnd(GetKaraokeMediaPosition(), _karaokeTrackDuration))
                {
                    SeekKaraokePlaybackTo(TimeSpan.Zero);
                }

                _isStoppingKaraokePlayback = false;
                _karaokeTrackMediaPlayer.Play();
                _isKaraokeTrackPlaying = true;
                StartKaraokePlaybackClock(GetKaraokeMediaPosition());
                _karaokePlaybackPositionTimer.Start();
                KaraokePlaybackStatusText.Text = $"Playing {System.IO.Path.GetFileName(_karaokeTrackPath)} via Windows media fallback.";
                return true;
            }

            if (_karaokeTrackOutput is null || _karaokeTrackReader is null)
            {
                if (!KaraokeTrackAudioReader.CanUseSampleReader(_karaokeTrackPath))
                {
                    return StartKaraokeMediaFallbackPlayback(_karaokeTrackPath);
                }

                KaraokeTrackAudioReader reader;
                try
                {
                    reader = KaraokeTrackAudioReader.Open(_karaokeTrackPath);
                }
                catch (Exception ex) when (KaraokePlaybackPolicy.ShouldTryMediaFallbackAfterSampleReaderFailure(_karaokeTrackPath, ex))
                {
                    AppStateStore.LogDiagnostic("karaoke-sample-reader-fallback", ex);
                    return StartKaraokeMediaFallbackPlayback(_karaokeTrackPath);
                }

                var rateProvider = new KaraokeRateSampleProvider(reader, GetKaraokeTempoRatio());
                var pitchProvider = new SmbPitchShiftingSampleProvider(rateProvider)
                {
                    PitchFactor = (float)GetKaraokePitchFactor()
                };
                var vocalReductionProvider = new KaraokeVocalReductionSampleProvider(pitchProvider, _karaokeVocalReductionEnabled);
                var output = CreateSelectedPlaybackOutput(vocalReductionProvider.ToWaveProvider(), desiredLatency: 90);
                output.PlaybackStopped += KaraokePlaybackStopped;
                _karaokeTrackReader = reader;
                _karaokeTrackRateProvider = rateProvider;
                _karaokeTrackPitchProvider = pitchProvider;
                _karaokeTrackVocalReductionProvider = vocalReductionProvider;
                _karaokeTrackOutput = output;
                _karaokeTrackDuration = reader.TotalTime;
                SeekKaraokePlaybackTo(KaraokePlaybackPolicy.ResolvePlaybackStartPosition(KaraokeSeekSlider.Value, _karaokeTrackDuration));
            }
            else
            {
                ApplyKaraokeTimePitchSettings();
                _karaokeTrackVocalReductionProvider?.SetEnabled(_karaokeVocalReductionEnabled);
            }

            if (KaraokePlaybackPolicy.ShouldRestartFromEnd(GetKaraokeMediaPosition(), _karaokeTrackDuration))
            {
                SeekKaraokePlaybackTo(TimeSpan.Zero);
            }

            _isStoppingKaraokePlayback = false;
            _karaokeTrackOutput.Play();
            _isKaraokeTrackPlaying = true;
            StartKaraokePlaybackClock(GetKaraokeMediaPosition());
            _karaokePlaybackPositionTimer.Start();
            KaraokePlaybackStatusText.Text = $"Playing {System.IO.Path.GetFileName(_karaokeTrackPath)}.";
            return true;
        }
        catch (Exception ex)
        {
            StopKaraokePlayback(clearTrack: false);
            KaraokePlaybackStatusText.Text = $"Playback failed: {ex.Message}";
            return false;
        }
        finally
        {
            UpdateKaraokeTransportControls();
        }
    }

    private void PauseKaraokePlayback()
    {
        if (_karaokeTrackMediaPlayer is not null)
        {
            _karaokeTrackMediaPlayer.Pause();
            _isKaraokeTrackPlaying = false;
            PauseKaraokePlaybackClock();
            _karaokePlaybackPositionTimer.Stop();
            UpdateKaraokePlaybackProgress();
            KaraokePlaybackStatusText.Text = "Backing track paused.";
            UpdateKaraokeTransportControls();
            return;
        }

        if (_karaokeTrackOutput is null)
        {
            return;
        }

        _karaokeTrackOutput.Pause();
        _isKaraokeTrackPlaying = false;
        PauseKaraokePlaybackClock();
        _karaokePlaybackPositionTimer.Stop();
        UpdateKaraokePlaybackProgress();
        KaraokePlaybackStatusText.Text = "Backing track paused.";
        UpdateKaraokeTransportControls();
    }

    private void StopKaraokePlayback(bool clearTrack)
    {
        _karaokePlaybackPositionTimer.Stop();
        _isKaraokeTrackPlaying = false;
        StopKaraokePlaybackClock();
        _isScrubbingKaraokePlayback = false;
        if (KaraokeSeekSlider is not null && KaraokeSeekSlider.IsMouseCaptured)
        {
            KaraokeSeekSlider.ReleaseMouseCapture();
        }

        var output = _karaokeTrackOutput;
        var reader = _karaokeTrackReader;
        var mediaPlayer = _karaokeTrackMediaPlayer;
        _karaokeTrackOutput = null;
        _karaokeTrackReader = null;
        _karaokeTrackMediaPlayer = null;
        _karaokeTrackRateProvider = null;
        _karaokeTrackPitchProvider = null;
        _karaokeTrackVocalReductionProvider = null;
        if (output is not null)
        {
            _isStoppingKaraokePlayback = true;
            try
            {
                output.PlaybackStopped -= KaraokePlaybackStopped;
                output.Stop();
            }
            catch
            {
            }

            try
            {
                output.Dispose();
            }
            catch
            {
            }
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
        }

        if (mediaPlayer is not null)
        {
            _isStoppingKaraokePlayback = true;
            try
            {
                mediaPlayer.MediaOpened -= KaraokeMediaFallbackOpened;
                mediaPlayer.MediaEnded -= KaraokeMediaFallbackEnded;
                mediaPlayer.MediaFailed -= KaraokeMediaFallbackFailed;
                mediaPlayer.Stop();
                mediaPlayer.Close();
            }
            catch
            {
            }
        }

        _isStoppingKaraokePlayback = false;
        if (clearTrack)
        {
            _karaokeTrackPath = null;
            _karaokeTrackDuration = TimeSpan.Zero;
        }
    }

    private bool StartKaraokeMediaFallbackPlayback(string path, TimeSpan? startPosition = null)
    {
        var clampedPosition = ClampKaraokePlaybackPosition(startPosition ?? TimeSpan.Zero);
        var player = new MediaPlayer
        {
            Volume = 1d
        };
        player.MediaOpened += KaraokeMediaFallbackOpened;
        player.MediaEnded += KaraokeMediaFallbackEnded;
        player.MediaFailed += KaraokeMediaFallbackFailed;
        player.Open(new Uri(path, UriKind.Absolute));
        if (clampedPosition > TimeSpan.Zero)
        {
            player.Position = clampedPosition;
        }

        _karaokeTrackMediaPlayer = player;
        _isStoppingKaraokePlayback = false;
        _isKaraokeTrackPlaying = true;
        StartKaraokePlaybackClock(clampedPosition);
        _karaokePlaybackPositionTimer.Start();
        player.Play();
        KaraokePlaybackStatusText.Text = $"Playing {System.IO.Path.GetFileName(path)} via Windows media fallback. WAV/MP3 enables key, tempo, and vocal reduction.";
        return true;
    }

    private void KaraokeMediaFallbackOpened(object? sender, EventArgs e)
    {
        if (_karaokeTrackMediaPlayer?.NaturalDuration.HasTimeSpan == true)
        {
            _karaokeTrackDuration = _karaokeTrackMediaPlayer.NaturalDuration.TimeSpan;
            UpdateKaraokePlaybackProgress();
        }
    }

    private void KaraokeMediaFallbackEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            HandleKaraokePlaybackCompleted(exception: null, wasStoppedByUser: _isStoppingKaraokePlayback);
        }, DispatcherPriority.Background);
    }

    private void KaraokeMediaFallbackFailed(object? sender, ExceptionEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            HandleKaraokePlaybackCompleted(e.ErrorException, wasStoppedByUser: _isStoppingKaraokePlayback);
        }, DispatcherPriority.Background);
    }

    private void KaraokePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            HandleKaraokePlaybackCompleted(e.Exception, wasStoppedByUser: _isStoppingKaraokePlayback);
        }, DispatcherPriority.Background);
    }

    private void HandleKaraokePlaybackCompleted(Exception? exception, bool wasStoppedByUser)
    {
        var trackPath = _karaokeTrackPath;
        var trackName = string.IsNullOrWhiteSpace(trackPath)
            ? "track"
            : System.IO.Path.GetFileName(trackPath);
        var stoppedSampleReaderPlayback = _karaokeTrackOutput is not null && _karaokeTrackMediaPlayer is null;
        var stoppedPosition = KaraokePlaybackPolicy.ResolvePlaybackStartPosition(GetKaraokeMediaPosition().TotalSeconds, _karaokeTrackDuration);
        StopKaraokePlayback(clearTrack: false);
        if (exception is not null)
        {
            if (!wasStoppedByUser
                && stoppedSampleReaderPlayback
                && KaraokePlaybackPolicy.ShouldTryMediaFallbackAfterSampleReaderFailure(trackPath, exception)
                && !string.IsNullOrWhiteSpace(trackPath)
                && File.Exists(trackPath))
            {
                AppStateStore.LogDiagnostic("karaoke-sample-reader-playback-fallback", exception);
                StartKaraokeMediaFallbackPlayback(trackPath, stoppedPosition);
                return;
            }

            KaraokePlaybackStatusText.Text = $"Playback failed: {exception.Message}";
        }
        else if (!wasStoppedByUser)
        {
            SeekKaraokePlaybackTo(_karaokeTrackDuration);
            if (_isKaraokeVocalRecording)
            {
                StopKaraokeVocalRecording();
                KaraokePlaybackStatusText.Text = $"Finished {trackName}; karaoke recording saved.";
            }
            else if (TryAdvanceKaraokeQueue())
            {
                KaraokePlaybackStatusText.Text = $"Finished {trackName}. Starting next track.";
                StartKaraokePlayback();
            }
            else
            {
                KaraokePlaybackStatusText.Text = $"Finished {trackName}.";
            }
        }

        UpdateKaraokeTransportControls();
    }

    private void KaraokePlaybackPositionTimerTick(object? sender, EventArgs e)
    {
        if (!_isScrubbingKaraokePlayback)
        {
            UpdateKaraokePlaybackProgress();
        }
    }

    private void UpdateKaraokePlaybackProgress()
    {
        if (KaraokeSeekSlider is null)
        {
            return;
        }

        var position = GetKaraokePlaybackClockPosition();
        var duration = _karaokeTrackDuration;
        var maxSeconds = Math.Max(1d, duration.TotalSeconds);
        var positionSeconds = Math.Clamp(position.TotalSeconds, 0d, maxSeconds);

        var now = DateTime.UtcNow;
        var shouldUpdateTransportUi = !_isKaraokeTrackPlaying
            || now - _lastKaraokeTransportUiUpdateUtc >= TimeSpan.FromMilliseconds(180);
        if (shouldUpdateTransportUi)
        {
            _lastKaraokeTransportUiUpdateUtc = now;
            _isUpdatingKaraokePlaybackPosition = true;
            try
            {
                if (Math.Abs(KaraokeSeekSlider.Minimum) > 0.001d)
                {
                    KaraokeSeekSlider.Minimum = 0d;
                }

                if (Math.Abs(KaraokeSeekSlider.Maximum - maxSeconds) > 0.001d)
                {
                    KaraokeSeekSlider.Maximum = maxSeconds;
                }

                if (Math.Abs(KaraokeSeekSlider.Value - positionSeconds) > 0.03d)
                {
                    KaraokeSeekSlider.Value = positionSeconds;
                }
            }
            finally
            {
                _isUpdatingKaraokePlaybackPosition = false;
            }

            var positionText = FormatDuration(TimeSpan.FromSeconds(positionSeconds));
            if (!string.Equals(KaraokePositionText.Text, positionText, StringComparison.Ordinal))
            {
                KaraokePositionText.Text = positionText;
            }

            var durationText = duration > TimeSpan.Zero ? FormatDuration(duration) : "00:00:00";
            if (!string.Equals(KaraokeDurationText.Text, durationText, StringComparison.Ordinal))
            {
                KaraokeDurationText.Text = durationText;
            }
        }

        UpdateActiveKaraokeLyric(TimeSpan.FromSeconds(positionSeconds));
    }

    private void ResetKaraokePlaybackProgress()
    {
        _isUpdatingKaraokePlaybackPosition = true;
        try
        {
            KaraokeSeekSlider.Minimum = 0d;
            KaraokeSeekSlider.Maximum = Math.Max(1d, _karaokeTrackDuration.TotalSeconds);
            KaraokeSeekSlider.Value = 0d;
        }
        finally
        {
            _isUpdatingKaraokePlaybackPosition = false;
        }

        KaraokePositionText.Text = "00:00:00";
        KaraokeDurationText.Text = _karaokeTrackDuration > TimeSpan.Zero ? FormatDuration(_karaokeTrackDuration) : "00:00:00";
        UpdateActiveKaraokeLyric(TimeSpan.Zero);
    }

    private void SeekKaraokePlaybackFromPoint(Point point)
    {
        if (_karaokeTrackDuration <= TimeSpan.Zero || KaraokeSeekSlider.ActualWidth <= 0d)
        {
            return;
        }

        var ratio = Math.Clamp(point.X / KaraokeSeekSlider.ActualWidth, 0d, 1d);
        SeekKaraokePlaybackTo(TimeSpan.FromSeconds(_karaokeTrackDuration.TotalSeconds * ratio));
    }

    private void SeekKaraokePlaybackTo(TimeSpan position)
    {
        var duration = _karaokeTrackDuration;
        var maxSeconds = Math.Max(0d, duration.TotalSeconds);
        var clamped = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0d, maxSeconds));
        if (_karaokeTrackReader is not null)
        {
            _karaokeTrackReader.CurrentTime = clamped;
            _karaokeTrackRateProvider?.Reset();
        }

        if (_karaokeTrackMediaPlayer is not null)
        {
            _karaokeTrackMediaPlayer.Position = clamped;
        }

        SetKaraokePlaybackClockPosition(clamped);
        _isUpdatingKaraokePlaybackPosition = true;
        try
        {
            KaraokeSeekSlider.Maximum = Math.Max(1d, maxSeconds);
            KaraokeSeekSlider.Value = clamped.TotalSeconds;
        }
        finally
        {
            _isUpdatingKaraokePlaybackPosition = false;
        }

        KaraokePositionText.Text = FormatDuration(clamped);
        KaraokeDurationText.Text = duration > TimeSpan.Zero ? FormatDuration(duration) : "00:00:00";
        _karaokeActiveLineIndex = -1;
        _karaokeActiveTokenIndex = -1;
        UpdateActiveKaraokeLyric(clamped);
    }

    private TimeSpan GetKaraokeMediaPosition()
    {
        return _karaokeTrackMediaPlayer?.Position
            ?? _karaokeTrackReader?.CurrentTime
            ?? TimeSpan.FromSeconds(Math.Clamp(KaraokeSeekSlider?.Value ?? 0d, 0d, Math.Max(0d, _karaokeTrackDuration.TotalSeconds)));
    }

    private TimeSpan GetKaraokePlaybackClockPosition()
    {
        var position = _karaokePlaybackClockBasePosition;
        if (_isKaraokeTrackPlaying && _karaokePlaybackClock.IsRunning)
        {
            position += TimeSpan.FromSeconds(_karaokePlaybackClock.Elapsed.TotalSeconds * GetKaraokePlaybackClockRate());
        }

        if (_karaokeTrackDuration > TimeSpan.Zero && position > _karaokeTrackDuration)
        {
            return _karaokeTrackDuration;
        }

        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private void StartKaraokePlaybackClock(TimeSpan position)
    {
        _karaokePlaybackClockBasePosition = ClampKaraokePlaybackPosition(position);
        _karaokePlaybackClock.Restart();
    }

    private void PauseKaraokePlaybackClock()
    {
        _karaokePlaybackClockBasePosition = GetKaraokePlaybackClockPosition();
        _karaokePlaybackClock.Reset();
    }

    private void StopKaraokePlaybackClock()
    {
        _karaokePlaybackClockBasePosition = TimeSpan.Zero;
        _karaokePlaybackClock.Reset();
    }

    private void SetKaraokePlaybackClockPosition(TimeSpan position)
    {
        _karaokePlaybackClockBasePosition = ClampKaraokePlaybackPosition(position);
        if (_isKaraokeTrackPlaying)
        {
            _karaokePlaybackClock.Restart();
        }
        else
        {
            _karaokePlaybackClock.Reset();
        }
    }

    private TimeSpan ClampKaraokePlaybackPosition(TimeSpan position)
    {
        var maxSeconds = Math.Max(0d, _karaokeTrackDuration.TotalSeconds);
        return TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0d, maxSeconds));
    }

    private double GetKaraokePlaybackClockRate()
    {
        return _karaokeTrackMediaPlayer is not null ? 1d : GetKaraokeTempoRatio();
    }

    private void KaraokeSeekSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_karaokeTrackDuration <= TimeSpan.Zero)
        {
            return;
        }

        _isScrubbingKaraokePlayback = true;
        KaraokeSeekSlider.CaptureMouse();
        SeekKaraokePlaybackFromPoint(e.GetPosition(KaraokeSeekSlider));
    }

    private void KaraokeSeekSliderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScrubbingKaraokePlayback || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        SeekKaraokePlaybackFromPoint(e.GetPosition(KaraokeSeekSlider));
    }

    private void KaraokeSeekSliderPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScrubbingKaraokePlayback)
        {
            return;
        }

        SeekKaraokePlaybackFromPoint(e.GetPosition(KaraokeSeekSlider));
        _isScrubbingKaraokePlayback = false;
        if (KaraokeSeekSlider.IsMouseCaptured)
        {
            KaraokeSeekSlider.ReleaseMouseCapture();
        }
    }

    private void KaraokeSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingKaraokePlaybackPosition || _karaokeTrackDuration <= TimeSpan.Zero)
        {
            return;
        }

        SeekKaraokePlaybackTo(TimeSpan.FromSeconds(e.NewValue));
    }

    private void LoadKaraokeLyricsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose karaoke lyrics",
            Filter = "Lyrics and text files|*.lrc;*.txt|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var lyrics = File.ReadAllText(dialog.FileName);
            KaraokeLyricsTextBox.Text = lyrics;
            SaveCachedKaraokeLyricsForTrack(_karaokeTrackPath, lyrics);
            KaraokeStatusText.Text = $"Loaded lyrics from {System.IO.Path.GetFileName(dialog.FileName)}.";
            PersistAppState();
        }
        catch (Exception ex)
        {
            KaraokeStatusText.Text = $"Lyrics load failed: {ex.Message}";
        }
    }

    private void ClearKaraokeLyricsClicked(object sender, RoutedEventArgs e)
    {
        KaraokeLyricsTextBox.Clear();
        DeleteCachedKaraokeLyricsForTrack(_karaokeTrackPath);
        KaraokeStatusText.Text = "Lyrics cleared.";
        PersistAppState();
    }

    private void KaraokeLyricsTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateKaraokeLyricsDisplay();
    }

    private void UpdateKaraokeLyricsDisplay()
    {
        if (KaraokeLyricsDisplayPanel is null || KaraokeLyricsTextBox is null)
        {
            return;
        }

        var text = KaraokeLyricsTextBox.Text;
        _karaokeTimedLyrics = ParseKaraokeTimedLyrics(text);
        _karaokeLyricDisplayLines.Clear();

        if (_karaokeTimedLyrics.Count > 0)
        {
            for (var i = 0; i < _karaokeTimedLyrics.Count; i++)
            {
                var line = _karaokeTimedLyrics[i];
                var end = i + 1 < _karaokeTimedLyrics.Count
                    ? _karaokeTimedLyrics[i + 1].Start
                    : EstimateKaraokeFinalLineEnd(line.Start, line.Text);
                _karaokeLyricDisplayLines.Add(CreateKaraokeLyricLineItem(line.Text, line.Start, end, line.Tokens));
            }
        }
        else
        {
            var lines = ExtractPlainKaraokeLyricLines(text);
            if (lines.Count == 0)
            {
                _karaokeLyricDisplayLines.Add(new KaraokeLyricLineItem(
                    GetKaraokeEmptyLyricDisplayText(_karaokeTrackPath),
                    null,
                    null,
                    []));
            }
            else
            {
                foreach (var line in lines)
                {
                    _karaokeLyricDisplayLines.Add(CreateKaraokeLyricLineItem(line, null, null));
                }
            }
        }

        _karaokeActiveLineIndex = -1;
        _karaokeActiveTokenIndex = -1;
        ResetKaraokeLyricVisualCache();
        RenderKaraokeLyrics(-1, -1);

        var position = _karaokeTrackMediaPlayer?.Position
            ?? _karaokeTrackReader?.CurrentTime
            ?? TimeSpan.FromSeconds(Math.Clamp(KaraokeSeekSlider?.Value ?? 0d, 0d, Math.Max(0d, _karaokeTrackDuration.TotalSeconds)));
        UpdateActiveKaraokeLyric(position);
    }

    private static string GetKaraokeEmptyLyricDisplayText(string? trackPath)
    {
        return string.IsNullOrWhiteSpace(trackPath)
            ? "Load a backing track, paste lyrics, and sing through the processed mic chain."
            : "No lyrics loaded for this song yet. Use Detect Lyrics or Load Lyrics to add timing.";
    }

    private bool LoadKaraokeLyricsForTrack(string trackPath)
    {
        if (KaraokeLyricsTextBox is null
            || string.IsNullOrWhiteSpace(trackPath)
            || !File.Exists(trackPath))
        {
            return false;
        }

        if (TryLoadCachedKaraokeLyricsForTrack(trackPath))
        {
            return true;
        }

        if (TryAutoLoadKaraokeLyricsForTrack(trackPath, replaceExisting: true))
        {
            return true;
        }

        if (TryLoadEmbeddedKaraokeLyricsForTrack(trackPath, replaceExisting: true))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text))
        {
            KaraokeLyricsTextBox.Clear();
        }

        return false;
    }

    private void SaveCurrentKaraokeLyricsForCurrentTrack()
    {
        if (_isRestoringAppState || KaraokeLyricsTextBox is null)
        {
            return;
        }

        var trackPath = _karaokeTrackPath;
        if (string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text))
        {
            DeleteCachedKaraokeLyricsForTrack(trackPath);
            if (string.Equals(_lastSavedKaraokeLyricsTrackPath, trackPath, StringComparison.OrdinalIgnoreCase))
            {
                _lastSavedKaraokeLyricsTrackPath = null;
                _lastSavedKaraokeLyricsHash = null;
            }

            return;
        }

        var lyrics = KaraokeLyricsTextBox.Text;
        var lyricsHash = ComputeKaraokeLyricsHash(lyrics);
        if (string.Equals(_lastSavedKaraokeLyricsTrackPath, trackPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_lastSavedKaraokeLyricsHash, lyricsHash, StringComparison.Ordinal))
        {
            return;
        }

        if (SaveCachedKaraokeLyricsForTrack(trackPath, lyrics))
        {
            _lastSavedKaraokeLyricsTrackPath = trackPath;
            _lastSavedKaraokeLyricsHash = lyricsHash;
        }
    }

    private bool TryLoadCachedKaraokeLyricsForTrack(string trackPath)
    {
        if (KaraokeLyricsTextBox is null)
        {
            return false;
        }

        var cachePath = GetKaraokeLyricCachePath(trackPath);
        if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            var lyrics = File.ReadAllText(cachePath);
            if (string.IsNullOrWhiteSpace(lyrics))
            {
                return false;
            }

            KaraokeLyricsTextBox.Text = lyrics;
            KaraokeStatusText.Text = "Loaded saved lyrics for this backing track.";
            return true;
        }
        catch (Exception ex)
        {
            AppStateStore.LogDiagnostic("karaoke-lyrics-cache-load-failed", ex);
            return false;
        }
    }

    private static bool SaveCachedKaraokeLyricsForTrack(string? trackPath, string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(trackPath)
            || string.IsNullOrWhiteSpace(lyrics)
            || !File.Exists(trackPath))
        {
            return false;
        }

        var cachePath = GetKaraokeLyricCachePath(trackPath);
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(KaraokeLyricCacheFolder);
            AtomicFile.WriteAllText(cachePath, lyrics);
            return true;
        }
        catch (Exception ex)
        {
            AppStateStore.LogDiagnostic("karaoke-lyrics-cache-save-failed", ex);
            return false;
        }
    }

    private static void DeleteCachedKaraokeLyricsForTrack(string? trackPath)
    {
        if (string.IsNullOrWhiteSpace(trackPath))
        {
            return;
        }

        var cachePath = GetKaraokeLyricCachePath(trackPath);
        if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
        {
            return;
        }

        try
        {
            File.Delete(cachePath);
        }
        catch (Exception ex)
        {
            AppStateStore.LogDiagnostic("karaoke-lyrics-cache-delete-failed", ex);
        }
    }

    private static string? GetKaraokeLyricCachePath(string? trackPath)
    {
        if (string.IsNullOrWhiteSpace(trackPath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(trackPath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var key = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return System.IO.Path.Combine(KaraokeLyricCacheFolder, $"{hash}.lrc");
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeKaraokeLyricsHash(string lyrics)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lyrics ?? string.Empty))).ToLowerInvariant();
    }

    private bool TryAutoLoadKaraokeLyricsForTrack(string trackPath, bool replaceExisting = false)
    {
        if (KaraokeLyricsTextBox is null
            || string.IsNullOrWhiteSpace(trackPath)
            || !File.Exists(trackPath)
            || (!replaceExisting && !string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text)))
        {
            return false;
        }

        var lyricsPath = FindLocalKaraokeLyricsFile(trackPath);
        if (!string.IsNullOrWhiteSpace(lyricsPath))
        {
            try
            {
                var lyrics = File.ReadAllText(lyricsPath);
                KaraokeLyricsTextBox.Text = lyrics;
                SaveCachedKaraokeLyricsForTrack(trackPath, lyrics);
                KaraokeStatusText.Text = $"Auto-loaded lyrics from {System.IO.Path.GetFileName(lyricsPath)}.";
                return true;
            }
            catch (Exception ex)
            {
                KaraokeStatusText.Text = $"Auto lyrics load failed: {ex.Message}";
                return false;
            }
        }

        return false;
    }

    private bool TryLoadEmbeddedKaraokeLyricsForTrack(string trackPath, bool replaceExisting = false)
    {
        if (KaraokeLyricsTextBox is null
            || (!replaceExisting && !string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text))
            || string.IsNullOrWhiteSpace(trackPath)
            || !File.Exists(trackPath))
        {
            return false;
        }

        try
        {
            if (!TryReadEmbeddedKaraokeLyrics(trackPath, out var embeddedLyrics))
            {
                return false;
            }

            KaraokeLyricsTextBox.Text = embeddedLyrics;
            SaveCachedKaraokeLyricsForTrack(trackPath, embeddedLyrics);
            KaraokeStatusText.Text = "Loaded embedded lyrics from the backing track.";
            return true;
        }
        catch (Exception ex)
        {
            AppStateStore.LogDiagnostic("karaoke-embedded-lyrics-failed", ex);
            KaraokeStatusText.Text = $"Embedded lyrics skipped safely: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadEmbeddedKaraokeLyrics(string trackPath, out string lyrics)
    {
        lyrics = string.Empty;
        var fileInfo = new FileInfo(trackPath);
        if (!fileInfo.Exists
            || fileInfo.Length <= 0
            || fileInfo.Length > MaximumKaraokeEmbeddedLyricsProbeBytes)
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(trackPath);
        try
        {
            if (extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadMp4EmbeddedLyrics(trackPath, out lyrics);
            }

            if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadMp3UnsynchronizedLyrics(trackPath, out lyrics);
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? FindLocalKaraokeLyricsFile(string trackPath)
    {
        var folder = System.IO.Path.GetDirectoryName(trackPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var baseName = System.IO.Path.GetFileNameWithoutExtension(trackPath);
        var candidateFolders = new[]
        {
            folder,
            System.IO.Path.Combine(folder, "Lyrics"),
            System.IO.Path.Combine(folder, "lyrics")
        };
        foreach (var candidateFolder in candidateFolders.Where(Directory.Exists))
        {
            foreach (var extension in new[] { ".lrc", ".txt" })
            {
                var exactPath = System.IO.Path.Combine(candidateFolder, baseName + extension);
                if (File.Exists(exactPath))
                {
                    return exactPath;
                }
            }
        }

        return null;
    }

    private static bool TryReadMp4EmbeddedLyrics(string path, out string lyrics)
    {
        lyrics = string.Empty;
        var bytes = File.ReadAllBytes(path);
        return TryFindMp4LyricsAtom(bytes, 0, bytes.Length, out lyrics);
    }

    private static bool TryFindMp4LyricsAtom(
        byte[] bytes,
        int start,
        int end,
        out string lyrics)
    {
        lyrics = string.Empty;
        var ranges = new Stack<KaraokeMp4AtomRange>();
        ranges.Push(new KaraokeMp4AtomRange(start, end, 0));
        var atomCount = 0;
        while (ranges.Count > 0)
        {
            var range = ranges.Pop();
            if (range.Depth > MaximumKaraokeMp4AtomDepth)
            {
                continue;
            }

            var offset = range.Start;
            while (offset + 8 <= range.End)
            {
                atomCount++;
                if (atomCount > MaximumKaraokeMp4AtomCount)
                {
                    return false;
                }

                var atomStart = offset;
                var atomSize = ReadBigEndianUInt32(bytes, offset);
                offset += 4;
                var typeOffset = offset;
                offset += 4;
                long size = atomSize;
                if (atomSize == 1)
                {
                    if (offset + 8 > range.End)
                    {
                        return false;
                    }

                    size = (long)ReadBigEndianUInt64(bytes, offset);
                    offset += 8;
                }
                else if (atomSize == 0)
                {
                    size = range.End - atomStart;
                }

                if (size < offset - atomStart || atomStart + size > range.End)
                {
                    return false;
                }

                var payloadStart = offset;
                var payloadEnd = (int)(atomStart + size);
                if (IsMp4AtomType(bytes, typeOffset, (byte)0xA9, (byte)'l', (byte)'y', (byte)'r')
                    && TryReadMp4DataAtomText(bytes, payloadStart, payloadEnd, out lyrics))
                {
                    return true;
                }

                if (!IsMp4AtomType(bytes, typeOffset, (byte)'m', (byte)'d', (byte)'a', (byte)'t')
                    && !IsMp4AtomType(bytes, typeOffset, (byte)'d', (byte)'a', (byte)'t', (byte)'a')
                    && payloadEnd - payloadStart >= 8)
                {
                    ranges.Push(new KaraokeMp4AtomRange(payloadStart, payloadEnd, range.Depth + 1));
                }

                offset = payloadEnd;
            }
        }

        return false;
    }

    private static bool TryReadMp4DataAtomText(byte[] bytes, int start, int end, out string text)
    {
        text = string.Empty;
        var offset = start;
        while (offset + 16 <= end)
        {
            var atomStart = offset;
            var atomSize = ReadBigEndianUInt32(bytes, offset);
            offset += 4;
            var typeOffset = offset;
            offset += 4;
            if (atomSize < 16 || atomStart + atomSize > end)
            {
                return false;
            }

            if (IsMp4AtomType(bytes, typeOffset, (byte)'d', (byte)'a', (byte)'t', (byte)'a'))
            {
                var textStart = offset + 8;
                if (textStart > atomStart + atomSize)
                {
                    return false;
                }

                text = DecodeAndCleanLyricText(bytes, textStart, (int)(atomStart + atomSize) - textStart, Encoding.UTF8);
                return !string.IsNullOrWhiteSpace(text);
            }

            offset = (int)(atomStart + atomSize);
        }

        return false;
    }

    private static bool TryReadMp3UnsynchronizedLyrics(string path, out string lyrics)
    {
        lyrics = string.Empty;
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[10];
        if (stream.Read(header) != header.Length
            || header[0] != (byte)'I'
            || header[1] != (byte)'D'
            || header[2] != (byte)'3')
        {
            return false;
        }

        var majorVersion = header[3];
        var tagSize = ReadId3SynchsafeInt(header[6], header[7], header[8], header[9]);
        if (tagSize <= 0 || tagSize > 16 * 1024 * 1024)
        {
            return false;
        }

        var tag = new byte[tagSize];
        var read = stream.Read(tag, 0, tag.Length);
        if (read != tag.Length)
        {
            return false;
        }

        var offset = 0;
        while (offset + 10 <= tag.Length)
        {
            var frameId = Encoding.ASCII.GetString(tag, offset, 4);
            if (string.IsNullOrWhiteSpace(frameId.Trim('\0')))
            {
                break;
            }

            var frameSize = majorVersion == 4
                ? ReadId3SynchsafeInt(tag[offset + 4], tag[offset + 5], tag[offset + 6], tag[offset + 7])
                : (int)ReadBigEndianUInt32(tag, offset + 4);
            if (frameSize <= 0 || offset + 10 + frameSize > tag.Length)
            {
                break;
            }

            if (frameId.Equals("USLT", StringComparison.Ordinal)
                && TryDecodeId3UnsynchronizedLyrics(tag, offset + 10, frameSize, out lyrics))
            {
                return true;
            }

            offset += 10 + frameSize;
        }

        return false;
    }

    private static bool TryDecodeId3UnsynchronizedLyrics(byte[] bytes, int offset, int length, out string lyrics)
    {
        lyrics = string.Empty;
        if (length < 5)
        {
            return false;
        }

        var encodingByte = bytes[offset];
        var cursor = offset + 4;
        var end = offset + length;
        Encoding encoding = encodingByte switch
        {
            1 => Encoding.Unicode,
            2 => Encoding.BigEndianUnicode,
            3 => Encoding.UTF8,
            _ => Encoding.Latin1
        };

        var terminatorLength = encodingByte is 1 or 2 ? 2 : 1;
        while (cursor + terminatorLength <= end)
        {
            if (terminatorLength == 2)
            {
                if (bytes[cursor] == 0 && bytes[cursor + 1] == 0)
                {
                    cursor += 2;
                    break;
                }
            }
            else if (bytes[cursor] == 0)
            {
                cursor++;
                break;
            }

            cursor++;
        }

        if (cursor >= end)
        {
            return false;
        }

        lyrics = DecodeAndCleanLyricText(bytes, cursor, end - cursor, encoding);
        return !string.IsNullOrWhiteSpace(lyrics);
    }

    private static string DecodeAndCleanLyricText(byte[] bytes, int offset, int count, Encoding encoding)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        return encoding
            .GetString(bytes, offset, count)
            .Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static bool IsMp4AtomType(byte[] bytes, int offset, byte a, byte b, byte c, byte d)
    {
        return offset >= 0
            && offset + 4 <= bytes.Length
            && bytes[offset] == a
            && bytes[offset + 1] == b
            && bytes[offset + 2] == c
            && bytes[offset + 3] == d;
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
    {
        return ((uint)bytes[offset] << 24)
            | ((uint)bytes[offset + 1] << 16)
            | ((uint)bytes[offset + 2] << 8)
            | bytes[offset + 3];
    }

    private static ulong ReadBigEndianUInt64(byte[] bytes, int offset)
    {
        return ((ulong)ReadBigEndianUInt32(bytes, offset) << 32)
            | ReadBigEndianUInt32(bytes, offset + 4);
    }

    private static int ReadId3SynchsafeInt(byte a, byte b, byte c, byte d)
    {
        return (a & 0x7F) << 21
            | (b & 0x7F) << 14
            | (c & 0x7F) << 7
            | d & 0x7F;
    }

    private async void AutoTimeKaraokeLyricsClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_karaokeTrackPath) || !File.Exists(_karaokeTrackPath))
        {
            KaraokeStatusText.Text = "Load a backing track before auto-timing lyrics.";
            return;
        }

        var lyricLines = ExtractPlainKaraokeLyricLines(KaraokeLyricsTextBox.Text);
        if (lyricLines.Count == 0)
        {
            KaraokeStatusText.Text = "Paste or load lyric lines before auto-timing.";
            return;
        }

        KaraokeStatusText.Text = "Analyzing backing track for lyric timing...";
        try
        {
            var trackPath = _karaokeTrackPath;
            var timedLines = await Task.Run(() => GenerateKaraokeLyricTimingsFromAudio(trackPath, lyricLines));
            KaraokeLyricsTextBox.Text = string.Join(
                Environment.NewLine,
                timedLines.Select(line => $"{FormatLyricTimestamp(line.Start)} {line.Text}"));
            SaveCachedKaraokeLyricsForTrack(trackPath, KaraokeLyricsTextBox.Text);
            KaraokeStatusText.Text = $"Auto-timed {timedLines.Count} lyric lines from the backing track.";
            PersistAppState();
        }
        catch (Exception ex)
        {
            KaraokeStatusText.Text = $"Auto timing failed: {ex.Message}";
        }
    }

    private async void DetectKaraokeLyricsClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_karaokeTrackPath) || !File.Exists(_karaokeTrackPath))
        {
            KaraokeStatusText.Text = "Load a backing track before detecting lyrics.";
            return;
        }

        await DetectKaraokeLyricsForTrackAsync(_karaokeTrackPath, tryLocalSidecarFirst: true);
    }

    private async Task DetectKaraokeLyricsForTrackAsync(string trackPath, bool tryLocalSidecarFirst)
    {
        if (_isDetectingKaraokeLyrics)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(trackPath) || !File.Exists(trackPath))
        {
            KaraokeStatusText.Text = "Load a backing track before detecting lyrics.";
            return;
        }

        _isDetectingKaraokeLyrics = true;
        DetectKaraokeLyricsButton.IsEnabled = false;
        KaraokeStatusText.Text = "Checking local lyrics...";
        try
        {
            if (tryLocalSidecarFirst
                && string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text)
                && LoadKaraokeLyricsForTrack(trackPath))
            {
                PersistAppState();
                return;
            }

            var toolchain = FindKaraokeAiToolchain();
            if (toolchain is null)
            {
                KaraokeStatusText.Text = GetKaraokeAiSetupMessage();
                return;
            }

            var loadedTimedLyrics = ParseKaraokeTimedLyrics(KaraokeLyricsTextBox.Text);
            if (loadedTimedLyrics.Count > 0)
            {
                UpdateKaraokeLyricsDisplay();
                KaraokeStatusText.Text = $"Using {loadedTimedLyrics.Count} already-loaded timed lyric lines. Clear lyrics to detect again.";
                PersistAppState();
                return;
            }

            KaraokeStatusText.Text = "Separating lead vocal for pro lyric detection...";
            void Report(string message)
            {
                Dispatcher.BeginInvoke(() => KaraokeStatusText.Text = message, DispatcherPriority.Background);
            }

            var detectedLyrics = await Task.Run(() => GenerateKaraokeLyricsWithAi(trackPath, toolchain, Report));
            if (!string.Equals(_karaokeTrackPath, trackPath, StringComparison.OrdinalIgnoreCase))
            {
                KaraokeStatusText.Text = "Lyric detection finished for a previous backing track.";
                return;
            }

            KaraokeLyricsTextBox.Text = detectedLyrics.LyricsText;
            SaveCachedKaraokeLyricsForTrack(trackPath, detectedLyrics.LyricsText);
            KaraokeStatusText.Text = $"Detected {detectedLyrics.LineCount} lyric lines with {detectedLyrics.TimedTokenCount} timed words.";
            PersistAppState();
        }
        catch (Exception ex)
        {
            KaraokeStatusText.Text = $"Lyric detection failed: {ShortenKaraokeProcessError(ex.Message)}";
        }
        finally
        {
            _isDetectingKaraokeLyrics = false;
            DetectKaraokeLyricsButton.IsEnabled = true;
        }
    }

    private static List<KaraokeTimedLyricLine> ParseKaraokeTimedLyrics(string text)
    {
        var timedLines = new List<KaraokeTimedLyricLine>();
        foreach (var rawLine in SplitKaraokeLyricLines(text))
        {
            var matches = KaraokeLyricTimestampRegex.Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            var lyricBody = KaraokeLyricTimestampRegex.Replace(rawLine, string.Empty).Trim();
            var timedTokens = ParseEnhancedKaraokeLyricTokens(lyricBody);
            var lyricText = KaraokeInlineLyricTimestampRegex.Replace(lyricBody, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(lyricText))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                if (TryParseLyricTimestamp(match, out var timestamp))
                {
                    timedLines.Add(new KaraokeTimedLyricLine(timestamp, lyricText, timedTokens));
                }
            }
        }

        return timedLines
            .OrderBy(line => line.Start)
            .ThenBy(line => line.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractPlainKaraokeLyricLines(string text)
    {
        return SplitKaraokeLyricLines(text)
            .Select(line => KaraokeLyricTimestampRegex.Replace(line, string.Empty).Trim())
            .Select(line => KaraokeInlineLyricTimestampRegex.Replace(line, string.Empty).Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !(line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)))
            .ToList();
    }

    private static List<KaraokeLyricToken> ParseEnhancedKaraokeLyricTokens(string lyricBody)
    {
        var source = lyricBody ?? string.Empty;
        var matches = KaraokeInlineLyricTimestampRegex.Matches(source);
        if (matches.Count == 0)
        {
            return [];
        }

        var tokens = new List<KaraokeLyricToken>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!TryParseLyricTimestamp(match, out var tokenStart))
            {
                continue;
            }

            var tokenTextStart = match.Index + match.Length;
            var nextMatch = i + 1 < matches.Count ? matches[i + 1] : null;
            var tokenTextEnd = nextMatch?.Index ?? source.Length;
            if (tokenTextEnd <= tokenTextStart)
            {
                continue;
            }

            var tokenText = source[tokenTextStart..tokenTextEnd];
            if (string.IsNullOrWhiteSpace(tokenText))
            {
                continue;
            }

            TimeSpan? tokenEnd = null;
            if (nextMatch is not null
                && TryParseLyricTimestamp(nextMatch, out var nextTimestamp)
                && nextTimestamp > tokenStart)
            {
                tokenEnd = nextTimestamp;
            }

            tokens.AddRange(CreateEnhancedKaraokeTokensForSegment(tokenText, tokenStart, tokenEnd));
        }

        return tokens;
    }

    private static List<KaraokeLyricToken> CreateEnhancedKaraokeTokensForSegment(
        string text,
        TimeSpan start,
        TimeSpan? end)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokenEnd = end.HasValue && end.Value > start
            ? end.Value
            : start + TimeSpan.FromMilliseconds(Math.Clamp(text.Count(IsKaraokeCoreCharacter) * 65d, 160d, 900d));
        var (leading, core, trailing) = SplitKaraokeDetectedWordParts(text);
        if (string.IsNullOrWhiteSpace(core))
        {
            return [new KaraokeLyricToken(text, start, tokenEnd, text.Any(IsKaraokeCoreCharacter))];
        }

        var tokens = new List<KaraokeLyricToken>(3);
        if (!string.IsNullOrEmpty(leading))
        {
            tokens.Add(new KaraokeLyricToken(leading, null, null, false));
        }

        tokens.Add(new KaraokeLyricToken(core, start, tokenEnd, true));

        if (!string.IsNullOrEmpty(trailing))
        {
            tokens.Add(new KaraokeLyricToken(trailing, null, null, false));
        }

        return tokens;
    }

    private static IEnumerable<string> SplitKaraokeLyricLines(string text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    private static bool TryParseLyricTimestamp(Match match, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        if (!int.TryParse(match.Groups["minutes"].Value, out var minutes)
            || !int.TryParse(match.Groups["seconds"].Value, out var seconds))
        {
            return false;
        }

        var fractionText = match.Groups["fraction"].Value;
        var milliseconds = 0;
        if (!string.IsNullOrWhiteSpace(fractionText))
        {
            var normalized = fractionText.PadRight(3, '0')[..3];
            _ = int.TryParse(normalized, out milliseconds);
        }

        var rawTimestamp = TimeSpan.FromMilliseconds(minutes * 60000d + seconds * 1000d + milliseconds);
        timestamp = rawTimestamp > KaraokeLyricDisplayLead
            ? rawTimestamp - KaraokeLyricDisplayLead
            : TimeSpan.Zero;
        return true;
    }

    private static List<KaraokeTimedLyricLine> GenerateKaraokeLyricTimingsFromAudio(string trackPath, IReadOnlyList<string> lyricLines)
    {
        using var reader = KaraokeTrackAudioReader.Open(trackPath);
        var windows = ReadKaraokeAudioEnergyWindows(reader);
        if (windows.Count == 0)
        {
            return GenerateEvenKaraokeLyricTimings(lyricLines, TimeSpan.Zero, reader.TotalTime);
        }

        var maxEnergy = windows.Max(window => window.Energy);
        var threshold = Math.Max(0.0025d, maxEnergy * 0.06d);
        var activeWindows = windows.Where(window => window.Energy >= threshold).ToList();
        var activeStart = activeWindows.Count > 0
            ? TimeSpan.FromSeconds(Math.Max(0d, activeWindows[0].TimeSeconds - 0.35d))
            : TimeSpan.Zero;
        var activeEnd = activeWindows.Count > 0
            ? TimeSpan.FromSeconds(Math.Min(reader.TotalTime.TotalSeconds, activeWindows[^1].TimeSeconds + 0.5d))
            : reader.TotalTime;

        var weightedWindows = windows
            .Where(window => window.TimeSeconds >= activeStart.TotalSeconds && window.TimeSeconds <= activeEnd.TotalSeconds)
            .Select(window => new KaraokeAudioEnergyWindow(
                window.TimeSeconds,
                Math.Max(0.00001d, window.Energy - threshold * 0.35d)))
            .ToList();
        var totalWeight = weightedWindows.Sum(window => window.Energy);
        if (weightedWindows.Count == 0 || totalWeight <= 0.0001d)
        {
            return GenerateEvenKaraokeLyricTimings(lyricLines, activeStart, activeEnd);
        }

        var timedLines = new List<KaraokeTimedLyricLine>(lyricLines.Count);
        for (var i = 0; i < lyricLines.Count; i++)
        {
            var targetWeight = totalWeight * i / Math.Max(1d, lyricLines.Count);
            var runningWeight = 0d;
            var timestampSeconds = activeStart.TotalSeconds;
            foreach (var window in weightedWindows)
            {
                runningWeight += window.Energy;
                if (runningWeight >= targetWeight)
                {
                    timestampSeconds = window.TimeSeconds;
                    break;
                }
            }

            timedLines.Add(new KaraokeTimedLyricLine(TimeSpan.FromSeconds(timestampSeconds), lyricLines[i]));
        }

        return timedLines;
    }

    private static List<KaraokeAudioEnergyWindow> ReadKaraokeAudioEnergyWindows(KaraokeTrackAudioReader reader)
    {
        const double windowSeconds = 0.05d;
        var channels = Math.Max(1, reader.WaveFormat.Channels);
        var samplesPerWindow = Math.Max(channels, (int)(reader.WaveFormat.SampleRate * windowSeconds) * channels);
        var buffer = new float[samplesPerWindow];
        var windows = new List<KaraokeAudioEnergyWindow>();
        var timeSeconds = 0d;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            var sum = 0d;
            for (var i = 0; i < read; i++)
            {
                sum += buffer[i] * buffer[i];
            }

            var rms = Math.Sqrt(sum / Math.Max(1, read));
            windows.Add(new KaraokeAudioEnergyWindow(timeSeconds, rms));
            timeSeconds += read / (double)channels / reader.WaveFormat.SampleRate;
        }

        return windows;
    }

    private static List<KaraokeTimedLyricLine> GenerateEvenKaraokeLyricTimings(
        IReadOnlyList<string> lyricLines,
        TimeSpan start,
        TimeSpan end)
    {
        var durationSeconds = Math.Max(1d, (end - start).TotalSeconds);
        var timedLines = new List<KaraokeTimedLyricLine>(lyricLines.Count);
        for (var i = 0; i < lyricLines.Count; i++)
        {
            var ratio = i / Math.Max(1d, lyricLines.Count);
            timedLines.Add(new KaraokeTimedLyricLine(start + TimeSpan.FromSeconds(durationSeconds * ratio), lyricLines[i]));
        }

        return timedLines;
    }

    private static KaraokeDetectedLyrics GenerateKaraokeLyricsWithAi(
        string trackPath,
        KaraokeAiToolchain toolchain,
        Action<string> reportStatus)
    {
        var workFolder = CreateKaraokeAiWorkFolder();
        PrepareCleanKaraokeAiWorkArea();
        Directory.CreateDirectory(workFolder);

        var demucsInputPath = PrepareKaraokeAiInputTrack(trackPath, workFolder, reportStatus);
        reportStatus("Separating lead vocal with Demucs...");
        var vocalPath = RunDemucsVocalSeparation(demucsInputPath, workFolder, toolchain, reportStatus);

        reportStatus("Aligning separated vocal with WhisperX...");
        var whisperJsonPath = RunWhisperXAlignment(vocalPath, workFolder, toolchain, reportStatus);

        reportStatus("Building bouncing-ball lyric timing...");
        var words = ReadWhisperXWords(whisperJsonPath);
        if (words.Count == 0)
        {
            throw new InvalidOperationException("WhisperX did not return word-level timings for this track.");
        }

        var lyricLines = GroupKaraokeWordsIntoLines(words);
        var lyricText = BuildEnhancedKaraokeLrc(lyricLines, out var timedTokenCount);
        if (string.IsNullOrWhiteSpace(lyricText))
        {
            throw new InvalidOperationException("Lyric detection finished without usable lyric text.");
        }

        return new KaraokeDetectedLyrics(lyricText, lyricLines.Count, timedTokenCount);
    }

    private static string CreateKaraokeAiWorkFolder()
    {
        Directory.CreateDirectory(KaraokeAiWorkFolder);
        return System.IO.Path.Combine(KaraokeAiWorkFolder, $"detect_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
    }

    private static void PrepareCleanKaraokeAiWorkArea()
    {
        foreach (var staleFolder in Directory.EnumerateDirectories(KaraokeAiWorkFolder, "detect*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteKaraokeAiWorkFolder(staleFolder);
        }
    }

    private static void TryDeleteKaraokeAiWorkFolder(string workFolder)
    {
        try
        {
            if (PathSafety.IsDirectoryUnderFolder(workFolder, KaraokeAiWorkFolder, allowRoot: false)
                && System.IO.Path.GetFileName(workFolder).StartsWith("detect", StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(workFolder, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static KaraokeAiToolchain? FindKaraokeAiToolchain()
    {
        var pythonPath = FindKaraokeToolFile("python.exe")
            ?? FindKaraokeToolFile("py.exe")
            ?? FindExecutableOnPath("python.exe")
            ?? FindExecutableOnPath("py.exe");
        var demucsPath = FindKaraokeToolFile("demucs.exe") ?? FindExecutableOnPath("demucs.exe");
        var whisperXPath = FindKaraokeToolFile("whisperx.exe") ?? FindExecutableOnPath("whisperx.exe");

        var canRunDemucs = !string.IsNullOrWhiteSpace(demucsPath) || !string.IsNullOrWhiteSpace(pythonPath);
        var canRunWhisperX = !string.IsNullOrWhiteSpace(whisperXPath) || !string.IsNullOrWhiteSpace(pythonPath);
        return canRunDemucs && canRunWhisperX
            ? new KaraokeAiToolchain(pythonPath, demucsPath, whisperXPath)
            : null;
    }

    private static string GetKaraokeAiSetupMessage()
    {
        return $"Pro lyric detection needs local Demucs and WhisperX. Put a Python env with demucs and whisperx under {KaraokeAiToolsFolder}, or install demucs.exe and whisperx.exe on PATH.";
    }

    private static string? FindKaraokeToolFile(string fileName)
    {
        foreach (var root in GetKaraokeToolRoots().Where(Directory.Exists))
        {
            try
            {
                var directPath = System.IO.Path.Combine(root, fileName);
                if (File.Exists(directPath))
                {
                    return directPath;
                }

                var match = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                    .OrderBy(path => path.Contains($"{System.IO.Path.DirectorySeparatorChar}.venv{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(path => path.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> GetKaraokeToolRoots()
    {
        yield return KaraokeAiToolsFolder;
        yield return System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "Tools", "WhisperX");
        yield return System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "Tools", "Demucs");
        yield return System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "KaraokeAi");
        yield return System.IO.Path.Combine(AppContext.BaseDirectory, "dependencies", "KaraokeAi");
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var rawFolder in pathVariable.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var folder = rawFolder.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                var candidate = System.IO.Path.Combine(folder, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string RunDemucsVocalSeparation(
        string trackPath,
        string workFolder,
        KaraokeAiToolchain toolchain,
        Action<string> reportStatus)
    {
        var outputFolder = System.IO.Path.Combine(workFolder, "demucs");
        Directory.CreateDirectory(outputFolder);
        string[] arguments = ["--two-stems=vocals", "--mp3", "-o", outputFolder, trackPath];
        try
        {
            RunKaraokeExternalTool(
                toolchain.DemucsPath,
                toolchain.PythonPath,
                "demucs",
                arguments,
                workFolder,
                TimeSpan.FromMinutes(20));
        }
        catch (Exception ex) when (IsDemucsWavSaveBackendFailure(ex.Message))
        {
            reportStatus("Demucs WAV output stumbled; retrying vocals as MP3...");
            outputFolder = System.IO.Path.Combine(workFolder, "demucs_mp3");
            Directory.CreateDirectory(outputFolder);
            arguments = ["--two-stems=vocals", "--mp3", "-o", outputFolder, trackPath];
            RunKaraokeExternalTool(
                toolchain.DemucsPath,
                toolchain.PythonPath,
                "demucs",
                arguments,
                workFolder,
                TimeSpan.FromMinutes(20));
        }

        var vocalPath = FindDemucsVocalOutput(outputFolder);
        if (string.IsNullOrWhiteSpace(vocalPath))
        {
            throw new InvalidOperationException("Demucs finished without producing a vocals track.");
        }

        return vocalPath;
    }

    private static string PrepareKaraokeAiInputTrack(
        string trackPath,
        string workFolder,
        Action<string> reportStatus)
    {
        if (KaraokeTrackAudioReader.CanUseSampleReader(trackPath))
        {
            return trackPath;
        }

        var ffmpegPath = FindExecutableOnPath("ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return trackPath;
        }

        reportStatus("Converting backing track to WAV for lyric detection...");
        var inputFolder = System.IO.Path.Combine(workFolder, "input");
        Directory.CreateDirectory(inputFolder);
        var wavPath = System.IO.Path.Combine(inputFolder, "karaoke_input.wav");
        string[] arguments = ["-y", "-hide_banner", "-loglevel", "error", "-i", trackPath, "-vn", "-ac", "2", "-ar", "44100", "-sample_fmt", "s16", wavPath];
        RunKaraokeProcess(ffmpegPath, arguments, inputFolder, TimeSpan.FromMinutes(5));
        if (!File.Exists(wavPath))
        {
            throw new InvalidOperationException("FFmpeg finished without producing a WAV input for Demucs.");
        }

        return wavPath;
    }

    private static string? FindDemucsVocalOutput(string outputFolder)
    {
        if (!Directory.Exists(outputFolder))
        {
            return null;
        }

        string[] preferredNames =
        [
            "vocals.wav",
            "vocals.flac",
            "vocals.mp3"
        ];
        foreach (var fileName in preferredNames)
        {
            var match = Directory.EnumerateFiles(outputFolder, fileName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsDemucsWavSaveBackendFailure(string message)
    {
        return message.Contains("torchaudio", StringComparison.OrdinalIgnoreCase)
            || message.Contains("appropriate backend", StringComparison.OrdinalIgnoreCase)
            || message.Contains("save_audio", StringComparison.OrdinalIgnoreCase);
    }

    private static string RunWhisperXAlignment(
        string vocalPath,
        string workFolder,
        KaraokeAiToolchain toolchain,
        Action<string> reportStatus)
    {
        var outputFolder = System.IO.Path.Combine(workFolder, "whisperx");
        Directory.CreateDirectory(outputFolder);

        string[] gpuArguments =
        [
            vocalPath,
            "--model", "large-v3",
            "--language", "en",
            "--output_format", "json",
            "--output_dir", outputFolder,
            "--align_model", "WAV2VEC2_ASR_LARGE_LV60K_960H",
            "--vad_method", "silero",
            "--batch_size", "4",
            "--compute_type", "float16"
        ];
        try
        {
            RunKaraokeExternalTool(
                toolchain.WhisperXPath,
                toolchain.PythonPath,
                "whisperx",
                gpuArguments,
                workFolder,
                TimeSpan.FromMinutes(30));
        }
        catch (Exception ex) when (IsWhisperXGpuRetryCandidate(ex.Message))
        {
            reportStatus("WhisperX GPU path stumbled; retrying CPU-safe alignment...");
            foreach (var staleJson in Directory.EnumerateFiles(outputFolder, "*.json", SearchOption.AllDirectories))
            {
                TryDeleteFile(staleJson);
            }

            string[] cpuArguments =
            [
                vocalPath,
                "--model", "medium",
                "--language", "en",
                "--output_format", "json",
                "--output_dir", outputFolder,
                "--vad_method", "silero",
                "--device", "cpu",
                "--compute_type", "int8"
            ];
            RunKaraokeExternalTool(
                toolchain.WhisperXPath,
                toolchain.PythonPath,
                "whisperx",
                cpuArguments,
                workFolder,
                TimeSpan.FromMinutes(60));
        }

        var jsonPath = Directory.EnumerateFiles(outputFolder, "*.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            throw new InvalidOperationException("WhisperX finished without producing a JSON transcript.");
        }

        return jsonPath;
    }

    private static bool IsWhisperXGpuRetryCandidate(string message)
    {
        return message.Contains("cuda", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cudnn", StringComparison.OrdinalIgnoreCase)
            || message.Contains("gpu", StringComparison.OrdinalIgnoreCase)
            || message.Contains("float16", StringComparison.OrdinalIgnoreCase)
            || message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not compiled with CUDA", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunKaraokeExternalTool(
        string? executablePath,
        string? pythonPath,
        string pythonModuleName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            RunKaraokeProcess(executablePath, arguments, workingDirectory, timeout);
            return;
        }

        if (!string.IsNullOrWhiteSpace(pythonPath))
        {
            var moduleArguments = new List<string>(arguments.Count + 2) { "-m", pythonModuleName };
            moduleArguments.AddRange(arguments);
            RunKaraokeProcess(pythonPath, moduleArguments, workingDirectory, timeout);
            return;
        }

        throw new InvalidOperationException($"{pythonModuleName} is not available.");
    }

    private static string RunKaraokeProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"{System.IO.Path.GetFileName(fileName)} did not start.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(fileName)} could not start: {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timeoutMilliseconds = (int)Math.Clamp(timeout.TotalMilliseconds, 1d, int.MaxValue);
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"{System.IO.Path.GetFileName(fileName)} timed out after {timeout.TotalMinutes:0} minutes.");
        }

        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(fileName)} failed with exit code {process.ExitCode}. {details}");
        }

        return stdout;
    }

    private static List<KaraokeDetectedWord> ReadWhisperXWords(string jsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        if (!document.RootElement.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var detectedWords = new List<KaraokeDetectedWord>();
        foreach (var segment in segments.EnumerateArray())
        {
            if (segment.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
            {
                foreach (var word in words.EnumerateArray())
                {
                    if (!word.TryGetProperty("word", out var wordTextElement)
                        || !TryGetJsonSeconds(word, "start", out var startSeconds)
                        || !TryGetJsonSeconds(word, "end", out var endSeconds))
                    {
                        continue;
                    }

                    var wordText = (wordTextElement.GetString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(wordText) || endSeconds <= startSeconds)
                    {
                        continue;
                    }

                    detectedWords.Add(new KaraokeDetectedWord(
                        wordText,
                        TimeSpan.FromSeconds(startSeconds),
                        TimeSpan.FromSeconds(endSeconds)));
                }

                continue;
            }

            if (segment.TryGetProperty("text", out var textElement)
                && TryGetJsonSeconds(segment, "start", out var segmentStart)
                && TryGetJsonSeconds(segment, "end", out var segmentEnd))
            {
                var segmentText = (textElement.GetString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(segmentText) && segmentEnd > segmentStart)
                {
                    detectedWords.AddRange(SplitKaraokeSegmentIntoEstimatedWords(
                        segmentText,
                        TimeSpan.FromSeconds(segmentStart),
                        TimeSpan.FromSeconds(segmentEnd)));
                }
            }
        }

        return detectedWords
            .OrderBy(word => word.Start)
            .ThenBy(word => word.End)
            .ToList();
    }

    private static List<KaraokeDetectedCharacter> ReadWhisperXCharacters(JsonElement word)
    {
        if (!word.TryGetProperty("chars", out var chars) || chars.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var detectedCharacters = new List<KaraokeDetectedCharacter>();
        foreach (var character in chars.EnumerateArray())
        {
            var text = TryGetJsonString(character, "char")
                ?? TryGetJsonString(character, "text")
                ?? TryGetJsonString(character, "letter")
                ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var hasStart = TryGetJsonSeconds(character, "start", out var startSeconds);
            var hasEnd = TryGetJsonSeconds(character, "end", out var endSeconds);
            detectedCharacters.Add(new KaraokeDetectedCharacter(
                text,
                hasStart ? TimeSpan.FromSeconds(startSeconds) : null,
                hasEnd ? TimeSpan.FromSeconds(endSeconds) : null));
        }

        return detectedCharacters;
    }

    private static List<KaraokeDetectedCharacter> GetWhisperXCharactersForWord(
        IReadOnlyList<KaraokeDetectedCharacter> segmentCharacters,
        TimeSpan wordStart,
        TimeSpan wordEnd)
    {
        var startWindow = wordStart - TimeSpan.FromMilliseconds(25);
        var endWindow = wordEnd + TimeSpan.FromMilliseconds(25);
        return segmentCharacters
            .Where(character => character.Start.HasValue
                && character.End.HasValue
                && character.End.Value >= startWindow
                && character.Start.Value <= endWindow
                && !string.IsNullOrEmpty(character.Text)
                && character.Text.Any(IsKaraokeCoreCharacter))
            .ToList();
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool TryGetJsonSeconds(JsonElement element, string propertyName, out double seconds)
    {
        seconds = 0d;
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDouble(out seconds),
            JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds),
            _ => false
        };
    }

    private static List<KaraokeDetectedWord> SplitKaraokeSegmentIntoEstimatedWords(
        string text,
        TimeSpan start,
        TimeSpan end)
    {
        var rawWords = Regex.Matches(text ?? string.Empty, @"\S+")
            .Select(match => match.Value.Trim())
            .Where(value => value.Any(char.IsLetterOrDigit))
            .ToList();
        if (rawWords.Count == 0)
        {
            return [];
        }

        if (end <= start)
        {
            end = start + TimeSpan.FromMilliseconds(Math.Max(220d, rawWords.Count * 180d));
        }

        var weights = rawWords
            .Select(word => Math.Max(1d, word.Count(char.IsLetterOrDigit) * 0.72d))
            .ToList();
        var totalWeight = Math.Max(0.01d, weights.Sum());
        var duration = end - start;
        var cursor = start;
        var result = new List<KaraokeDetectedWord>(rawWords.Count);
        for (var i = 0; i < rawWords.Count; i++)
        {
            var wordDuration = TimeSpan.FromSeconds(duration.TotalSeconds * weights[i] / totalWeight);
            var wordEnd = i == rawWords.Count - 1
                ? end
                : cursor + wordDuration;
            if (wordEnd <= cursor)
            {
                wordEnd = cursor + TimeSpan.FromMilliseconds(120);
            }

            result.Add(new KaraokeDetectedWord(rawWords[i], cursor, wordEnd));
            cursor = wordEnd;
        }

        return result;
    }

    private static List<List<KaraokeDetectedWord>> GroupKaraokeWordsIntoLines(IReadOnlyList<KaraokeDetectedWord> words)
    {
        const double maxGapSeconds = 0.78d;
        const double maxLineSeconds = 4.2d;
        const int maxLineCharacters = 38;
        var lines = new List<List<KaraokeDetectedWord>>();
        var current = new List<KaraokeDetectedWord>();

        foreach (var word in words)
        {
            if (current.Count > 0)
            {
                var gapSeconds = (word.Start - current[^1].End).TotalSeconds;
                var lineSeconds = (word.End - current[0].Start).TotalSeconds;
                var characterCount = current.Sum(item => item.Text.Length) + current.Count + word.Text.Length;
                if (gapSeconds > maxGapSeconds
                    || lineSeconds > maxLineSeconds
                    || characterCount > maxLineCharacters
                    || ShouldStartNewKaraokePhraseLine(current, word, gapSeconds, characterCount))
                {
                    lines.Add(current);
                    current = [];
                }
            }

            current.Add(word);
        }

        if (current.Count > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static bool ShouldStartNewKaraokePhraseLine(
        IReadOnlyList<KaraokeDetectedWord> current,
        KaraokeDetectedWord nextWord,
        double gapSeconds,
        int characterCountWithNextWord)
    {
        if (current.Count < 3)
        {
            return false;
        }

        var currentCharacters = current.Sum(item => item.Text.Length) + Math.Max(0, current.Count - 1);
        var next = NormalizeKaraokePhraseWord(nextWord.Text);
        if (string.IsNullOrWhiteSpace(next))
        {
            return false;
        }

        if (gapSeconds >= 0.42d && currentCharacters >= 18)
        {
            return true;
        }

        if (currentCharacters >= 24 && IsKaraokePhraseStarter(next))
        {
            return true;
        }

        return characterCountWithNextWord > 32 && IsKaraokePhraseStarter(next);
    }

    private static string NormalizeKaraokePhraseWord(string text)
    {
        return new string((text ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static bool IsKaraokePhraseStarter(string word)
    {
        return word is "and"
            or "but"
            or "cause"
            or "for"
            or "he"
            or "i"
            or "im"
            or "in"
            or "nothing"
            or "the"
            or "then"
            or "there"
            or "we"
            or "when"
            or "who"
            or "you"
            or "your";
    }

    private static string BuildEnhancedKaraokeLrc(IReadOnlyList<List<KaraokeDetectedWord>> lyricLines, out int timedTokenCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[by:Jericho Down AI Lyric Detection]");
        builder.AppendLine("[re:Demucs vocals + WhisperX word alignment]");
        timedTokenCount = 0;

        foreach (var line in lyricLines)
        {
            if (line.Count == 0)
            {
                continue;
            }

            var tokens = CreateTimedKaraokeTokensFromWords(line);
            var firstTimestamp = tokens.FirstOrDefault(token => token.Start.HasValue)?.Start ?? line[0].Start;
            builder.Append(FormatLyricTimestamp(firstTimestamp));
            builder.Append(' ');

            foreach (var token in tokens)
            {
                if (token.IsSingable && token.Start.HasValue)
                {
                    builder.Append(FormatInlineLyricTimestamp(token.Start.Value));
                    timedTokenCount++;
                }

                builder.Append(token.Text);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static List<KaraokeLyricToken> CreateTimedKaraokeTokensFromWords(IReadOnlyList<KaraokeDetectedWord> words)
    {
        var tokens = new List<KaraokeLyricToken>();
        for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
        {
            var word = words[wordIndex];
            if (wordIndex > 0)
            {
                tokens.Add(new KaraokeLyricToken(" ", null, null, false));
            }

            var (leading, core, trailing) = SplitKaraokeDetectedWordParts(word.Text);
            if (!string.IsNullOrEmpty(leading))
            {
                tokens.Add(new KaraokeLyricToken(leading, null, null, false));
            }

            if (string.IsNullOrWhiteSpace(core))
            {
                tokens.Add(new KaraokeLyricToken(word.Text, word.Start, word.End, word.Text.Any(char.IsLetterOrDigit)));
            }
            else
            {
                tokens.Add(new KaraokeLyricToken(core, word.Start, word.End, true));
            }

            if (!string.IsNullOrEmpty(trailing))
            {
                tokens.Add(new KaraokeLyricToken(trailing, null, null, false));
            }
        }

        return tokens;
    }

    private static List<KaraokeLyricToken> CreateTimedKaraokeSyllablesFromCharacters(
        string core,
        IReadOnlyList<string> syllables,
        KaraokeDetectedWord word)
    {
        if (word.Characters is not { Count: > 0 } characters || syllables.Count == 0)
        {
            return [];
        }

        var timedCharacters = characters
            .Where(character => character.Text.Any(IsKaraokeCoreCharacter) && character.Start.HasValue)
            .ToList();
        var expectedCharacterCount = core.Count(IsKaraokeCoreCharacter);
        if (timedCharacters.Count < expectedCharacterCount || expectedCharacterCount == 0)
        {
            return [];
        }

        var result = new List<KaraokeLyricToken>(syllables.Count);
        var characterIndex = 0;
        for (var syllableIndex = 0; syllableIndex < syllables.Count; syllableIndex++)
        {
            var syllable = syllables[syllableIndex];
            var neededCharacters = Math.Max(1, syllable.Count(IsKaraokeCoreCharacter));
            if (characterIndex + neededCharacters > timedCharacters.Count)
            {
                return [];
            }

            var firstCharacter = timedCharacters[characterIndex];
            var lastCharacter = timedCharacters[characterIndex + neededCharacters - 1];
            var start = firstCharacter.Start ?? word.Start;
            var nextCharacterStart = characterIndex + neededCharacters < timedCharacters.Count
                ? timedCharacters[characterIndex + neededCharacters].Start
                : null;
            var end = lastCharacter.End
                ?? nextCharacterStart
                ?? (syllableIndex == syllables.Count - 1 ? word.End : null);
            if (!end.HasValue || end.Value <= start)
            {
                end = syllableIndex == syllables.Count - 1 && word.End > start
                    ? word.End
                    : start + TimeSpan.FromMilliseconds(120);
            }

            result.Add(new KaraokeLyricToken(syllable, start, end, true));
            characterIndex += neededCharacters;
        }

        return result;
    }

    private static (string Leading, string Core, string Trailing) SplitKaraokeDetectedWordParts(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var start = 0;
        while (start < text.Length && !char.IsLetterOrDigit(text[start]) && text[start] != '\'')
        {
            start++;
        }

        var end = text.Length - 1;
        while (end >= start && !char.IsLetterOrDigit(text[end]) && text[end] != '\'')
        {
            end--;
        }

        if (start > end)
        {
            return (string.Empty, text, string.Empty);
        }

        return (text[..start], text[start..(end + 1)], text[(end + 1)..]);
    }

    private static string FormatInlineLyricTimestamp(TimeSpan timestamp)
    {
        return $"<{(int)timestamp.TotalMinutes:00}:{timestamp.Seconds:00}.{timestamp.Milliseconds / 10:00}>";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string ShortenKaraokeProcessError(string message)
    {
        var firstUsefulLine = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(firstUsefulLine))
        {
            return "No diagnostic output was returned.";
        }

        return firstUsefulLine.Length <= 260
            ? firstUsefulLine
            : firstUsefulLine[..260] + "...";
    }

    private static string FormatLyricTimestamp(TimeSpan timestamp)
    {
        return $"[{(int)timestamp.TotalMinutes:00}:{timestamp.Seconds:00}.{timestamp.Milliseconds / 10:00}]";
    }

    private KaraokeLyricLineItem CreateKaraokeLyricLineItem(string text, TimeSpan? start, TimeSpan? end)
    {
        var tokens = CreateKaraokeLyricTokens(text, start, end);
        return new KaraokeLyricLineItem(text, start, end, tokens);
    }

    private KaraokeLyricLineItem CreateKaraokeLyricLineItem(
        string text,
        TimeSpan? start,
        TimeSpan? end,
        IReadOnlyList<KaraokeLyricToken>? timedTokens)
    {
        if (timedTokens is { Count: > 0 })
        {
            var completedTokens = CompleteKaraokeTokenEnds(timedTokens, end);
            return new KaraokeLyricLineItem(text, start, end, completedTokens);
        }

        return CreateKaraokeLyricLineItem(text, start, end);
    }

    private TimeSpan EstimateKaraokeFinalLineEnd(TimeSpan start, string text)
    {
        var minimumSeconds = Math.Clamp(EstimateKaraokeTokenWeight(text) * 0.18d, 1.5d, 6d);
        var estimatedEnd = start + TimeSpan.FromSeconds(minimumSeconds);
        if (_karaokeTrackDuration > TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(Math.Clamp(estimatedEnd.TotalSeconds, start.TotalSeconds + 1d, _karaokeTrackDuration.TotalSeconds));
        }

        return estimatedEnd;
    }

    private static List<KaraokeLyricToken> CreateKaraokeLyricTokens(string text, TimeSpan? start, TimeSpan? end)
    {
        var rawTokens = SplitKaraokeSyllableTokens(text);
        if (!start.HasValue || !end.HasValue || end <= start || rawTokens.Count == 0)
        {
            return rawTokens
                .Select(token => new KaraokeLyricToken(token.Text, null, null, token.IsSingable))
                .ToList();
        }

        var weightedTokens = rawTokens
            .Select(token => new
            {
                Token = token,
                Weight = token.IsSingable ? Math.Max(1d, token.Text.Count(char.IsLetterOrDigit) * 0.72d) : 0.15d
            })
            .ToList();
        var totalWeight = weightedTokens.Sum(token => token.Weight);
        var lineDuration = end.Value - start.Value;
        var cursor = start.Value;
        var timedTokens = new List<KaraokeLyricToken>(weightedTokens.Count);
        foreach (var item in weightedTokens)
        {
            var tokenDuration = TimeSpan.FromSeconds(lineDuration.TotalSeconds * item.Weight / Math.Max(0.01d, totalWeight));
            var tokenEnd = cursor + tokenDuration;
            timedTokens.Add(new KaraokeLyricToken(item.Token.Text, cursor, tokenEnd, item.Token.IsSingable));
            cursor = tokenEnd;
        }

        return timedTokens;
    }

    private static List<KaraokeLyricToken> CompleteKaraokeTokenEnds(
        IReadOnlyList<KaraokeLyricToken> tokens,
        TimeSpan? fallbackEnd)
    {
        var completed = new List<KaraokeLyricToken>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.Start.HasValue)
            {
                completed.Add(token);
                continue;
            }

            var tokenEnd = token.End;
            if (!tokenEnd.HasValue || tokenEnd.Value <= token.Start.Value)
            {
                tokenEnd = tokens
                    .Skip(i + 1)
                    .FirstOrDefault(next => next.Start.HasValue && next.Start.Value > token.Start.Value)
                    ?.Start
                    ?? fallbackEnd;
            }

            if (!tokenEnd.HasValue || tokenEnd.Value <= token.Start.Value)
            {
                tokenEnd = token.Start.Value + TimeSpan.FromMilliseconds(180);
            }

            completed.Add(token with { End = tokenEnd });
        }

        return completed;
    }

    private static List<KaraokeRawLyricToken> SplitKaraokeSyllableTokens(string text)
    {
        var tokens = new List<KaraokeRawLyricToken>();
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"[A-Za-z0-9']+|[^A-Za-z0-9']+"))
        {
            var part = match.Value;
            if (part.Any(char.IsLetterOrDigit))
            {
                foreach (var syllable in SplitKaraokeWordIntoSyllables(part))
                {
                    tokens.Add(new KaraokeRawLyricToken(syllable, true));
                }
            }
            else
            {
                tokens.Add(new KaraokeRawLyricToken(part, false));
            }
        }

        return tokens;
    }

    private static List<string> SplitKaraokeWordIntoSyllables(string word)
    {
        if (word.Length <= 4)
        {
            return [word];
        }

        var parts = new List<string>();
        var start = 0;
        for (var i = 1; i < word.Length - 1; i++)
        {
            if (IsKaraokeVowel(word[i - 1]) && !IsKaraokeVowel(word[i]) && IsKaraokeVowel(word[i + 1]))
            {
                parts.Add(word[start..i]);
                start = i;
            }
            else if (!IsKaraokeVowel(word[i - 1]) && IsKaraokeVowel(word[i]) && !IsKaraokeVowel(word[i + 1]) && i - start >= 3)
            {
                parts.Add(word[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < word.Length)
        {
            parts.Add(word[start..]);
        }

        return parts.Count == 0 ? [word] : parts;
    }

    private static bool IsKaraokeVowel(char value)
    {
        return "aeiouyAEIOUY".IndexOf(value) >= 0;
    }

    private static bool IsKaraokeCoreCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '\'';
    }

    private static double EstimateKaraokeTokenWeight(string text)
    {
        return SplitKaraokeSyllableTokens(text).Where(token => token.IsSingable).Sum(token => Math.Max(1, token.Text.Length));
    }

    private void UpdateActiveKaraokeLyric(TimeSpan position)
    {
        if (KaraokeLyricsDisplayPanel is null || _karaokeLyricDisplayLines.Count == 0)
        {
            return;
        }

        var activeLineIndex = GetActiveKaraokeLineIndex(position);
        var activeLine = _karaokeLyricDisplayLines[Math.Clamp(activeLineIndex, 0, _karaokeLyricDisplayLines.Count - 1)];
        var activeTokenIndex = GetActiveKaraokeTokenIndex(activeLine, position);
        activeTokenIndex = ConstrainKaraokeTokenJump(activeLineIndex, activeTokenIndex);
        if (_karaokeActiveLineIndex == activeLineIndex && _karaokeActiveTokenIndex == activeTokenIndex)
        {
            return;
        }

        _karaokeActiveLineIndex = activeLineIndex;
        _karaokeActiveTokenIndex = activeTokenIndex;
        RenderKaraokeLyrics(activeLineIndex, activeTokenIndex, position);
    }

    private int GetActiveKaraokeLineIndex(TimeSpan position)
    {
        var firstTimedIndex = -1;
        for (var i = 0; i < _karaokeLyricDisplayLines.Count; i++)
        {
            if (_karaokeLyricDisplayLines[i].Start.HasValue)
            {
                firstTimedIndex = i;
                break;
            }
        }

        if (firstTimedIndex >= 0)
        {
            var bestTimedIndex = -1;
            var low = firstTimedIndex;
            var high = _karaokeLyricDisplayLines.Count - 1;
            while (low <= high)
            {
                var mid = low + (high - low) / 2;
                var start = _karaokeLyricDisplayLines[mid].Start;
                if (!start.HasValue)
                {
                    high = mid - 1;
                    continue;
                }

                if (start.Value <= position)
                {
                    bestTimedIndex = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return bestTimedIndex >= 0 ? bestTimedIndex : firstTimedIndex;
        }

        if (_karaokeTrackDuration > TimeSpan.Zero && _karaokeLyricDisplayLines.Count > 1)
        {
            var ratio = Math.Clamp(position.TotalSeconds / Math.Max(1d, _karaokeTrackDuration.TotalSeconds), 0d, 0.999d);
            return (int)(ratio * _karaokeLyricDisplayLines.Count);
        }

        return 0;
    }

    private int ConstrainKaraokeTokenJump(int activeLineIndex, int targetTokenIndex)
    {
        if (targetTokenIndex < 0
            || _karaokeActiveLineIndex != activeLineIndex
            || _karaokeActiveTokenIndex < 0
            || activeLineIndex < 0
            || activeLineIndex >= _karaokeLyricDisplayLines.Count)
        {
            return targetTokenIndex;
        }

        if (targetTokenIndex <= _karaokeActiveTokenIndex)
        {
            return targetTokenIndex;
        }

        var line = _karaokeLyricDisplayLines[activeLineIndex];
        if (ShouldHoldCurrentKaraokeToken(line, _karaokeActiveTokenIndex))
        {
            return _karaokeActiveTokenIndex;
        }

        var nextSingableToken = FindNextKaraokeSingableToken(line, _karaokeActiveTokenIndex);
        return nextSingableToken >= 0 && targetTokenIndex > nextSingableToken
            ? nextSingableToken
            : targetTokenIndex;
    }

    private bool ShouldHoldCurrentKaraokeToken(KaraokeLyricLineItem line, int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex >= line.Tokens.Count)
        {
            return false;
        }

        var token = line.Tokens[tokenIndex];
        if (!IsShortKaraokeToken(token) || token.Start is null)
        {
            return false;
        }

        var elapsed = GetKaraokePlaybackClockPosition() - token.Start.Value;
        return elapsed >= TimeSpan.Zero && elapsed < KaraokeShortWordDisplayHold;
    }

    private static bool IsShortKaraokeToken(KaraokeLyricToken token)
    {
        return token.IsSingable && token.Text.Count(char.IsLetterOrDigit) is > 0 and <= 3;
    }

    private static int FindNextKaraokeSingableToken(KaraokeLyricLineItem line, int afterTokenIndex)
    {
        for (var i = Math.Max(0, afterTokenIndex + 1); i < line.Tokens.Count; i++)
        {
            if (line.Tokens[i].IsSingable)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetActiveKaraokeTokenIndex(KaraokeLyricLineItem line, TimeSpan position)
    {
        if (line.Tokens.Count == 0)
        {
            return -1;
        }

        var hasTimedToken = false;
        var activeTimedTokenIndex = -1;
        for (var i = 0; i < line.Tokens.Count; i++)
        {
            var token = line.Tokens[i];
            if (!token.IsSingable || !token.Start.HasValue)
            {
                continue;
            }

            hasTimedToken = true;
            if (token.Start.Value > position)
            {
                break;
            }

            if (IsShortKaraokeToken(token)
                && position - token.Start.Value < KaraokeShortWordDisplayHold)
            {
                return i;
            }

            activeTimedTokenIndex = i;
        }

        if (hasTimedToken)
        {
            return activeTimedTokenIndex;
        }

        if (!line.Start.HasValue || !line.End.HasValue || line.End <= line.Start)
        {
            return line.Tokens.FindIndex(token => token.IsSingable);
        }

        if (position < line.Start.Value)
        {
            return -1;
        }

        var singableTokenCount = 0;
        foreach (var token in line.Tokens)
        {
            if (token.IsSingable)
            {
                singableTokenCount++;
            }
        }

        if (singableTokenCount == 0)
        {
            return -1;
        }

        var ratio = Math.Clamp((position - line.Start.Value).TotalSeconds / Math.Max(0.1d, (line.End.Value - line.Start.Value).TotalSeconds), 0d, 0.999d);
        var targetSingableIndex = (int)(ratio * singableTokenCount);
        var currentSingableIndex = 0;
        for (var i = 0; i < line.Tokens.Count; i++)
        {
            if (!line.Tokens[i].IsSingable)
            {
                continue;
            }

            if (currentSingableIndex == targetSingableIndex)
            {
                return i;
            }

            currentSingableIndex++;
        }

        return -1;
    }

    private void RenderKaraokeLyrics(int activeLineIndex, int activeTokenIndex, TimeSpan? position = null)
    {
        if (KaraokeLyricsDisplayPanel is null)
        {
            return;
        }

        var windowStartLineIndex = GetKaraokeLyricWindowStart(activeLineIndex);
        var previousActiveLineIndex = _karaokeRenderedActiveLineIndex;
        var visualTreeRebuilt = EnsureKaraokeLyricVisualTree(windowStartLineIndex);
        if (_karaokeRenderedActiveLineIndex == activeLineIndex
            && _karaokeRenderedActiveTokenIndex == activeTokenIndex)
        {
            return;
        }

        _karaokeRenderedActiveLineIndex = activeLineIndex;
        _karaokeRenderedActiveTokenIndex = activeTokenIndex;

        if (previousActiveLineIndex >= 0
            && previousActiveLineIndex < _karaokeLyricDisplayLines.Count
            && previousActiveLineIndex != activeLineIndex)
        {
            RenderKaraokeLyricLine(previousActiveLineIndex, false, -1, null);
        }

        if (activeLineIndex >= 0 && activeLineIndex < _karaokeLyricDisplayLines.Count)
        {
            RenderKaraokeLyricLine(activeLineIndex, true, activeTokenIndex, position);
            if (visualTreeRebuilt)
            {
                AnimateKaraokeLyricWindow();
            }
            else if (previousActiveLineIndex != activeLineIndex)
            {
                AnimateKaraokeLyricLine(activeLineIndex);
            }
        }
    }

    private int GetKaraokeLyricWindowStart(int activeLineIndex)
    {
        if (_karaokeLyricDisplayLines.Count <= KaraokeLyricVisibleLineCount)
        {
            return 0;
        }

        if (activeLineIndex < 0)
        {
            return 0;
        }

        var maxStart = Math.Max(0, _karaokeLyricDisplayLines.Count - KaraokeLyricVisibleLineCount);
        return Math.Clamp(activeLineIndex - 1, 0, maxStart);
    }

    private bool EnsureKaraokeLyricVisualTree(int windowStartLineIndex)
    {
        if (KaraokeLyricsDisplayPanel is null)
        {
            return false;
        }

        var visibleLineCount = Math.Min(KaraokeLyricVisibleLineCount, _karaokeLyricDisplayLines.Count);
        if (_karaokeRenderedWindowStartLineIndex == windowStartLineIndex
            && _karaokeLyricLineBorders.Count == visibleLineCount
            && _karaokeLyricLineTextBlocks.Count == visibleLineCount
            && _karaokeLyricLineRuns.Count == visibleLineCount
            && KaraokeLyricsDisplayPanel.Children.Count == visibleLineCount)
        {
            return false;
        }

        KaraokeLyricsDisplayPanel.Children.Clear();
        _karaokeLyricLineBorders.Clear();
        _karaokeLyricLineTextBlocks.Clear();
        _karaokeLyricLineRuns.Clear();
        _karaokeRenderedActiveLineIndex = int.MinValue;
        _karaokeRenderedActiveTokenIndex = int.MinValue;
        _karaokeRenderedWindowStartLineIndex = windowStartLineIndex;

        for (var offset = 0; offset < visibleLineCount; offset++)
        {
            var lineIndex = windowStartLineIndex + offset;
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            };
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2),
                Child = textBlock,
                RenderTransform = new TranslateTransform()
            };
            border.RenderTransformOrigin = new Point(0.5d, 0.5d);

            _karaokeLyricLineTextBlocks.Add(textBlock);
            _karaokeLyricLineBorders.Add(border);
            _karaokeLyricLineRuns.Add([]);
            KaraokeLyricsDisplayPanel.Children.Add(border);
            BuildKaraokeLyricLineRuns(lineIndex, offset);
            RenderKaraokeLyricLine(lineIndex, false, -1, null);
        }

        return true;
    }

    private void AnimateKaraokeLyricWindow()
    {
        for (var i = 0; i < _karaokeLyricLineBorders.Count; i++)
        {
            AnimateKaraokeLyricVisual(_karaokeLyricLineBorders[i], 8d + i);
        }
    }

    private void AnimateKaraokeLyricLine(int lineIndex)
    {
        var localLineIndex = lineIndex - _karaokeRenderedWindowStartLineIndex;
        if (localLineIndex < 0 || localLineIndex >= _karaokeLyricLineBorders.Count)
        {
            return;
        }

        AnimateKaraokeLyricVisual(_karaokeLyricLineBorders[localLineIndex], 5d);
    }

    private static void AnimateKaraokeLyricVisual(Border border, double yOffset)
    {
        if (border.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            border.RenderTransform = transform;
        }

        var duration = new Duration(KaraokeLyricLineTransitionDuration);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        border.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.78d, 1d, duration)
            {
                EasingFunction = easing
            });
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(yOffset, 0d, duration)
            {
                EasingFunction = easing
            });
    }

    private void ResetKaraokeLyricVisualCache()
    {
        _karaokeLyricLineBorders.Clear();
        _karaokeLyricLineTextBlocks.Clear();
        _karaokeLyricLineRuns.Clear();
        _karaokeRenderedActiveLineIndex = int.MinValue;
        _karaokeRenderedActiveTokenIndex = int.MinValue;
        _karaokeRenderedWindowStartLineIndex = int.MinValue;
        KaraokeLyricsDisplayPanel?.Children.Clear();
    }

    private void BuildKaraokeLyricLineRuns(int lineIndex, int localLineIndex)
    {
        if (lineIndex < 0
            || lineIndex >= _karaokeLyricDisplayLines.Count
            || localLineIndex < 0
            || localLineIndex >= _karaokeLyricLineTextBlocks.Count
            || localLineIndex >= _karaokeLyricLineRuns.Count)
        {
            return;
        }

        var line = _karaokeLyricDisplayLines[lineIndex];
        var textBlock = _karaokeLyricLineTextBlocks[localLineIndex];
        var runs = _karaokeLyricLineRuns[localLineIndex];
        textBlock.Inlines.Clear();
        textBlock.Text = string.Empty;
        runs.Clear();

        if (line.Tokens.Count == 0)
        {
            var run = new Run(line.Text);
            runs.Add(run);
            textBlock.Inlines.Add(run);
            return;
        }

        foreach (var token in line.Tokens)
        {
            var run = new Run(token.Text);
            runs.Add(run);
            textBlock.Inlines.Add(run);
        }
    }

    private void RenderKaraokeLyricLine(int lineIndex, bool isActiveLine, int activeTokenIndex, TimeSpan? position)
    {
        var localLineIndex = lineIndex - _karaokeRenderedWindowStartLineIndex;
        if (lineIndex < 0
            || lineIndex >= _karaokeLyricDisplayLines.Count
            || localLineIndex < 0
            || localLineIndex >= _karaokeLyricLineTextBlocks.Count
            || localLineIndex >= _karaokeLyricLineBorders.Count
            || localLineIndex >= _karaokeLyricLineRuns.Count)
        {
            return;
        }

        var line = _karaokeLyricDisplayLines[lineIndex];
        var textBlock = _karaokeLyricLineTextBlocks[localLineIndex];
        textBlock.FontSize = 34d;
        textBlock.FontWeight = isActiveLine ? FontWeights.Bold : FontWeights.SemiBold;
        textBlock.LineHeight = 48d;
        textBlock.Foreground = isActiveLine ? _karaokeActiveLineBrush : _karaokeInactiveLineBrush;

        var border = _karaokeLyricLineBorders[localLineIndex];
        border.Background = isActiveLine ? _karaokeActiveLineBackgroundBrush : Brushes.Transparent;
        var runs = _karaokeLyricLineRuns[localLineIndex];
        if (runs.Count == 0)
        {
            return;
        }

        if (line.Tokens.Count == 0)
        {
            var run = runs[0];
            run.Foreground = textBlock.Foreground;
            run.FontWeight = textBlock.FontWeight;
            run.FontSize = textBlock.FontSize;
            return;
        }

        var tokenCount = Math.Min(line.Tokens.Count, runs.Count);
        for (var tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
        {
            var token = line.Tokens[tokenIndex];
            var run = runs[tokenIndex];
            var isActiveToken = isActiveLine && tokenIndex == activeTokenIndex && token.IsSingable;
            var isSungToken = isActiveLine
                && !isActiveToken
                && position.HasValue
                && token.IsSingable
                && token.Start.HasValue
                && token.Start.Value < position.Value;
            run.Foreground = isActiveToken
                ? _karaokeActiveWordBrush
                : isSungToken
                    ? _karaokeSungWordBrush
                    : textBlock.Foreground;
            run.FontWeight = isActiveToken
                ? FontWeights.ExtraBold
                : isSungToken
                    ? FontWeights.Bold
                    : textBlock.FontWeight;
            run.FontSize = textBlock.FontSize;
        }
    }

    private void KaraokeKeySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingKaraokeAdjustments)
        {
            return;
        }

        _karaokeKeySemitones = Math.Round(e.NewValue);
        UpdateKaraokeAdjustmentStatus();
        PersistAppState();
    }

    private void KaraokeTempoSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingKaraokeAdjustments)
        {
            return;
        }

        _karaokeTempoPercent = Math.Round(e.NewValue);
        UpdateKaraokeAdjustmentStatus();
        PersistAppState();
    }

    private void KaraokeVocalReductionChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingKaraokeAdjustments)
        {
            return;
        }

        _karaokeVocalReductionEnabled = KaraokeVocalReductionCheckBox.IsChecked == true;
        _karaokeTrackVocalReductionProvider?.SetEnabled(_karaokeVocalReductionEnabled);
        UpdateKaraokeAdjustmentStatus();
        PersistAppState();
    }

    private void UpdateKaraokeAdjustmentStatus()
    {
        if (KaraokeKeyValueText is null || KaraokeTempoValueText is null || KaraokeAdjustmentStatusText is null)
        {
            return;
        }

        KaraokeKeyValueText.Text = $"{_karaokeKeySemitones:+0;-0;0} st";
        KaraokeTempoValueText.Text = $"{_karaokeTempoPercent:0}%";
        var vocalReductionText = _karaokeVocalReductionEnabled ? "lead vocal reduction on" : "normal song playback";
        KaraokeAdjustmentStatusText.Text = $"Key {_karaokeKeySemitones:+0;-0;0} st, tempo {_karaokeTempoPercent:0}%, {vocalReductionText}.";
        ApplyKaraokeTimePitchSettings();
        _karaokeTrackVocalReductionProvider?.SetEnabled(_karaokeVocalReductionEnabled);
    }

    private double GetKaraokeTempoRatio()
    {
        return Math.Clamp(_karaokeTempoPercent / 100d, 0.5d, 2d);
    }

    private double GetKaraokeKeyRatio()
    {
        return Math.Clamp(Math.Pow(2d, _karaokeKeySemitones / 12d), 0.5d, 2d);
    }

    private double GetKaraokePitchFactor()
    {
        return Math.Clamp(GetKaraokeKeyRatio() / GetKaraokeTempoRatio(), 0.5d, 2d);
    }

    private void ApplyKaraokeTimePitchSettings()
    {
        _karaokeTrackRateProvider?.SetPlaybackRate(GetKaraokeTempoRatio());
        if (_karaokeTrackPitchProvider is not null)
        {
            _karaokeTrackPitchProvider.PitchFactor = (float)GetKaraokePitchFactor();
        }
    }

    private void BrowseKaraokeRecordingFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose karaoke recording folder",
            InitialDirectory = Directory.Exists(_karaokeRecordingFolder)
                ? _karaokeRecordingFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _karaokeRecordingFolder = dialog.FolderName;
        UpdateKaraokeRecordingFolderText();
        PersistAppState();
    }

    private void KaraokeRecordVocalClicked(object sender, RoutedEventArgs e)
    {
        if (_isKaraokeVocalRecording)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_karaokeTrackPath) || !File.Exists(_karaokeTrackPath))
        {
            KaraokeVocalStatusText.Text = "Load a backing track before recording karaoke.";
            KaraokePlaybackStatusText.Text = "Choose a backing track first.";
            return;
        }

        if (!_spectrumService.IsRunning)
        {
            KaraokeVocalStatusText.Text = "Select a microphone before recording vocals.";
            return;
        }

        if (_spectrumService.IsProcessedAudioRecording)
        {
            KaraokeVocalStatusText.Text = "Another processed audio recording is already running.";
            return;
        }

        try
        {
            StopKaraokePlayback(clearTrack: false);
            SeekKaraokePlaybackTo(TimeSpan.Zero);
            if (!StartKaraokePlayback())
            {
                KaraokeVocalStatusText.Text = "Backing track could not start, so recording did not begin.";
                return;
            }

            var path = CreateKaraokeVocalRecordingPath(DateTime.Now);
            _spectrumService.StartProcessedAudioRecording(path);
            _isKaraokeVocalRecording = true;
            _isKaraokeVocalRecordingPaused = false;
            _karaokeVocalRecordingPath = path;
            _karaokeVocalRecordingStartedAt = DateTime.UtcNow;
            _karaokeVocalRecordingPausedAt = default;
            _karaokeVocalRecordingPausedDuration = TimeSpan.Zero;
            KaraokeVocalStatusText.Text = $"Recording karaoke DSP vocal: {System.IO.Path.GetFileName(path)}";
            KaraokeStatusText.Text = "Karaoke recording is live.";
        }
        catch (Exception ex)
        {
            StopKaraokePlayback(clearTrack: false);
            ResetKaraokePlaybackProgress();
            _isKaraokeVocalRecording = false;
            _isKaraokeVocalRecordingPaused = false;
            _karaokeVocalRecordingPath = null;
            KaraokeVocalStatusText.Text = $"Vocal recording failed: {ex.Message}";
        }

        UpdateKaraokeTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void PauseKaraokeVocalRecording(string statusText)
    {
        if (!_isKaraokeVocalRecording || _isKaraokeVocalRecordingPaused)
        {
            return;
        }

        _karaokeVocalRecordingPausedAt = DateTime.UtcNow;
        _isKaraokeVocalRecordingPaused = true;
        _spectrumService.PauseProcessedAudioRecording();
        KaraokeVocalStatusText.Text = statusText;
        UpdateKaraokeTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void ResumeKaraokeVocalRecording(string statusText)
    {
        if (!_isKaraokeVocalRecording || !_isKaraokeVocalRecordingPaused)
        {
            return;
        }

        _karaokeVocalRecordingPausedDuration += DateTime.UtcNow - _karaokeVocalRecordingPausedAt;
        _isKaraokeVocalRecordingPaused = false;
        _spectrumService.ResumeProcessedAudioRecording();
        KaraokeVocalStatusText.Text = statusText;
        UpdateKaraokeTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void KaraokePauseVocalClicked(object sender, RoutedEventArgs e)
    {
        if (!_isKaraokeVocalRecording)
        {
            return;
        }

        if (_isKaraokeVocalRecordingPaused)
        {
            if (!_isKaraokeTrackPlaying && !StartKaraokePlayback())
            {
                KaraokeVocalStatusText.Text = "Backing track could not resume; recording is still paused.";
                return;
            }

            ResumeKaraokeVocalRecording("Karaoke recording resumed.");
        }
        else
        {
            if (_isKaraokeTrackPlaying)
            {
                PauseKaraokePlayback();
            }

            PauseKaraokeVocalRecording("Karaoke recording paused.");
        }

        UpdateKaraokeTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();
    }

    private void KaraokeStopVocalClicked(object sender, RoutedEventArgs e)
    {
        StopKaraokePlayback(clearTrack: false);
        ResetKaraokePlaybackProgress();
        StopKaraokeVocalRecording();
    }

    private void StopKaraokeVocalRecording()
    {
        if (!_isKaraokeVocalRecording)
        {
            return;
        }

        var elapsed = GetKaraokeVocalRecordingElapsed();
        var savedPath = _spectrumService.StopProcessedAudioRecording() ?? _karaokeVocalRecordingPath;
        _isKaraokeVocalRecording = false;
        _isKaraokeVocalRecordingPaused = false;
        _karaokeVocalRecordingPath = null;
        _karaokeVocalRecordingStartedAt = default;
        _karaokeVocalRecordingPausedAt = default;
        _karaokeVocalRecordingPausedDuration = TimeSpan.Zero;
        _lastKaraokeRecordingPath = savedPath;
        RefreshKaraokeRecordingFiles(savedPath);
        KaraokeVocalStatusText.Text = string.IsNullOrWhiteSpace(savedPath)
            ? $"Karaoke recording stopped at {FormatDuration(elapsed)}."
            : $"Saved karaoke recording {System.IO.Path.GetFileName(savedPath)} ({FormatDuration(elapsed)}).";
        KaraokeStatusText.Text = "Karaoke recording saved.";
        UpdateKaraokeTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();
        PersistAppState();
    }

    private TimeSpan GetKaraokeVocalRecordingElapsed()
    {
        if (!_isKaraokeVocalRecording)
        {
            return TimeSpan.Zero;
        }

        var now = _isKaraokeVocalRecordingPaused ? _karaokeVocalRecordingPausedAt : DateTime.UtcNow;
        return now - _karaokeVocalRecordingStartedAt - _karaokeVocalRecordingPausedDuration;
    }

    private string CreateKaraokeVocalRecordingPath(DateTime timestamp)
    {
        Directory.CreateDirectory(_karaokeRecordingFolder);
        var fileName = $"karaokeRecording_{timestamp:yyyy-MM-dd_HH-mm-ss}.wav";
        return System.IO.Path.Combine(_karaokeRecordingFolder, fileName);
    }

    private void PlayKaraokeRecordingFileClicked(object sender, RoutedEventArgs e)
    {
        if (_karaokeRecordingPlaybackOutput is not null)
        {
            StopKaraokeRecordingPlayback();
            KaraokeVocalStatusText.Text = "Karaoke playback stopped.";
            UpdateKaraokeTransportControls();
            return;
        }

        RefreshKaraokeRecordingFiles(GetSelectedKaraokeRecordingPath());
        var selectedPath = GetSelectedKaraokeRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            KaraokeVocalStatusText.Text = "Choose a saved karaoke recording above.";
            return;
        }

        StartKaraokeRecordingPlayback(selectedPath);
    }

    private void KaraokeRecordingFilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _lastKaraokeRecordingPath = GetSelectedKaraokeRecordingPath();
        PersistAppState();
        UpdateKaraokeTransportControls();
        UpdateSelectedKaraokeRecordingAnalysisStatus();
    }

    private void KaraokeRecordingFileDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        var selectedPath = GetSelectedKaraokeRecordingPath();
        if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
        {
            StartKaraokeRecordingPlayback(selectedPath);
        }
    }

    private void PlaySelectedKaraokeRecordingMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = GetSelectedKaraokeRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            KaraokeVocalStatusText.Text = "Choose a saved karaoke recording above.";
            return;
        }

        StartKaraokeRecordingPlayback(selectedPath);
    }

    private void ShowSelectedKaraokeRecordingPropertiesMenuClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = GetSelectedKaraokeRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            KaraokeVocalStatusText.Text = "Choose a saved karaoke recording above.";
            return;
        }

        try
        {
            ShowWindowsFileProperties(selectedPath);
            KaraokeVocalStatusText.Text = $"Opened properties for {System.IO.Path.GetFileName(selectedPath)}.";
        }
        catch (Exception ex)
        {
            KaraokeVocalStatusText.Text = $"Properties failed: {ex.Message}";
        }
    }

    private void ExportSelectedKaraokeRecordingMp3MenuClicked(object sender, RoutedEventArgs e)
    {
        ExportSelectedKaraokeRecording(AudioRecordingExportFormat.Mp3);
    }

    private void ExportSelectedKaraokeRecordingAacMenuClicked(object sender, RoutedEventArgs e)
    {
        ExportSelectedKaraokeRecording(AudioRecordingExportFormat.Aac);
    }

    private void ExportSelectedKaraokeRecordingWmaMenuClicked(object sender, RoutedEventArgs e)
    {
        ExportSelectedKaraokeRecording(AudioRecordingExportFormat.Wma);
    }

    private void ExportSelectedKaraokeRecording(AudioRecordingExportFormat format)
    {
        var selectedPath = GetSelectedKaraokeRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            KaraokeVocalStatusText.Text = "Choose a saved karaoke recording above.";
            return;
        }

        try
        {
            if (!TryChooseAudioExportPath(selectedPath, format, out var exportPath))
            {
                return;
            }

            var info = AudioRecordingExporter.GetFormatInfo(format);
            KaraokeVocalStatusText.Text = $"Exporting {info.DisplayName}: {System.IO.Path.GetFileName(selectedPath)}";
            AudioRecordingExporter.Export(selectedPath, exportPath, format);
            _lastKaraokeRecordingPath = exportPath;
            RefreshKaraokeRecordingFiles(exportPath);
            KaraokeVocalStatusText.Text = $"Exported {info.DisplayName}: {System.IO.Path.GetFileName(exportPath)}.";
        }
        catch (Exception ex)
        {
            KaraokeVocalStatusText.Text = $"Export failed: {ex.Message}";
        }

        UpdateKaraokeTransportControls();
    }

    private void DeleteSelectedKaraokeRecordingMenuClicked(object sender, RoutedEventArgs e)
    {
        DeleteSelectedKaraokeRecording();
    }

    private void OpenKaraokeRecordingLocationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_karaokeRecordingFolder);
            var selectedPath = GetSelectedKaraokeRecordingPath();
            if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
            {
                if (!PathSafety.IsRegularFileUnderFolder(selectedPath, _karaokeRecordingFolder, AudioRecordingCatalog.SupportedRecordingExtensions))
                {
                    KaraokeVocalStatusText.Text = "Open location blocked: selected karaoke recording is outside the recording folder.";
                    return;
                }

                PathSafety.RevealFileInExplorer(selectedPath);
                KaraokeVocalStatusText.Text = $"Opened location for {System.IO.Path.GetFileName(selectedPath)}.";
                return;
            }

            PathSafety.OpenFolderInExplorer(_karaokeRecordingFolder);
            KaraokeVocalStatusText.Text = "Opened karaoke recording folder.";
        }
        catch (Exception ex)
        {
            KaraokeVocalStatusText.Text = $"Open location failed: {ex.Message}";
        }

        UpdateKaraokeTransportControls();
    }

    private void DeleteSelectedKaraokeRecording()
    {
        var selectedPath = GetSelectedKaraokeRecordingPath();
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            KaraokeVocalStatusText.Text = "Choose a saved karaoke recording to delete.";
            return;
        }

        if (!PathSafety.IsRegularFileUnderFolder(selectedPath, _karaokeRecordingFolder, AudioRecordingCatalog.SupportedRecordingExtensions))
        {
            KaraokeVocalStatusText.Text = "Delete blocked: selected karaoke recording is outside the recording folder.";
            return;
        }

        var fileName = System.IO.Path.GetFileName(selectedPath);
        var result = MessageBox.Show(
            this,
            $"Delete this karaoke recording?{Environment.NewLine}{fileName}",
            "Delete karaoke recording",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (string.Equals(_karaokeRecordingPlaybackPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                StopKaraokeRecordingPlayback();
            }

            File.Delete(selectedPath);
            if (string.Equals(_lastKaraokeRecordingPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                _lastKaraokeRecordingPath = null;
            }

            RefreshKaraokeRecordingFiles();
            KaraokeVocalStatusText.Text = $"Deleted {fileName}.";
        }
        catch (Exception ex)
        {
            KaraokeVocalStatusText.Text = $"Delete failed: {ex.Message}";
        }

        UpdateKaraokeTransportControls();
    }

    private void StartKaraokeRecordingPlayback(string path)
    {
        try
        {
            StopKaraokeRecordingPlayback();

            var reader = new AudioFileReader(path);
            var output = CreateSelectedPlaybackOutput(reader.ToWaveProvider(), desiredLatency: 90);
            output.PlaybackStopped += KaraokeRecordingPlaybackStopped;

            _karaokeRecordingPlaybackReader = reader;
            _karaokeRecordingPlaybackOutput = output;
            _karaokeRecordingPlaybackPath = path;
            _lastKaraokeRecordingPath = path;
            _isStoppingKaraokeRecordingPlayback = false;
            output.Play();
            PersistAppState();

            KaraokeVocalStatusText.Text = $"Playing {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StopKaraokeRecordingPlayback();
            KaraokeVocalStatusText.Text = $"Playback failed: {ex.Message}";
        }

        UpdateKaraokeTransportControls();
    }

    private void KaraokeRecordingPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var path = _karaokeRecordingPlaybackPath;
            var wasStoppedByUser = _isStoppingKaraokeRecordingPlayback;
            StopKaraokeRecordingPlayback();
            if (e.Exception is not null)
            {
                KaraokeVocalStatusText.Text = $"Playback failed: {e.Exception.Message}";
            }
            else if (!wasStoppedByUser && !string.IsNullOrWhiteSpace(path))
            {
                KaraokeVocalStatusText.Text = $"Finished {System.IO.Path.GetFileName(path)}.";
            }

            UpdateKaraokeTransportControls();
        }, DispatcherPriority.Background);
    }

    private void StopKaraokeRecordingPlayback()
    {
        var output = _karaokeRecordingPlaybackOutput;
        var reader = _karaokeRecordingPlaybackReader;
        _karaokeRecordingPlaybackOutput = null;
        _karaokeRecordingPlaybackReader = null;
        _karaokeRecordingPlaybackPath = null;

        if (output is not null)
        {
            _isStoppingKaraokeRecordingPlayback = true;
            try
            {
                output.PlaybackStopped -= KaraokeRecordingPlaybackStopped;
                output.Stop();
            }
            catch
            {
            }

            try
            {
                output.Dispose();
            }
            catch
            {
            }
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
        }

        _isStoppingKaraokeRecordingPlayback = false;
    }

    private void UpdateKaraokeTrackUi()
    {
        if (KaraokeTrackTitleText is null)
        {
            return;
        }

        KaraokeTrackTitleText.Text = string.IsNullOrWhiteSpace(_karaokeTrackPath)
            ? "No backing track loaded"
            : _karaokeQueue.FirstOrDefault(item => string.Equals(item.Path, _karaokeTrackPath, StringComparison.OrdinalIgnoreCase)) is { } track
                ? $"{track.Artist} - {track.Name}"
                : System.IO.Path.GetFileName(_karaokeTrackPath);
        ResetKaraokePlaybackProgress();
    }

    private void UpdateKaraokeTransportControls()
    {
        if (KaraokePlayPauseButton is null || KaraokeRecordVocalButton is null)
        {
            return;
        }

        var hasTrack = !string.IsNullOrWhiteSpace(_karaokeTrackPath) && File.Exists(_karaokeTrackPath);
        var otherProcessedRecordingActive = _spectrumService.IsProcessedAudioRecording && !_isKaraokeVocalRecording;
        KaraokePlayPauseButton.IsEnabled = hasTrack;
        KaraokeStopButton.IsEnabled = hasTrack;
        KaraokePlayPauseButton.Content = _isKaraokeTrackPlaying ? "Pause" : "Play";
        KaraokeRecordVocalButton.IsEnabled = !_isKaraokeVocalRecording && !otherProcessedRecordingActive;
        if (KaraokePauseVocalButton is not null)
        {
            KaraokePauseVocalButton.IsEnabled = _isKaraokeVocalRecording;
            KaraokePauseVocalButton.Content = _isKaraokeVocalRecordingPaused ? "Resume" : "Pause";
        }

        KaraokeStopVocalButton.IsEnabled = _isKaraokeVocalRecording;
        if (KaraokePlayRecordingButton is not null)
        {
            KaraokePlayRecordingButton.IsEnabled = !_isKaraokeVocalRecording && !otherProcessedRecordingActive;
            KaraokePlayRecordingButton.Content = _karaokeRecordingPlaybackOutput is null ? "Play File" : "Stop Play";
        }

        if (KaraokeOpenRecordingLocationButton is not null)
        {
            KaraokeOpenRecordingLocationButton.IsEnabled = !_isKaraokeVocalRecording && !otherProcessedRecordingActive;
        }

        UpdateKaraokeQueueControls();
    }

    private void StartSessionPlayback(string path)
    {
        try
        {
            StopSessionPlayback();
            if (_isCameraEnabled)
            {
                _isCameraEnabled = false;
                UpdateCameraEnabledState();
            }

            StopTextureNativeCameraStream();
            StopPreviewServices();
            _isDirectShowPreviewActive = false;
            CameraPreviewImage.Visibility = Visibility.Collapsed;
            CameraPlaceholder.Visibility = Visibility.Collapsed;
            SessionPlaybackBar.Visibility = Visibility.Visible;
            ResetSessionPlaybackProgress();
            _sessionPlaybackPath = path;
            _lastSessionRecordingPath = path;
            if (TryStartDx12SessionPlayback(path, out var dx12Failure))
            {
                _isSessionDx12PlaybackActive = true;
                _isSessionMediaElementFallbackActive = false;
                _isSessionPlaybackPlaying = true;
                _isSessionPlaybackEnded = false;
                _sessionPlaybackPositionTimer.Start();
                PersistAppState();
                RecordingStatusText.Text = FormatSessionPlaybackStatus(path, "through DX12 session playback");
            }
            else
            {
                StartSessionMediaElementFallbackPlayback(path);
                if (!string.IsNullOrWhiteSpace(dx12Failure))
                {
                    RecordingStatusText.Text = $"Playing {System.IO.Path.GetFileName(path)} with Windows media fallback; DX12 unavailable: {dx12Failure}";
                }
            }
        }
        catch (Exception ex)
        {
            StopSessionPlayback();
            RecordingStatusText.Text = $"Session playback failed: {ex.Message}";
        }

        UpdateSessionPlaybackTransportControls();
    }

    private bool TryStartDx12SessionPlayback(string path, out string? failure)
    {
        failure = null;
        try
        {
            var host = new Direct3D12PreviewHost
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            host.StatusChanged += SessionDx12PlaybackHostStatusChanged;
            _sessionDx12PlaybackHost = host;
            SessionDx12PlaybackHostPanel.Children.Clear();
            SessionDx12PlaybackHostPanel.Children.Add(host);
            SessionDx12PlaybackHostPanel.Visibility = Visibility.Visible;

            var service = new MediaFoundationFilePlaybackService();
            service.FrameAvailable += SessionVideoPlaybackFrameAvailable;
            service.PlaybackEnded += SessionVideoPlaybackEnded;
            service.PlaybackFailed += SessionVideoPlaybackFailed;
            service.StatusChanged += SessionVideoPlaybackStatusChanged;
            _sessionVideoPlaybackService = service;

            if (!service.Start(path))
            {
                failure = "Media Foundation video reader did not start";
                StopSessionDx12Playback();
                return false;
            }

            if (!TryStartSessionAudioPlayback(path, out var audioFailure))
            {
                failure = $"audio reader did not start ({audioFailure})";
                StopSessionDx12Playback();
                return false;
            }

            SessionPlaybackElement.Stop();
            SessionPlaybackElement.Source = null;
            SessionPlaybackElement.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            StopSessionDx12Playback();
            return false;
        }
    }

    private bool TryStartSessionAudioPlayback(string path, out string? failure)
    {
        failure = null;
        try
        {
            var audioPath = SessionPlaybackAudioResolver.ResolveAudioPlaybackPath(path);
            var reader = new AudioFileReader(audioPath);
            var output = CreateSelectedPlaybackOutput(reader.ToWaveProvider(), desiredLatency: 90);
            output.PlaybackStopped += SessionAudioPlaybackStopped;
            _sessionAudioPlaybackReader = reader;
            _sessionAudioPlaybackPath = audioPath;
            _sessionAudioPlaybackOutput = output;
            _isStoppingSessionAudioPlayback = false;
            output.Play();
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            StopSessionAudioPlayback();
            return false;
        }
    }

    private string FormatSessionPlaybackStatus(string videoPath, string route)
    {
        var videoName = System.IO.Path.GetFileName(videoPath);
        if (!string.IsNullOrWhiteSpace(_sessionAudioPlaybackPath)
            && !string.Equals(_sessionAudioPlaybackPath, videoPath, StringComparison.OrdinalIgnoreCase))
        {
            return $"Playing {videoName} {route} with {System.IO.Path.GetFileName(_sessionAudioPlaybackPath)}.";
        }

        return $"Playing {videoName} {route}.";
    }

    private void StartSessionMediaElementFallbackPlayback(string path)
    {
        StopSessionDx12Playback();
        StopSessionAudioPlayback();
        SessionDx12PlaybackHostPanel.Visibility = Visibility.Collapsed;
        SessionPlaybackElement.Opacity = 1d;
        SessionPlaybackElement.Source = new Uri(path);
        SessionPlaybackElement.Visibility = Visibility.Visible;
        CameraPlaceholder.Visibility = Visibility.Collapsed;
        SessionPlaybackBar.Visibility = Visibility.Visible;
        _startSessionPlaybackFromBeginning = true;
        _isSessionDx12PlaybackActive = false;
        _isSessionMediaElementFallbackActive = true;
        SessionPlaybackElement.Play();
        _ = TryStartSessionSidecarAudioPlayback(path, out _);
        _isSessionPlaybackPlaying = true;
        _isSessionPlaybackEnded = false;
        _sessionPlaybackPositionTimer.Start();
        PersistAppState();
        RecordingStatusText.Text = FormatSessionPlaybackStatus(path, "with Windows media fallback");
    }

    private bool TryStartSessionSidecarAudioPlayback(string path, out string? failure)
    {
        var audioPath = SessionPlaybackAudioResolver.ResolveAudioPlaybackPath(path);
        if (string.Equals(audioPath, path, StringComparison.OrdinalIgnoreCase))
        {
            failure = "no session sidecar audio file was found";
            return false;
        }

        return TryStartSessionAudioPlayback(path, out failure);
    }

    private void StopSessionPlayback()
    {
        _sessionPlaybackPositionTimer.Stop();
        _isScrubbingSessionPlayback = false;
        _isSessionPlaybackPlaying = false;
        _isSessionPlaybackEnded = false;
        _startSessionPlaybackFromBeginning = false;
        _isSessionDx12PlaybackActive = false;
        _isSessionMediaElementFallbackActive = false;
        if (SessionPlaybackSeekSlider is not null && SessionPlaybackSeekSlider.IsMouseCaptured)
        {
            SessionPlaybackSeekSlider.ReleaseMouseCapture();
        }

        StopSessionDx12Playback();
        StopSessionAudioPlayback();

        try
        {
            SessionPlaybackElement.Stop();
            SessionPlaybackElement.Source = null;
            SessionPlaybackElement.Opacity = 1d;
            SessionPlaybackElement.Visibility = Visibility.Collapsed;
        }
        catch
        {
        }

        _sessionPlaybackPath = null;
        ResetSessionPlaybackProgress();
        if (SessionPlaybackBar is not null)
        {
            SessionPlaybackBar.Visibility = Visibility.Collapsed;
        }

        if (!_isCameraEnabled && CameraPlaceholder is not null)
        {
            CameraPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void StopSessionDx12Playback()
    {
        var service = _sessionVideoPlaybackService;
        _sessionVideoPlaybackService = null;
        if (service is not null)
        {
            service.FrameAvailable -= SessionVideoPlaybackFrameAvailable;
            service.PlaybackEnded -= SessionVideoPlaybackEnded;
            service.PlaybackFailed -= SessionVideoPlaybackFailed;
            service.StatusChanged -= SessionVideoPlaybackStatusChanged;
            service.Dispose();
        }

        var host = _sessionDx12PlaybackHost;
        _sessionDx12PlaybackHost = null;
        if (host is not null)
        {
            host.StatusChanged -= SessionDx12PlaybackHostStatusChanged;
            SessionDx12PlaybackHostPanel.Children.Remove(host);
            host.Dispose();
        }

        if (SessionDx12PlaybackHostPanel is not null)
        {
            SessionDx12PlaybackHostPanel.Children.Clear();
            SessionDx12PlaybackHostPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void StopSessionAudioPlayback()
    {
        var output = _sessionAudioPlaybackOutput;
        var reader = _sessionAudioPlaybackReader;
        _sessionAudioPlaybackOutput = null;
        _sessionAudioPlaybackReader = null;
        _sessionAudioPlaybackPath = null;

        if (output is not null)
        {
            _isStoppingSessionAudioPlayback = true;
            try
            {
                output.PlaybackStopped -= SessionAudioPlaybackStopped;
                output.Stop();
            }
            catch
            {
            }

            try
            {
                output.Dispose();
            }
            catch
            {
            }
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
        }

        _isStoppingSessionAudioPlayback = false;
    }

    private void SessionVideoPlaybackFrameAvailable(object? sender, CameraFrame frame)
    {
        if (_isClosing)
        {
            return;
        }

        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (!_isSessionDx12PlaybackActive || _sessionDx12PlaybackHost is null)
            {
                return;
            }

            if (frame.HasNv12)
            {
                _sessionDx12PlaybackHost.RenderBgraFrame(frame, DateTime.UtcNow.Ticks);
                return;
            }

            if (frame.HasBgra)
            {
                _sessionDx12PlaybackHost.RenderBgraFrame(frame, DateTime.UtcNow.Ticks);
            }
        }), DispatcherPriority.Background);
    }

    private void SessionVideoPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            var path = _sessionPlaybackPath;
            _sessionPlaybackPositionTimer.Stop();
            StopSessionAudioPlayback();
            _isScrubbingSessionPlayback = false;
            _isSessionPlaybackPlaying = false;
            _isSessionPlaybackEnded = true;
            UpdateSessionPlaybackProgress();
            if (!string.IsNullOrWhiteSpace(path))
            {
                RecordingStatusText.Text = $"Finished {System.IO.Path.GetFileName(path)}.";
            }

            UpdateSessionPlaybackTransportControls();
        }), DispatcherPriority.Normal);
    }

    private void SessionVideoPlaybackFailed(object? sender, string message)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            var path = _sessionPlaybackPath;
            StopSessionPlayback();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _sessionPlaybackPath = path;
                _lastSessionRecordingPath = path;
                ResetSessionPlaybackProgress();
                StartSessionMediaElementFallbackPlayback(path);
                RecordingStatusText.Text = $"DX12 session playback failed; using Windows media fallback: {message}";
            }
            else
            {
                RecordingStatusText.Text = $"Session playback failed: {message}";
            }

            UpdateSessionPlaybackTransportControls();
        }), DispatcherPriority.Normal);
    }

    private void SessionVideoPlaybackStatusChanged(object? sender, string message)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (_isSessionDx12PlaybackActive && !string.IsNullOrWhiteSpace(message))
            {
                CameraPreviewStatusText.Text = message;
            }
        }), DispatcherPriority.Background);
    }

    private void SessionDx12PlaybackHostStatusChanged(object? sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (_isSessionDx12PlaybackActive)
            {
                CameraPreviewStatusText.Text = message;
            }
        }), DispatcherPriority.Background);
    }

    private void SessionAudioPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (_isStoppingSessionAudioPlayback)
            {
                return;
            }

            if (e.Exception is not null)
            {
                var path = _sessionPlaybackPath;
                var canUseMediaFallback = _isSessionDx12PlaybackActive;
                StopSessionPlayback();
                RecordingStatusText.Text = $"Session audio playback failed: {e.Exception.Message}";
                if (canUseMediaFallback && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    StartSessionMediaElementFallbackPlayback(path);
                    RecordingStatusText.Text = $"Session audio playback failed; using Windows media fallback: {e.Exception.Message}";
                }
            }
        }), DispatcherPriority.Normal);
    }

    private void SessionPlaybackOpened(object sender, RoutedEventArgs e)
    {
        if (!_isSessionMediaElementFallbackActive)
        {
            return;
        }

        if (_startSessionPlaybackFromBeginning)
        {
            _startSessionPlaybackFromBeginning = false;
            SessionPlaybackElement.Position = TimeSpan.Zero;
        }

        UpdateSessionPlaybackProgress();
    }

    private void SessionPlaybackBarPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sessionPlaybackPath))
        {
            return;
        }

        if (_isSessionPlaybackPlaying)
        {
            PauseLoadedSessionPlayback();
            RecordingStatusText.Text = $"Paused {System.IO.Path.GetFileName(_sessionPlaybackPath)}.";
        }
        else
        {
            PlayLoadedSessionPlayback(restartIfAtEnd: true);
            RecordingStatusText.Text = $"Playing {System.IO.Path.GetFileName(_sessionPlaybackPath)}.";
        }

        UpdateSessionPlaybackTransportControls();
    }

    private void PlayLoadedSessionPlayback(bool restartIfAtEnd)
    {
        if (string.IsNullOrWhiteSpace(_sessionPlaybackPath))
        {
            return;
        }

        var duration = GetSessionPlaybackDuration();
        if (_isSessionDx12PlaybackActive)
        {
            if (_isSessionPlaybackEnded)
            {
                var path = _sessionPlaybackPath;
                StartSessionPlayback(path);
                return;
            }

            if (restartIfAtEnd
                && duration > TimeSpan.Zero
                && GetSessionPlaybackPosition() >= duration - TimeSpan.FromMilliseconds(250))
            {
                SeekSessionPlaybackTo(TimeSpan.Zero);
            }

            _sessionVideoPlaybackService?.Play();
            _sessionAudioPlaybackOutput?.Play();
            SessionDx12PlaybackHostPanel.Visibility = Visibility.Visible;
            CameraPlaceholder.Visibility = Visibility.Collapsed;
            SessionPlaybackBar.Visibility = Visibility.Visible;
            _isSessionPlaybackPlaying = true;
            _isSessionPlaybackEnded = false;
            _sessionPlaybackPositionTimer.Start();
            UpdateSessionPlaybackBarControls();
            return;
        }

        if (restartIfAtEnd
            && duration > TimeSpan.Zero
            && SessionPlaybackElement.Position >= duration - TimeSpan.FromMilliseconds(250))
        {
            SeekSessionPlaybackTo(TimeSpan.Zero);
        }

        SessionPlaybackElement.Visibility = Visibility.Visible;
        CameraPlaceholder.Visibility = Visibility.Collapsed;
        SessionPlaybackBar.Visibility = Visibility.Visible;
        SessionPlaybackElement.Play();
        _sessionAudioPlaybackOutput?.Play();
        _isSessionPlaybackPlaying = true;
        _isSessionPlaybackEnded = false;
        _sessionPlaybackPositionTimer.Start();
        UpdateSessionPlaybackBarControls();
    }

    private void PauseLoadedSessionPlayback()
    {
        if (string.IsNullOrWhiteSpace(_sessionPlaybackPath))
        {
            return;
        }

        if (_isSessionDx12PlaybackActive)
        {
            _sessionVideoPlaybackService?.Pause();
            _sessionAudioPlaybackOutput?.Pause();
            _isSessionPlaybackPlaying = false;
            _isSessionPlaybackEnded = false;
            _sessionPlaybackPositionTimer.Stop();
            UpdateSessionPlaybackProgress();
            UpdateSessionPlaybackBarControls();
            return;
        }

        SessionPlaybackElement.Pause();
        _sessionAudioPlaybackOutput?.Pause();
        _isSessionPlaybackPlaying = false;
        _isSessionPlaybackEnded = false;
        _sessionPlaybackPositionTimer.Stop();
        UpdateSessionPlaybackProgress();
        UpdateSessionPlaybackBarControls();
    }

    private void SessionPlaybackPositionTimerTick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sessionPlaybackPath))
        {
            _sessionPlaybackPositionTimer.Stop();
            return;
        }

        if (!_isScrubbingSessionPlayback)
        {
            UpdateSessionPlaybackProgress();
        }
    }

    private void UpdateSessionPlaybackProgress()
    {
        if (SessionPlaybackSeekSlider is null)
        {
            return;
        }

        var position = GetSessionPlaybackPosition();
        var duration = GetSessionPlaybackDuration();
        var maxSeconds = Math.Max(1d, duration.TotalSeconds);
        var positionSeconds = Math.Clamp(position.TotalSeconds, 0d, maxSeconds);

        _isUpdatingSessionPlaybackPosition = true;
        try
        {
            if (Math.Abs(SessionPlaybackSeekSlider.Maximum - maxSeconds) > 0.001d)
            {
                SessionPlaybackSeekSlider.Maximum = maxSeconds;
            }

            if (Math.Abs(SessionPlaybackSeekSlider.Value - positionSeconds) > 0.03d)
            {
                SessionPlaybackSeekSlider.Value = positionSeconds;
            }
        }
        finally
        {
            _isUpdatingSessionPlaybackPosition = false;
        }

        var positionText = FormatDuration(TimeSpan.FromSeconds(positionSeconds));
        if (!string.Equals(SessionPlaybackPositionText.Text, positionText, StringComparison.Ordinal))
        {
            SessionPlaybackPositionText.Text = positionText;
        }

        var durationText = duration > TimeSpan.Zero
            ? FormatDuration(duration)
            : "00:00:00";
        if (!string.Equals(SessionPlaybackDurationText.Text, durationText, StringComparison.Ordinal))
        {
            SessionPlaybackDurationText.Text = durationText;
        }
    }

    private void ResetSessionPlaybackProgress()
    {
        if (SessionPlaybackSeekSlider is null)
        {
            return;
        }

        _isUpdatingSessionPlaybackPosition = true;
        try
        {
            SessionPlaybackSeekSlider.Minimum = 0d;
            SessionPlaybackSeekSlider.Maximum = 1d;
            SessionPlaybackSeekSlider.Value = 0d;
        }
        finally
        {
            _isUpdatingSessionPlaybackPosition = false;
        }

        SessionPlaybackPositionText.Text = "00:00:00";
        SessionPlaybackDurationText.Text = "00:00:00";
        UpdateSessionPlaybackBarControls();
    }

    private TimeSpan GetSessionPlaybackDuration()
    {
        if (_isSessionDx12PlaybackActive)
        {
            var serviceDuration = _sessionVideoPlaybackService?.Duration ?? TimeSpan.Zero;
            if (serviceDuration > TimeSpan.Zero)
            {
                return serviceDuration;
            }

            var audioDuration = _sessionAudioPlaybackReader?.TotalTime ?? TimeSpan.Zero;
            if (audioDuration > TimeSpan.Zero)
            {
                return audioDuration;
            }
        }

        return SessionPlaybackElement.NaturalDuration.HasTimeSpan
            ? SessionPlaybackElement.NaturalDuration.TimeSpan
            : _sessionAudioPlaybackReader?.TotalTime ?? TimeSpan.Zero;
    }

    private TimeSpan GetSessionPlaybackPosition()
    {
        if (_isSessionDx12PlaybackActive)
        {
            var videoPosition = _sessionVideoPlaybackService?.Position ?? TimeSpan.Zero;
            if (videoPosition > TimeSpan.Zero || _sessionAudioPlaybackReader is null)
            {
                return videoPosition;
            }

            return _sessionAudioPlaybackReader.CurrentTime;
        }

        return SessionPlaybackElement.Position > TimeSpan.Zero || _sessionAudioPlaybackReader is null
            ? SessionPlaybackElement.Position
            : _sessionAudioPlaybackReader.CurrentTime;
    }

    private bool CanSeekSessionPlayback()
    {
        return !string.IsNullOrWhiteSpace(_sessionPlaybackPath)
            && GetSessionPlaybackDuration() > TimeSpan.Zero;
    }

    private void SeekSessionPlaybackFromPoint(Point point)
    {
        if (!CanSeekSessionPlayback() || SessionPlaybackSeekSlider.ActualWidth <= 0d)
        {
            return;
        }

        var duration = GetSessionPlaybackDuration();
        var ratio = Math.Clamp(point.X / SessionPlaybackSeekSlider.ActualWidth, 0d, 1d);
        SeekSessionPlaybackTo(TimeSpan.FromSeconds(duration.TotalSeconds * ratio));
    }

    private void SeekSessionPlaybackTo(TimeSpan position)
    {
        if (!CanSeekSessionPlayback())
        {
            return;
        }

        var duration = GetSessionPlaybackDuration();
        var clampedSeconds = Math.Clamp(position.TotalSeconds, 0d, duration.TotalSeconds);
        var clamped = TimeSpan.FromSeconds(clampedSeconds);
        if (_isSessionDx12PlaybackActive)
        {
            _sessionVideoPlaybackService?.Seek(clamped);
            if (_sessionAudioPlaybackReader is not null)
            {
                try
                {
                    _sessionAudioPlaybackReader.CurrentTime = clamped;
                }
                catch
                {
                }
            }
        }
        else
        {
            SessionPlaybackElement.Position = clamped;
            if (_sessionAudioPlaybackReader is not null)
            {
                try
                {
                    _sessionAudioPlaybackReader.CurrentTime = clamped;
                }
                catch
                {
                }
            }
        }

        _isUpdatingSessionPlaybackPosition = true;
        try
        {
            SessionPlaybackSeekSlider.Maximum = Math.Max(1d, duration.TotalSeconds);
            SessionPlaybackSeekSlider.Value = clampedSeconds;
        }
        finally
        {
            _isUpdatingSessionPlaybackPosition = false;
        }

        SessionPlaybackPositionText.Text = FormatDuration(clamped);
        SessionPlaybackDurationText.Text = FormatDuration(duration);
        UpdateSessionPlaybackBarControls();
    }

    private void SessionPlaybackSeekSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanSeekSessionPlayback())
        {
            return;
        }

        _isScrubbingSessionPlayback = true;
        SessionPlaybackSeekSlider.CaptureMouse();
        SeekSessionPlaybackFromPoint(e.GetPosition(SessionPlaybackSeekSlider));
        e.Handled = true;
    }

    private void SessionPlaybackSeekSliderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScrubbingSessionPlayback || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        SeekSessionPlaybackFromPoint(e.GetPosition(SessionPlaybackSeekSlider));
        e.Handled = true;
    }

    private void SessionPlaybackSeekSliderPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScrubbingSessionPlayback)
        {
            return;
        }

        SeekSessionPlaybackFromPoint(e.GetPosition(SessionPlaybackSeekSlider));
        _isScrubbingSessionPlayback = false;
        if (SessionPlaybackSeekSlider.IsMouseCaptured)
        {
            SessionPlaybackSeekSlider.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private void SessionPlaybackSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSessionPlaybackPosition || !CanSeekSessionPlayback())
        {
            return;
        }

        SeekSessionPlaybackTo(TimeSpan.FromSeconds(e.NewValue));
    }

    private void SessionPlaybackEnded(object sender, RoutedEventArgs e)
    {
        if (!_isSessionMediaElementFallbackActive)
        {
            return;
        }

        var path = _sessionPlaybackPath;
        _sessionPlaybackPositionTimer.Stop();
        _isScrubbingSessionPlayback = false;
        _isSessionPlaybackPlaying = false;
        _isSessionPlaybackEnded = true;
        StopSessionAudioPlayback();
        var duration = GetSessionPlaybackDuration();
        if (duration > TimeSpan.Zero)
        {
            SeekSessionPlaybackTo(duration);
        }
        else
        {
            UpdateSessionPlaybackProgress();
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            RecordingStatusText.Text = $"Finished {System.IO.Path.GetFileName(path)}.";
        }

        UpdateSessionPlaybackTransportControls();
    }

    private void SessionPlaybackFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (!_isSessionMediaElementFallbackActive)
        {
            return;
        }

        StopSessionPlayback();
        RecordingStatusText.Text = $"Session playback failed: {e.ErrorException.Message}";
        UpdateSessionPlaybackTransportControls();
    }

    private void UpdateSessionPlaybackTransportControls()
    {
        if (SessionPlayFileButton is null)
        {
            return;
        }

        SessionPlayFileButton.IsEnabled = !_isRecordingSession;
        SessionPlayFileButton.Content = string.IsNullOrWhiteSpace(_sessionPlaybackPath)
            ? "Play File"
            : _isSessionPlaybackEnded
                ? "Play Again"
                : "Stop Play";
        SessionOpenLocationButton.IsEnabled = !_isRecordingSession;
        UpdateSessionPlaybackBarControls();
    }

    private void UpdateSessionPlaybackBarControls()
    {
        if (SessionPlaybackBarPlayPauseButton is null)
        {
            return;
        }

        SessionPlaybackBarPlayPauseButton.IsEnabled = !_isRecordingSession
            && !string.IsNullOrWhiteSpace(_sessionPlaybackPath);
        SessionPlaybackBarPlayPauseButton.Content = _isSessionPlaybackPlaying ? "Pause" : "Play";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void RenderSpectrum(SpectrumFrame frame)
    {
        var width = Math.Max(1d, SpectrumCanvas.ActualWidth);
        var height = Math.Max(1d, SpectrumCanvas.ActualHeight);
        var topInset = 86d;
        var bottomInset = GetAnalyzerBottomInset();
        var usableHeight = Math.Max(1d, height - topInset - bottomInset);
        var graphTop = topInset;
        var graphBottom = graphTop + usableHeight;

        EnsureGraphGrid(width, graphTop, graphBottom);
        UpdateEqualizerHoverRegion(width, graphTop, graphBottom);

        var livePoints = new PointCollection();
        var frameCeiling = 0.08d;
        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.Magnitudes[i]));
        }

        _visualCeiling = frameCeiling > _visualCeiling
            ? Ease(_visualCeiling, frameCeiling, 0.08d)
            : Ease(_visualCeiling, frameCeiling, 0.015d);
        EnsureRenderBuffers(frame.Magnitudes.Length, 0);
        EnsureInputRenderBuffers(frame.Input1Magnitudes.Length, frame.Input2Magnitudes.Length);

        var analyzerSmoothingCoefficient = GetAnalyzerSmoothingCoefficient();
        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            var shaped = NormalizeForDisplay(ShapeMagnitude(frame.Magnitudes[i]));
            _renderedMagnitudes[i] = Ease(_renderedMagnitudes[i], shaped, analyzerSmoothingCoefficient);

            var x = frame.Magnitudes.Length == 1
                ? 0d
                : i / (double)(frame.Magnitudes.Length - 1) * width;
            livePoints.Add(new Point(x, graphBottom - usableHeight * _renderedMagnitudes[i]));
        }

        _input1Trace.Data = Geometry.Empty;
        _input2Trace.Data = Geometry.Empty;
        _averageTrace.Data = Geometry.Empty;
        _averageTrace.Visibility = Visibility.Collapsed;
        _liveTrace.Visibility = Visibility.Visible;
        _input1Trace.Visibility = Visibility.Collapsed;
        _input2Trace.Visibility = Visibility.Collapsed;
        _liveTrace.Data = CreateSmoothedGeometry(livePoints);
    }

    private void RenderMixingMicSpectrum(SpectrumFrame frame)
    {
        var width = Math.Max(1d, MixingMicSpectrumCanvas.ActualWidth);
        var height = Math.Max(1d, MixingMicSpectrumCanvas.ActualHeight);
        var graphTop = 18d;
        var graphBottom = Math.Max(graphTop + 1d, height - 18d);
        var usableHeight = Math.Max(1d, graphBottom - graphTop);
        EnsureMixingSpectrumGrid(width, graphTop, graphBottom);

        var frameCeiling = 0.08d;
        foreach (var magnitude in frame.Magnitudes)
        {
            frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(magnitude));
        }

        foreach (var magnitude in frame.RawMagnitudes)
        {
            frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(magnitude));
        }

        _mixingVisualCeiling = frameCeiling > _mixingVisualCeiling
            ? Ease(_mixingVisualCeiling, frameCeiling, 0.1d)
            : Ease(_mixingVisualCeiling, frameCeiling, 0.02d);

        foreach (var trace in _mixingMicSpectrumTraces)
        {
            trace.Visibility = Visibility.Collapsed;
        }

        if (frame.Magnitudes.Length == 0 && frame.RawMagnitudes.Length == 0)
        {
            return;
        }

        RenderMixingSpectrumTrace(
            traceIndex: 0,
            magnitudes: frame.Magnitudes,
            peakLevel: frame.PeakLevel,
            stroke: _mixingProgramSpectrumBrush,
            strokeThickness: 2.6d,
            width,
            graphBottom,
            usableHeight);
        RenderMixingSpectrumTrace(
            traceIndex: 1,
            magnitudes: frame.RawMagnitudes,
            peakLevel: frame.RawPeakLevel,
            stroke: _mixingRecordingSpectrumBrush,
            strokeThickness: 2.1d,
            width,
            graphBottom,
            usableHeight);
    }

    private void RenderMixingSpectrumTrace(
        int traceIndex,
        double[] magnitudes,
        double peakLevel,
        Brush stroke,
        double strokeThickness,
        double width,
        double graphBottom,
        double usableHeight)
    {
        if (magnitudes.Length == 0)
        {
            return;
        }

        EnsureMixingMicRenderBuffer(traceIndex, magnitudes.Length);
        var renderedMagnitudes = _renderedMixingMicMagnitudes[traceIndex];
        var points = new PointCollection();
        var smoothing = GetAnalyzerSmoothingCoefficient();
        for (var i = 0; i < magnitudes.Length; i++)
        {
            var shaped = NormalizeForDisplay(ShapeMagnitude(magnitudes[i]), _mixingVisualCeiling);
            renderedMagnitudes[i] = Ease(renderedMagnitudes[i], shaped, smoothing);
            var x = magnitudes.Length == 1
                ? 0d
                : i / (double)(magnitudes.Length - 1) * width;
            points.Add(new Point(x, graphBottom - usableHeight * renderedMagnitudes[i]));
        }

        var outputTrace = _mixingMicSpectrumTraces[traceIndex];
        outputTrace.Stroke = stroke;
        outputTrace.StrokeThickness = strokeThickness;
        outputTrace.Data = CreateSmoothedGeometry(points);
        outputTrace.Opacity = peakLevel <= 0.0001d ? 0.34d : 0.98d;
        outputTrace.Visibility = Visibility.Visible;
    }

    private void UpdateMixerChannelMeters(SpectrumFrame frame)
    {
        var updatedChannels = new HashSet<int>();
        var hasSolo = _micChannels.Any(channel => channel.SelectedDevice is not null && channel.IsEnabled && channel.IsSoloed);
        foreach (var line in frame.MicrophoneLines)
        {
            var channel = FindMicChannel(line.ChannelNumber);
            if (channel is null)
            {
                continue;
            }

            if (!IsMixerChannelAudibleInProgram(channel, hasSolo))
            {
                channel.ClearLevelMeter();
                channel.UpdateSyncStatus(
                    line.SyncBufferedMilliseconds,
                    line.SyncTargetLatencyMilliseconds,
                    line.SyncUnderflowCount,
                    line.SyncDriftTrimCount);
                updatedChannels.Add(line.ChannelNumber);
                continue;
            }

            var meteredPeak = line.MeteredPeakLevel > 0d
                ? line.MeteredPeakLevel
                : line.PeakLevel;
            channel.UpdateLevelMeter(meteredPeak, line.RmsLevel);
            channel.UpdateSyncStatus(
                line.SyncBufferedMilliseconds,
                line.SyncTargetLatencyMilliseconds,
                line.SyncUnderflowCount,
                line.SyncDriftTrimCount);
            updatedChannels.Add(line.ChannelNumber);
        }

        if (frame.MicrophoneLines.Count == 0 && _activeMicChannel is not null)
        {
            _activeMicChannel.UpdateLevelMeter(frame.RawPeakLevel);
            updatedChannels.Add(_activeMicChannel.ChannelNumber);
        }

        foreach (var channel in _micChannels)
        {
            if (!updatedChannels.Contains(channel.ChannelNumber))
            {
                channel.UpdateLevelMeter(0d);
            }
        }
    }

    private static bool IsMixerChannelAudibleInProgram(MicChannelStrip channel, bool hasSolo)
    {
        return channel.SelectedDevice is not null
            && channel.VolumePercent > 0.1d
            && LiveMixAudibility.IsAudible(channel.IsEnabled, channel.IsMuted, channel.IsSoloed, hasSolo);
    }

    private void UpdateMixBusStatus(SpectrumFrame frame)
    {
        if (MixBusStatusText is null)
        {
            return;
        }

        var enabledChannels = _micChannels
            .Where(channel => channel.SelectedDevice is not null)
            .ToList();
        var soloedChannels = enabledChannels
            .Where(channel => channel.IsSoloed)
            .ToList();
        var candidateAudibleChannels = soloedChannels.Count > 0 ? soloedChannels : enabledChannels;
        var audibleChannels = candidateAudibleChannels
            .Where(channel => !channel.IsMuted && channel.VolumePercent > 0.1d)
            .ToList();
        var hasInput = frame.MicrophoneLines.Any(line => line.RawPeakLevel > 0.001d)
            || frame.RawPeakLevel > 0.001d;

        string text;
        Brush foreground;
        if (!_spectrumService.IsRunning)
        {
            text = "Bus: not listening";
            foreground = _meterTextMutedBrush;
        }
        else if (enabledChannels.Count == 0)
        {
            text = "Bus: no mics enabled";
            foreground = _meterWarnBrush;
        }
        else if (!hasInput)
        {
            text = "Bus: waiting for mic input";
            foreground = _meterTextMutedBrush;
        }
        else if (audibleChannels.Count == 0)
        {
            text = "Bus: input live, program muted";
            foreground = _meterWarnBrush;
        }
        else if (frame.PeakLevel < 0.001d)
        {
            text = "Bus: input live, program quiet";
            foreground = _meterWarnBrush;
        }
        else
        {
            var limiterReductionDb = frame.Telemetry.MasterLimiterReductionDb;
            var limiterText = limiterReductionDb >= 0.1d
                ? $", limiter {limiterReductionDb:0.0} dB GR"
                : string.Empty;
            text = $"Bus: program active ({audibleChannels.Count} mic{(audibleChannels.Count == 1 ? string.Empty : "s")}{limiterText})";
            foreground = _meterGoodBrush;
        }

        if (!string.Equals(MixBusStatusText.Text, text, StringComparison.Ordinal))
        {
            MixBusStatusText.Text = text;
        }

        if (!ReferenceEquals(MixBusStatusText.Foreground, foreground))
        {
            MixBusStatusText.Foreground = foreground;
        }
    }

    private void UpdateMixingMicLegend()
    {
        if (MixingMicLegendPanel is null)
        {
            return;
        }

        MixingMicLegendPanel.Children.Clear();
        AddMixingLegendItem(
            "Program mix",
            _mixingProgramSpectrumBrush);
        if (_audioRecordingSource == ProcessedRecordingSource.ProgramMix)
        {
            return;
        }

        AddMixingLegendItem(
            $"Recording: {GetAudioRecordingSourceLabel()}",
            _mixingRecordingSpectrumBrush);
    }

    private void AddMixingLegendItem(string text, Brush brush)
    {
        var item = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        item.Children.Add(new Rectangle
        {
            Width = 22,
            Height = 3,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Fill = brush,
            Margin = new Thickness(0, 0, 6, 0)
        });
        item.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        MixingMicLegendPanel.Children.Add(item);
    }

    private double GetAnalyzerSmoothingCoefficient()
    {
        var smoothingPercent = _activeMicChannel?.AnalyzerSmoothing ?? DefaultAnalyzerSmoothingPercent;
        return Math.Clamp(0.36d - smoothingPercent * 0.0031d, 0.05d, 0.36d);
    }

    private double GetAnalyzerBottomInset()
    {
        var faceplateHeight = EqualizerFaceplate?.ActualHeight ?? 0d;
        return Math.Max(270d, faceplateHeight + 38d);
    }

    private void UpdateInputCoach(double rawPeakLevel)
    {
        _displayInputPeak = rawPeakLevel > _displayInputPeak
            ? Ease(_displayInputPeak, rawPeakLevel, 0.35d)
            : Ease(_displayInputPeak, rawPeakLevel, 0.06d);

        var now = Stopwatch.GetTimestamp();
        if (_lastInputLevelDisplayTimestamp != 0)
        {
            var elapsedMs = (now - _lastInputLevelDisplayTimestamp) * 1000d / Stopwatch.Frequency;
            if (elapsedMs < 33d)
            {
                return;
            }
        }

        _lastInputLevelDisplayTimestamp = now;
        var peakDb = 20d * Math.Log10(Math.Max(0.000001d, _displayInputPeak));
        var meterHostWidth = InputLevelMeter.Parent is FrameworkElement meterHost && meterHost.ActualWidth > 0d
            ? meterHost.ActualWidth
            : 260d;
        var meterWidth = Math.Clamp((_displayInputPeak / 0.95d) * meterHostWidth, 0d, meterHostWidth);

        string coachText;
        Brush coachForeground;
        Brush meterFill;

        if (peakDb < -36d)
        {
            coachText = $"Input: too quiet ({peakDb:0} dB)";
            coachForeground = _meterTextMutedBrush;
            meterFill = _meterMutedBrush;
        }
        else if (peakDb < -12d)
        {
            coachText = $"Input: good ({peakDb:0} dB)";
            coachForeground = _meterGoodBrush;
            meterFill = _meterGoodBrush;
        }
        else if (peakDb < -3d)
        {
            coachText = $"Input: hot ({peakDb:0} dB)";
            coachForeground = _meterWarnBrush;
            meterFill = _meterWarnBrush;
        }
        else
        {
            coachText = $"Input: clipping risk ({peakDb:0} dB)";
            coachForeground = _meterDangerBrush;
            meterFill = _meterDangerBrush;
        }

        if (Math.Abs(meterWidth - _lastInputLevelMeterWidth) >= 0.5d)
        {
            _lastInputLevelMeterWidth = meterWidth;
            InputLevelMeter.Width = meterWidth;
        }

        if (!string.Equals(_lastInputCoachText, coachText, StringComparison.Ordinal))
        {
            _lastInputCoachText = coachText;
            InputCoachText.Text = coachText;
        }

        if (!ReferenceEquals(_lastInputCoachForeground, coachForeground))
        {
            _lastInputCoachForeground = coachForeground;
            InputCoachText.Foreground = coachForeground;
        }

        if (!ReferenceEquals(_lastInputLevelFill, meterFill))
        {
            _lastInputLevelFill = meterFill;
            InputLevelMeter.Fill = meterFill;
        }
    }

    private void UpdateOutputSignalStatus(SpectrumFrame frame)
    {
        if (OutputSignalText is null || OutputPeakMeter is null || OutputRmsMeter is null)
        {
            return;
        }

        var programPeakLevel = Math.Clamp(double.IsFinite(frame.PeakLevel) ? frame.PeakLevel : 0d, 0d, 1d);
        var programRmsLevel = Math.Clamp(double.IsFinite(frame.RmsLevel) ? frame.RmsLevel : 0d, 0d, 1d);
        var limiterReductionDb = Math.Clamp(
            double.IsFinite(frame.Telemetry.MasterLimiterReductionDb) ? frame.Telemetry.MasterLimiterReductionDb : 0d,
            0d,
            24d);
        var normalizeGain = Math.Clamp(
            double.IsFinite(frame.Telemetry.MasterNormalizeGain) ? frame.Telemetry.MasterNormalizeGain : 1d,
            0.01d,
            12d);

        _displayOutputPeak = programPeakLevel > _displayOutputPeak
            ? Ease(_displayOutputPeak, programPeakLevel, 0.35d)
            : Ease(_displayOutputPeak, programPeakLevel, 0.06d);
        _displayOutputRms = programRmsLevel > _displayOutputRms
            ? Ease(_displayOutputRms, programRmsLevel, 0.24d)
            : Ease(_displayOutputRms, programRmsLevel, 0.08d);
        _displayMasterLimiterReductionDb = limiterReductionDb > _displayMasterLimiterReductionDb
            ? Ease(_displayMasterLimiterReductionDb, limiterReductionDb, 0.38d)
            : Ease(_displayMasterLimiterReductionDb, limiterReductionDb, 0.08d);

        var now = Stopwatch.GetTimestamp();
        if (_lastOutputLevelDisplayTimestamp != 0)
        {
            var elapsedMs = (now - _lastOutputLevelDisplayTimestamp) * 1000d / Stopwatch.Frequency;
            if (elapsedMs < 33d)
            {
                return;
            }
        }

        _lastOutputLevelDisplayTimestamp = now;
        var peakDb = 20d * Math.Log10(Math.Max(0.000001d, _displayOutputPeak));
        var rmsDb = 20d * Math.Log10(Math.Max(0.000001d, _displayOutputRms));
        var meterHostWidth = OutputPeakMeter.Parent is FrameworkElement meterHost && meterHost.ActualWidth > 0d
            ? meterHost.ActualWidth
            : 260d;
        var meterWidth = Math.Clamp((_displayOutputPeak / 0.95d) * meterHostWidth, 0d, meterHostWidth);
        var rmsMeterWidth = Math.Clamp((_displayOutputRms / 0.45d) * meterHostWidth, 0d, meterHostWidth);
        var routed = IsProcessedOutputRequested() && _spectrumService.IsProcessedOutputEnabled;
        if (programPeakLevel >= 0.98d)
        {
            _masterClipHoldFrames = 18;
        }
        else if (_masterClipHoldFrames > 0)
        {
            _masterClipHoldFrames--;
        }

        string signalText;
        Brush signalForeground;
        Brush meterFill;
        if (!_spectrumService.IsRunning)
        {
            signalText = "Program mix: not listening";
            signalForeground = _meterTextMutedBrush;
            meterFill = _meterMutedBrush;
        }
        else if (peakDb < -54d)
        {
            signalText = routed ? "Program mix: route open, silent" : "Program mix: silent";
            signalForeground = _meterWarnBrush;
            meterFill = _meterMutedBrush;
        }
        else if (peakDb < -12d)
        {
            signalText = routed ? $"Program mix: routing live ({peakDb:0} dB)" : $"Program mix: live ({peakDb:0} dB)";
            signalForeground = _meterGoodBrush;
            meterFill = _meterGoodBrush;
        }
        else if (peakDb < -3d)
        {
            signalText = routed ? $"Program mix: routing hot ({peakDb:0} dB)" : $"Program mix: hot ({peakDb:0} dB)";
            signalForeground = _meterWarnBrush;
            meterFill = _meterWarnBrush;
        }
        else
        {
            signalText = routed ? $"Program mix: routing clipping risk ({peakDb:0} dB)" : $"Program mix: clipping risk ({peakDb:0} dB)";
            signalForeground = _meterDangerBrush;
            meterFill = _meterDangerBrush;
        }

        var masterMeterText = $"Peak {FormatDbText(peakDb)} | RMS {FormatDbText(rmsDb)}";
        var limiterText = _displayMasterLimiterReductionDb >= 0.05d
            ? $"Limiter GR {_displayMasterLimiterReductionDb:0.0} dB"
            : "Limiter GR 0.0 dB";
        var normalizeText = $"Normalize {normalizeGain:0.00}x";
        var limiterHostWidth = MasterLimiterReductionMeter?.Parent is FrameworkElement limiterHost && limiterHost.ActualWidth > 0d
            ? limiterHost.ActualWidth
            : meterHostWidth;
        var limiterMeterWidth = Math.Clamp((_displayMasterLimiterReductionDb / 12d) * limiterHostWidth, 0d, limiterHostWidth);
        var clipFill = _masterClipHoldFrames > 0 ? _meterDangerBrush : _meterMutedBrush;

        if (Math.Abs(meterWidth - _lastOutputLevelMeterWidth) >= 0.5d)
        {
            _lastOutputLevelMeterWidth = meterWidth;
            OutputPeakMeter.Width = meterWidth;
        }

        if (Math.Abs(rmsMeterWidth - _lastOutputRmsMeterWidth) >= 0.5d)
        {
            _lastOutputRmsMeterWidth = rmsMeterWidth;
            OutputRmsMeter.Width = rmsMeterWidth;
        }

        if (MasterLimiterReductionMeter is not null
            && Math.Abs(limiterMeterWidth - _lastMasterLimiterReductionMeterWidth) >= 0.5d)
        {
            _lastMasterLimiterReductionMeterWidth = limiterMeterWidth;
            MasterLimiterReductionMeter.Width = limiterMeterWidth;
        }

        if (!string.Equals(_lastOutputSignalText, signalText, StringComparison.Ordinal))
        {
            _lastOutputSignalText = signalText;
            OutputSignalText.Text = signalText;
        }

        if (MasterMeterText is not null
            && !string.Equals(_lastMasterMeterText, masterMeterText, StringComparison.Ordinal))
        {
            _lastMasterMeterText = masterMeterText;
            MasterMeterText.Text = masterMeterText;
        }

        if (MasterLimiterReductionText is not null
            && !string.Equals(_lastMasterLimiterReductionText, limiterText, StringComparison.Ordinal))
        {
            _lastMasterLimiterReductionText = limiterText;
            MasterLimiterReductionText.Text = limiterText;
        }

        if (MasterLimiterNormalizeText is not null
            && !string.Equals(_lastMasterLimiterNormalizeText, normalizeText, StringComparison.Ordinal))
        {
            _lastMasterLimiterNormalizeText = normalizeText;
            MasterLimiterNormalizeText.Text = normalizeText;
        }

        if (!ReferenceEquals(_lastOutputSignalForeground, signalForeground))
        {
            _lastOutputSignalForeground = signalForeground;
            OutputSignalText.Foreground = signalForeground;
        }

        if (!ReferenceEquals(_lastOutputLevelFill, meterFill))
        {
            _lastOutputLevelFill = meterFill;
            OutputPeakMeter.Fill = meterFill;
        }

        if (MasterClipIndicator is not null && !ReferenceEquals(_lastMasterClipFill, clipFill))
        {
            _lastMasterClipFill = clipFill;
            MasterClipIndicator.Fill = clipFill;
            MasterClipIndicator.Opacity = _masterClipHoldFrames > 0 ? 1d : 0.45d;
        }

        UpdateFeedbackRisk(frame);
    }

    private void UpdateFeedbackRisk(SpectrumFrame frame)
    {
        if (FeedbackRiskText is null)
        {
            return;
        }

        var risk = _spectrumService.IsRunning && frame.PeakLevel > 0.001d
            ? FeedbackDangerDetector.Analyze(frame.Magnitudes)
            : FeedbackDangerResult.None;
        if (risk.IsDangerous)
        {
            _feedbackRiskHoldFrames = risk.Score >= 0.55d ? 36 : 20;
            _lastFeedbackRiskFrequencyHz = risk.FrequencyHz;
            _lastFeedbackRiskSpikeRiseDb = risk.SpikeRiseDb;
        }
        else if (_feedbackRiskHoldFrames > 0)
        {
            _feedbackRiskHoldFrames--;
        }

        var targetScore = risk.IsDangerous ? risk.Score : 0d;
        _displayFeedbackRiskScore = targetScore > _displayFeedbackRiskScore
            ? Ease(_displayFeedbackRiskScore, targetScore, 0.44d)
            : Ease(_displayFeedbackRiskScore, targetScore, 0.07d);

        string text;
        Brush foreground;
        if (!_spectrumService.IsRunning)
        {
            text = "Feedback: not listening";
            foreground = _meterTextMutedBrush;
        }
        else if (_feedbackRiskHoldFrames <= 0 || _displayFeedbackRiskScore < 0.16d)
        {
            text = "Feedback: clear";
            foreground = _meterTextMutedBrush;
        }
        else
        {
            var frequencyText = FormatFrequencyText(_lastFeedbackRiskFrequencyHz);
            var spikeText = _lastFeedbackRiskSpikeRiseDb > 0d
                ? $" (+{_lastFeedbackRiskSpikeRiseDb:0} dB spike)"
                : string.Empty;
            if (_displayFeedbackRiskScore >= 0.62d)
            {
                text = $"Feedback risk: {frequencyText} ringing{spikeText}";
                foreground = _meterDangerBrush;
            }
            else
            {
                text = $"Feedback watch: narrow spike near {frequencyText}{spikeText}";
                foreground = _meterWarnBrush;
            }
        }

        if (!string.Equals(_lastFeedbackRiskText, text, StringComparison.Ordinal))
        {
            _lastFeedbackRiskText = text;
            FeedbackRiskText.Text = text;
        }

        if (!ReferenceEquals(_lastFeedbackRiskForeground, foreground))
        {
            _lastFeedbackRiskForeground = foreground;
            FeedbackRiskText.Foreground = foreground;
        }
    }

    private static string FormatFrequencyText(double frequencyHz)
    {
        return frequencyHz >= 1000d
            ? $"{frequencyHz / 1000d:0.0} kHz"
            : $"{frequencyHz:0} Hz";
    }

    private static string FormatDbText(double decibels)
    {
        return decibels <= -99d ? "-inf dB" : $"{decibels:0} dB";
    }

    private void UpdateAudioStability(SpectrumFrame frame)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastAudioStabilityDisplayTimestamp != 0)
        {
            var displayElapsedMs = (now - _lastAudioStabilityDisplayTimestamp) * 1000d / Stopwatch.Frequency;
            if (displayElapsedMs < 300d)
            {
                return;
            }
        }

        _lastAudioStabilityDisplayTimestamp = now;
        var telemetry = frame.Telemetry;
        var callbackMs = telemetry.AudioCallbackIntervalMs;
        var processingMs = telemetry.AudioProcessingTimeMs;
        var bufferMs = Math.Max(1d, telemetry.AudioBufferDurationMs);
        var expectedFrameMs = telemetry.AudioExpectedCallbackIntervalMs > 0d
            ? Math.Max(bufferMs, telemetry.AudioExpectedCallbackIntervalMs)
            : Math.Max(bufferMs, callbackMs);
        var frameIntervalMs = callbackMs > 0d ? Math.Max(1d, callbackMs) : expectedFrameMs;

        _displayedAudioFrameIntervalMs = SmoothAudioTiming(_displayedAudioFrameIntervalMs, frameIntervalMs, 0.18d);
        _displayedAudioProcessingMs = SmoothAudioTiming(_displayedAudioProcessingMs, processingMs, 0.18d);

        var dspLoadRatio = Math.Clamp(_displayedAudioProcessingMs / Math.Max(0.001d, _displayedAudioFrameIntervalMs), 0d, 1.25d);
        _audioStabilityScore += (dspLoadRatio - _audioStabilityScore) * 0.2d;

        var desiredSeverity = _audioStabilityScore switch
        {
            >= 0.85d => 2,
            >= 0.65d => 1,
            _ => 0
        };

        var severity = UpdateAudioStabilitySeverity(desiredSeverity, now);
        var meterHostWidth = AudioStabilityMeter.Parent is FrameworkElement meterHost && meterHost.ActualWidth > 0d
            ? meterHost.ActualWidth
            : 260d;
        var targetMeterWidth = Math.Clamp(_audioStabilityScore * meterHostWidth, 0d, meterHostWidth);
        _audioStabilityMeterWidth = _audioStabilityMeterWidth <= 0d
            ? targetMeterWidth
            : _audioStabilityMeterWidth + (targetMeterWidth - _audioStabilityMeterWidth) * 0.24d;

        var fill = severity switch
        {
            0 => _meterGoodBrush,
            1 => _meterWarnBrush,
            _ => _meterDangerBrush
        };
        var label = severity switch
        {
            0 => "Headroom",
            1 => "Busy",
            _ => "Tight"
        };

        AudioStabilityIndicator.Fill = fill;
        AudioStabilityMeter.Fill = fill;
        AudioStabilityMeter.Width = Math.Clamp(_audioStabilityMeterWidth, 0d, meterHostWidth);
        AudioStabilityText.Foreground = fill;
        var statusText = $"{label}: frame {_displayedAudioFrameIntervalMs:0.0} ms ({1000d / Math.Max(0.001d, _displayedAudioFrameIntervalMs):0}/s), DSP avg {_displayedAudioProcessingMs:0.0} ms, load {_audioStabilityScore:P0}";
        AudioStabilityText.Text = statusText;

        if (KaraokePlaybackHealthIndicator is not null
            && KaraokePlaybackHealthMeter is not null
            && KaraokePlaybackHealthText is not null)
        {
            var karaokeMeterHostWidth = KaraokePlaybackHealthMeter.Parent is FrameworkElement karaokeMeterHost && karaokeMeterHost.ActualWidth > 0d
                ? karaokeMeterHost.ActualWidth
                : 260d;
            KaraokePlaybackHealthIndicator.Fill = fill;
            KaraokePlaybackHealthMeter.Fill = fill;
            KaraokePlaybackHealthMeter.Width = Math.Clamp(_audioStabilityMeterWidth, 0d, karaokeMeterHostWidth);
            KaraokePlaybackHealthText.Foreground = fill;
            KaraokePlaybackHealthText.Text = statusText;
        }
    }

    private static double SmoothAudioTiming(double current, double next, double amount)
    {
        if (current <= 0d)
        {
            return Math.Max(0d, next);
        }

        return current + (Math.Max(0d, next) - current) * amount;
    }

    private int UpdateAudioStabilitySeverity(int desiredSeverity, long timestamp)
    {
        if (desiredSeverity == _audioStabilitySeverity)
        {
            _audioStabilityCandidateSeverity = -1;
            _audioStabilityCandidateTimestamp = 0;
            return _audioStabilitySeverity;
        }

        if (_audioStabilityCandidateSeverity != desiredSeverity)
        {
            _audioStabilityCandidateSeverity = desiredSeverity;
            _audioStabilityCandidateTimestamp = timestamp;
            return _audioStabilitySeverity;
        }

        var requiredMs = desiredSeverity > _audioStabilitySeverity ? 900d : 1400d;
        var elapsedMs = (timestamp - _audioStabilityCandidateTimestamp) * 1000d / Stopwatch.Frequency;
        if (elapsedMs < requiredMs)
        {
            return _audioStabilitySeverity;
        }

        _audioStabilitySeverity = desiredSeverity;
        _audioStabilityCandidateSeverity = -1;
        _audioStabilityCandidateTimestamp = 0;
        return _audioStabilitySeverity;
    }

    private static double ShapeMagnitude(double magnitude)
    {
        var lifted = Math.Max(0d, magnitude - 0.01d);
        return Math.Clamp(Math.Pow(lifted, 0.9d), 0d, 1d);
    }

    private double NormalizeForDisplay(double magnitude)
    {
        return NormalizeForDisplay(magnitude, _visualCeiling);
    }

    private static double NormalizeForDisplay(double magnitude, double visualCeiling)
    {
        return Math.Clamp(magnitude / Math.Max(0.08d, visualCeiling) * 0.62d, 0d, 0.88d);
    }

    private void EnsureRenderBuffers(int processedLength, int rawLength)
    {
        if (_renderedMagnitudes.Length != processedLength)
        {
            _renderedMagnitudes = new double[processedLength];
        }

        if (_renderedRawMagnitudes.Length != rawLength)
        {
            _renderedRawMagnitudes = new double[rawLength];
        }
    }

    private void EnsureInputRenderBuffers(int input1Length, int input2Length)
    {
        if (_renderedInput1Magnitudes.Length != input1Length)
        {
            _renderedInput1Magnitudes = new double[input1Length];
        }

        if (_renderedInput2Magnitudes.Length != input2Length)
        {
            _renderedInput2Magnitudes = new double[input2Length];
        }
    }

    private void EnsureMixingMicRenderBuffer(int traceIndex, int length)
    {
        if (_renderedMixingMicMagnitudes[traceIndex] is null
            || _renderedMixingMicMagnitudes[traceIndex].Length != length)
        {
            _renderedMixingMicMagnitudes[traceIndex] = new double[length];
        }
    }

    private void EnsureMixingSpectrumGrid(double width, double graphTop, double graphBottom)
    {
        const int horizontalLineCount = 5;
        const int verticalLineCount = 9;
        var desiredLineCount = horizontalLineCount + verticalLineCount;
        while (_mixingSpectrumGridLines.Count < desiredLineCount)
        {
            var line = new Line
            {
                Stroke = _waveformGridBrush,
                StrokeThickness = 1d,
                Opacity = 0.78d,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(line, 0);
            _mixingSpectrumGridLines.Add(line);
            MixingMicSpectrumCanvas.Children.Insert(0, line);
        }

        var lineIndex = 0;
        for (var i = 0; i < horizontalLineCount; i++)
        {
            var y = horizontalLineCount == 1
                ? graphTop
                : graphTop + (graphBottom - graphTop) * i / (double)(horizontalLineCount - 1);
            var line = _mixingSpectrumGridLines[lineIndex++];
            line.X1 = 0d;
            line.X2 = width;
            line.Y1 = y;
            line.Y2 = y;
            line.StrokeThickness = i == horizontalLineCount - 1 ? 1.5d : 1d;
        }

        for (var i = 0; i < verticalLineCount; i++)
        {
            var x = verticalLineCount == 1
                ? 0d
                : i / (double)(verticalLineCount - 1) * width;
            var line = _mixingSpectrumGridLines[lineIndex++];
            line.X1 = x;
            line.X2 = x;
            line.Y1 = graphTop;
            line.Y2 = graphBottom;
            line.StrokeThickness = i == 0 || i == verticalLineCount - 1 ? 1.5d : 1d;
        }
    }

    private static double Ease(double current, double target, double amount)
    {
        return current + (target - current) * amount;
    }

    private static Geometry CreatePolylineGeometry(PointCollection source)
    {
        if (source.Count < 2)
        {
            return Geometry.Empty;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(source[0], isFilled: false, isClosed: false);
            context.PolyLineTo(source.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateSmoothedGeometry(PointCollection source)
    {
        if (source.Count < 4)
        {
            return CreatePolylineGeometry(source);
        }

        var smoothed = new List<Point>(source.Count * 2)
        {
            source[0]
        };

        for (var i = 1; i < source.Count - 2; i++)
        {
            var p0 = source[i - 1];
            var p1 = source[i];
            var p2 = source[i + 1];
            var p3 = source[i + 2];

            smoothed.Add(p1);
            smoothed.Add(CatmullRom(p0, p1, p2, p3, 0.5d));
        }

        smoothed.Add(source[^2]);
        smoothed.Add(source[^1]);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(smoothed[0], isFilled: false, isClosed: false);
            context.PolyLineTo(smoothed.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point CatmullRom(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var x = 0.5d * ((2d * p1.X) + (-p0.X + p2.X) * t + (2d * p0.X - 5d * p1.X + 4d * p2.X - p3.X) * t2 + (-p0.X + 3d * p1.X - 3d * p2.X + p3.X) * t3);
        var y = 0.5d * ((2d * p1.Y) + (-p0.Y + p2.Y) * t + (2d * p0.Y - 5d * p1.Y + 4d * p2.Y - p3.Y) * t2 + (-p0.Y + 3d * p1.Y - 3d * p2.Y + p3.Y) * t3);
        return new Point(x, y);
    }

    private void UpdateSignalStatus(double peakLevel)
    {
        if (_spectrumService.IsRunning && !_spectrumService.HasReceivedAudioCallbacks)
        {
            _silentFrameCount = 0;
            var callbackStatusText = _selectedDevice is null
                ? "Listening; waiting for audio callbacks."
                : CreateWaitingForAudioCallbacksStatus(_selectedDevice);
            if (!StatusText.Text.Contains("no audio callbacks", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(StatusText.Text, callbackStatusText, StringComparison.Ordinal))
            {
                StatusText.Text = callbackStatusText;
            }

            return;
        }

        if (peakLevel < 0.001d)
        {
            _silentFrameCount++;
        }
        else
        {
            _silentFrameCount = 0;
        }

        var statusText = _silentFrameCount > 20
            ? "Listening, but this input is silent. Try another mic/device or raise input gain."
            : _selectedDevice is not null
                ? "Listening"
                : null;
        if (statusText is not null && !string.Equals(StatusText.Text, statusText, StringComparison.Ordinal))
        {
            StatusText.Text = statusText;
        }
    }

    private void EnsureGraphGrid(double width, double graphTop, double graphBottom)
    {
        var neededLines = 17;
        while (_gridLines.Count < neededLines)
        {
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(36, 45, 54)),
                StrokeThickness = 1,
                Opacity = 0.82
            };
            _gridLines.Add(line);
            SpectrumCanvas.Children.Insert(0, line);
        }

        for (var i = 0; i < 9; i++)
        {
            var y = graphTop + (graphBottom - graphTop) * i / 8d;
            _gridLines[i].X1 = 0;
            _gridLines[i].Y1 = y;
            _gridLines[i].X2 = width;
            _gridLines[i].Y2 = y;
        }

        for (var i = 0; i < 8; i++)
        {
            var x = width * i / 7d;
            var line = _gridLines[9 + i];
            line.X1 = x;
            line.Y1 = graphTop;
            line.X2 = x;
            line.Y2 = graphBottom;
        }
    }

    private void EqVoiceZoneGuideCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateEqVoiceZoneGuide), DispatcherPriority.Render);
    }

    private void EqVoiceZoneGuideCanvasLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateEqualizerGuideLayout), DispatcherPriority.Render);
    }

    private void EqualizerFaceplateSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateEqualizerGuideLayout), DispatcherPriority.Render);
    }

    private void UpdateEqVoiceZoneGuide()
    {
        if (EqVoiceZoneGuideCanvas is null)
        {
            return;
        }

        EqVoiceZoneGuideCanvas.Children.Clear();
        var width = EqVoiceZoneGuideCanvas.ActualWidth;
        if (width <= 1d || _activeMicChannel is null || Bands.Count == 0)
        {
            return;
        }

        var height = Math.Max(1d, EqVoiceZoneGuideCanvas.ActualHeight);
        var railY = Math.Round(height * 0.52d) + 0.5d;
        var capTop = Math.Max(3d, railY - 7d);
        var capBottom = Math.Min(height - 3d, railY + 7d);
        var railBrush = new SolidColorBrush(Color.FromArgb(180, 95, 119, 134));
        var capBrush = new SolidColorBrush(Color.FromArgb(210, 129, 155, 169));
        var labelBrush = new SolidColorBrush(Color.FromRgb(222, 234, 244));
        var labelBackground = new SolidColorBrush(Color.FromArgb(232, 7, 13, 18));
        var labelBorderBrush = new SolidColorBrush(Color.FromArgb(145, 60, 86, 102));

        foreach (var zone in _voiceZones)
        {
            var left = Math.Clamp(FrequencyToEqualizerGuideX(zone.StartFrequencyHz, width), 0d, width);
            var right = Math.Clamp(FrequencyToEqualizerGuideX(zone.EndFrequencyHz, width), 0d, width);
            if (right - left < 8d)
            {
                continue;
            }

            var zoneWidth = right - left;
            var labelText = new TextBlock
            {
                Text = zone.Name,
                Foreground = labelBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var label = new Border
            {
                Background = labelBackground,
                BorderBrush = labelBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 2),
                MaxWidth = Math.Max(22d, zoneWidth - 10d),
                Child = labelText
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelWidth = Math.Min(label.DesiredSize.Width, Math.Max(22d, zoneWidth - 10d));
            var labelHeight = label.DesiredSize.Height;
            label.Width = labelWidth;

            var labelLeft = Math.Clamp(
                left + (zoneWidth - labelWidth) * 0.5d,
                left + 2d,
                Math.Max(left + 2d, right - labelWidth - 2d));
            var labelRight = labelLeft + labelWidth;
            var labelTop = Math.Clamp(railY - labelHeight * 0.5d, 0d, Math.Max(0d, height - labelHeight));

            EqVoiceZoneGuideCanvas.Children.Add(CreateEqZoneGuideLine(left, capTop, left, capBottom, capBrush, 1.1d));
            EqVoiceZoneGuideCanvas.Children.Add(CreateEqZoneGuideLine(right, capTop, right, capBottom, capBrush, 1.1d));
            if (labelLeft > left + 5d)
            {
                EqVoiceZoneGuideCanvas.Children.Add(CreateEqZoneGuideLine(left, railY, labelLeft - 5d, railY, railBrush, 1d));
            }

            if (labelRight + 5d < right)
            {
                EqVoiceZoneGuideCanvas.Children.Add(CreateEqZoneGuideLine(labelRight + 5d, railY, right, railY, railBrush, 1d));
            }

            Canvas.SetLeft(label, labelLeft);
            Canvas.SetTop(label, labelTop);
            EqVoiceZoneGuideCanvas.Children.Add(label);
        }
    }

    private static Line CreateEqZoneGuideLine(double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        return new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
    }

    private double FrequencyToEqualizerGuideX(double frequency, double width)
    {
        var bandCount = Bands.Count;
        if (bandCount == 0)
        {
            return 0d;
        }

        var slotWidth = width / bandCount;
        if (bandCount == 1)
        {
            return slotWidth * 0.5d;
        }

        var minimumFrequency = Math.Max(1d, Bands[0].CenterFrequencyHz);
        var maximumFrequency = Math.Max(minimumFrequency, Bands[bandCount - 1].CenterFrequencyHz);
        var targetLog = Math.Log(Math.Clamp(frequency, minimumFrequency, maximumFrequency));
        for (var i = 0; i < bandCount - 1; i++)
        {
            var currentLog = Math.Log(Math.Max(1d, Bands[i].CenterFrequencyHz));
            var nextLog = Math.Log(Math.Max(1d, Bands[i + 1].CenterFrequencyHz));
            if (nextLog <= currentLog)
            {
                continue;
            }

            if (targetLog <= nextLog || i == bandCount - 2)
            {
                var blend = Math.Clamp((targetLog - currentLog) / (nextLog - currentLog), 0d, 1d);
                return slotWidth * (i + 0.5d + blend);
            }
        }

        return width - slotWidth * 0.5d;
    }

    private void UpdateEqualizerHoverRegion()
    {
        var width = Math.Max(1d, SpectrumCanvas.ActualWidth);
        var height = Math.Max(1d, SpectrumCanvas.ActualHeight);
        var topInset = 86d;
        var bottomInset = GetAnalyzerBottomInset();
        var graphTop = topInset;
        var graphBottom = graphTop + Math.Max(1d, height - topInset - bottomInset);
        UpdateEqualizerHoverRegion(width, graphTop, graphBottom);
    }

    private void UpdateEqualizerHoverRegion(double width, double graphTop, double graphBottom)
    {
        if (_hoveredEqualizerBand is null || _showWaveform3D)
        {
            _equalizerHoverRegion.Visibility = Visibility.Collapsed;
            return;
        }

        var (startFrequency, endFrequency) = GetEqualizerBandDisplayRange(_hoveredEqualizerBand);
        var left = FrequencyToX(startFrequency, width);
        var right = FrequencyToX(endFrequency, width);
        if (right <= left)
        {
            var center = FrequencyToX(_hoveredEqualizerBand.CenterFrequencyHz, width);
            left = Math.Max(0d, center - 4d);
            right = Math.Min(width, center + 4d);
        }

        _equalizerHoverRegion.Width = Math.Max(2d, right - left);
        _equalizerHoverRegion.Height = Math.Max(1d, graphBottom - graphTop);
        Canvas.SetLeft(_equalizerHoverRegion, Math.Clamp(left, 0d, width));
        Canvas.SetTop(_equalizerHoverRegion, graphTop);
        _equalizerHoverRegion.Visibility = Visibility.Visible;
    }

    private void UpdateDx12EqualizerHoverRegion()
    {
        if (_selectedMicSpectrumGraphHost is null)
        {
            return;
        }

        if (_hoveredEqualizerBand is null || _showWaveform3D)
        {
            _selectedMicSpectrumGraphHost.ClearSpectrumHoverRange();
            return;
        }

        var (startFrequency, endFrequency) = GetEqualizerBandDisplayRange(_hoveredEqualizerBand);
        _selectedMicSpectrumGraphHost.SetSpectrumHoverRange(
            FrequencyToGraphPosition(startFrequency),
            FrequencyToGraphPosition(endFrequency));
    }

    private static (double StartFrequency, double EndFrequency) GetEqualizerBandDisplayRange(EqualizerBand band)
    {
        var start = band.CenterFrequencyHz / EqualizerHoverHalfPowerRatio;
        var end = band.CenterFrequencyHz * EqualizerHoverHalfPowerRatio;
        return (
            Math.Clamp(start, MinimumDisplayFrequency, MaximumDisplayFrequency),
            Math.Clamp(end, MinimumDisplayFrequency, MaximumDisplayFrequency));
    }

    private static double CalculateEqualizerHoverHalfPowerRatio()
    {
        var qTerm = Math.Sqrt(4d * EqualizerHoverQ * EqualizerHoverQ + 1d);
        var bandwidthOctaves = Math.Log2((qTerm + 1d) / (qTerm - 1d));
        return Math.Pow(2d, bandwidthOctaves / 2d);
    }

    private static double FrequencyToX(double frequency, double width)
    {
        return FrequencyToGraphPosition(frequency) * width;
    }

    private static double FrequencyToGraphPosition(double frequency)
    {
        var clamped = Math.Clamp(frequency, MinimumDisplayFrequency, MaximumDisplayFrequency);
        return Math.Log(clamped / MinimumDisplayFrequency) / Math.Log(MaximumDisplayFrequency / MinimumDisplayFrequency);
    }

    private void FlatPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadBuiltInPreset(BuiltInVoicePresetCatalog.Flat);
    }

    private void PodcastCleanPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadBuiltInPreset(BuiltInVoicePresetCatalog.PodcastClean);
    }

    private void WarmRadioPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadBuiltInPreset(BuiltInVoicePresetCatalog.WarmRadio);
    }

    private void DeepWarmPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadBuiltInPreset(BuiltInVoicePresetCatalog.DeepWarm);
    }

    private void NoisyRoomPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadBuiltInPreset(BuiltInVoicePresetCatalog.NoisyRoom);
    }

    private void BrightHeadsetPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadBuiltInPreset(BuiltInVoicePresetCatalog.BrightHeadset);
    }

    private void LoadBuiltInPreset(BuiltInVoicePreset preset)
    {
        ApplyPreset(
            preset.Name,
            preset.Description,
            preset.HighPassFrequencyHz,
            preset.DePopperAmountDb,
            preset.GateThresholdDb,
            preset.NoiseSuppressionAmountDb,
            preset.EchoReducerAmountDb,
            preset.CompressorThresholdDb,
            preset.CompressorRatio,
            preset.DeEsserAmountDb,
            preset.MakeupGainDb,
            preset.LimiterCeilingDb,
            preset.Gains,
            preset.ConfigureAdditionalSettings);
    }

    private void ApplyPreset(
        string name,
        string description,
        double highPassFrequencyHz,
        double dePopperAmountDb,
        double gateThresholdDb,
        double noiseSuppressionAmountDb,
        double echoReducerAmountDb,
        double compressorThresholdDb,
        double compressorRatio,
        double deEsserAmountDb,
        double makeupGainDb,
        double limiterCeilingDb,
        IReadOnlyList<double> gains,
        Action<VoiceProcessorSettings>? configureAdditionalSettings = null)
    {
        Settings.HighPassEnabled = true;
        Settings.LowPassEnabled = false;
        Settings.HumRemovalEnabled = false;
        Settings.NotchFilterEnabled = false;
        Settings.ParametricEqEnabled = false;
        Settings.ShelfEqEnabled = false;
        Settings.InputTrimDb = 0;
        Settings.HighPassFrequencyHz = highPassFrequencyHz;
        Settings.LowPassFrequencyHz = 16000;
        Settings.HumRemovalFrequencyHz = 60;
        Settings.NotchFilterFrequencyHz = 2800;
        Settings.NotchFilterDepthDb = 18;
        Settings.NotchFilterQ = 16;
        Settings.ParametricEqFrequencyHz = 1000;
        Settings.ParametricEqGainDb = 0;
        Settings.ParametricEqQ = 1.2;
        Settings.LowShelfFrequencyHz = 160;
        Settings.LowShelfGainDb = 0;
        Settings.HighShelfFrequencyHz = 8000;
        Settings.HighShelfGainDb = 0;
        Settings.DePopperEnabled = true;
        Settings.DePopperAmountDb = dePopperAmountDb;
        Settings.DePopperFrequencyHz = 180;
        Settings.DePopperThresholdDb = -28;
        Settings.NoiseGateEnabled = true;
        Settings.NoiseGateThresholdDb = gateThresholdDb;
        Settings.NoiseGateAttackMs = 5;
        Settings.NoiseGateHoldMs = 90;
        Settings.NoiseGateReleaseMs = 140;
        Settings.NoiseGateRangeDb = 28;
        Settings.ExpanderEnabled = true;
        Settings.ExpanderThresholdDb = Math.Min(-15, gateThresholdDb - 6);
        Settings.ExpanderRatio = 1.8;
        Settings.ExpanderRangeDb = 12;
        Settings.ExpanderAttackMs = 8;
        Settings.ExpanderHoldMs = 60;
        Settings.ExpanderReleaseMs = 220;
        Settings.NoiseSuppressionEnabled = true;
        Settings.NoiseSuppressionAmountDb = noiseSuppressionAmountDb;
        Settings.NoiseSuppressionSensitivity = 4;
        Settings.EchoReducerEnabled = true;
        Settings.EchoReducerAmountDb = echoReducerAmountDb;
        Settings.EchoReducerSensitivity = 5;
        Settings.CompressorEnabled = true;
        Settings.CompressorThresholdDb = compressorThresholdDb;
        Settings.CompressorRatio = compressorRatio;
        Settings.CompressorAttackMs = 12;
        Settings.CompressorReleaseMs = 140;
        Settings.CompressorKneeDb = 6;
        Settings.DeEsserEnabled = true;
        Settings.DeEsserAmountDb = deEsserAmountDb;
        Settings.DeEsserFrequencyHz = 5200;
        Settings.DeEsserThresholdDb = -34;
        Settings.DeEsserRangeDb = 9;
        Settings.BreathReducerEnabled = false;
        Settings.BreathReducerAmountDb = 6;
        Settings.BreathReducerSensitivity = 5;
        Settings.PresenceEnhancerEnabled = true;
        Settings.PresenceEnhancerAmountDb = 2;
        Settings.PresenceEnhancerFrequencyHz = 3000;
        Settings.PresenceEnhancerWidthHz = 2600;
        Settings.SaturationEnabled = false;
        Settings.SaturationAmount = 2;
        Settings.MakeupGainDb = makeupGainDb;
        Settings.LimiterEnabled = true;
        Settings.LimiterCeilingDb = limiterCeilingDb;
        Settings.LimiterSoftClipEnabled = true;
        Settings.LimiterSoftClipDriveDb = 1.5;
        Settings.LimiterLookaheadEnabled = true;
        Settings.LimiterLookaheadMs = 3;
        Settings.LimiterReleaseMs = 60;
        configureAdditionalSettings?.Invoke(Settings);

        for (var i = 0; i < Bands.Count && i < gains.Count; i++)
        {
            Bands[i].IsEnabled = true;
            Bands[i].GainDb = gains[i];
        }

        for (var i = gains.Count; i < Bands.Count; i++)
        {
            Bands[i].IsEnabled = true;
            Bands[i].GainDb = 0;
        }

        SyncEqualizerSettings();
        SetProcessingSliderDefaults(description);
        SetActivePresetButton(name);
        _lastLoadedPresetName = name;
        _lastLoadedPresetIsUserPreset = false;
        _activeMicChannel.ActivePresetName = name;
        _activeMicChannel.ActivePresetIsUserPreset = false;
        _activeMicChannel.PresetDescription = description;
        _activeMicChannel.AnalyzerSmoothing = DefaultAnalyzerSmoothingPercent;
        StatusText.Text = $"{name} preset loaded";
        ConfigureLiveMixFromChannels();
        PersistAppState();
    }

    private void UserPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void CameraProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void SaveCameraProfileClicked(object sender, RoutedEventArgs e)
    {
        var name = GetCameraProfileNameFromEntry();
        if (string.IsNullOrWhiteSpace(name))
        {
            CameraProfileStatusText.Text = "Type a new profile name or choose a camera profile to update.";
            return;
        }

        try
        {
            var profile = CaptureCameraProfile(name);
            CameraProfileStore.Save(CameraProfileFolder, name, profile, UserPresetJsonOptions);
            LoadCameraProfileList();
            CameraProfileComboBox.SelectedItem = CameraProfileNames.FirstOrDefault(candidate =>
                candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
            CameraProfileNameTextBox.Clear();
            CameraProfileStatusText.Text = $"Camera profile saved: {name}";
        }
        catch (Exception ex)
        {
            CameraProfileStatusText.Text = $"Could not save camera profile: {ex.Message}";
        }
    }

    private void LoadCameraProfileClicked(object sender, RoutedEventArgs e)
    {
        var name = GetCameraProfileNameFromEntry();
        if (string.IsNullOrWhiteSpace(name))
        {
            CameraProfileStatusText.Text = "Choose a camera profile to load.";
            return;
        }

        try
        {
            var profile = LoadCameraProfile(name);
            if (profile is null)
            {
                CameraProfileStatusText.Text = $"Camera profile not found: {name}";
                return;
            }

            ApplyCameraProfile(profile);
        }
        catch (Exception ex)
        {
            CameraProfileStatusText.Text = $"Could not load camera profile: {ex.Message}";
        }
    }

    private void DeleteCameraProfileClicked(object sender, RoutedEventArgs e)
    {
        var name = GetCameraProfileNameFromEntry();
        if (string.IsNullOrWhiteSpace(name))
        {
            CameraProfileStatusText.Text = "Choose a camera profile to delete.";
            return;
        }

        try
        {
            var path = CameraProfileStore.GetPath(CameraProfileFolder, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            LoadCameraProfileList();
            CameraProfileComboBox.SelectedIndex = CameraProfileNames.Count > 0 ? 0 : -1;
            CameraProfileStatusText.Text = $"Camera profile deleted: {name}";
        }
        catch (Exception ex)
        {
            CameraProfileStatusText.Text = $"Could not delete camera profile: {ex.Message}";
        }
    }

    private void SaveUserPresetClicked(object sender, RoutedEventArgs e)
    {
        var typedName = UserPresetNameTextBox.Text.Trim();
        var selectedName = UserPresetComboBox.SelectedItem as string ?? string.Empty;
        var name = !string.IsNullOrWhiteSpace(typedName)
            ? typedName
            : selectedName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Type a new preset name or choose a user preset to update.";
            return;
        }

        try
        {
            Directory.CreateDirectory(UserPresetFolder);
            var preset = CaptureUserPreset(name);
            var json = JsonSerializer.Serialize(preset, UserPresetJsonOptions);
            AtomicFile.WriteAllText(GetUserPresetPath(name), json);
            LoadUserPresetList();
            UserPresetComboBox.SelectedItem = UserPresetNames.FirstOrDefault(candidate =>
                candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
            UserPresetNameTextBox.Clear();
            StatusText.Text = $"User preset saved: {name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save preset: {ex.Message}";
        }
    }

    private void LoadUserPresetClicked(object sender, RoutedEventArgs e)
    {
        var name = GetUserPresetNameFromEntry();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Choose a user preset to load.";
            return;
        }

        try
        {
            var preset = LoadUserPreset(name);
            if (preset is null)
            {
                StatusText.Text = $"User preset not found: {name}";
                return;
            }

            ApplyUserPreset(preset);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load preset: {ex.Message}";
        }
    }

    private void DeleteUserPresetClicked(object sender, RoutedEventArgs e)
    {
        var name = GetUserPresetNameFromEntry();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Choose a user preset to delete.";
            return;
        }

        try
        {
            var path = GetUserPresetPath(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            LoadUserPresetList();
            UserPresetComboBox.SelectedIndex = UserPresetNames.Count > 0 ? 0 : -1;
            StatusText.Text = $"User preset deleted: {name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not delete preset: {ex.Message}";
        }
    }

    private void LoadUserPresetList()
    {
        var selectedName = UserPresetComboBox.SelectedItem as string;
        UserPresetNames.Clear();

        try
        {
            Directory.CreateDirectory(UserPresetFolder);
            foreach (var path in Directory.EnumerateFiles(UserPresetFolder, "*.json").OrderBy(path => path))
            {
                var name = ReadUserPresetName(path);
                if (!string.IsNullOrWhiteSpace(name)
                    && !UserPresetNames.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    UserPresetNames.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read user presets: {ex.Message}";
        }

        UserPresetComboBox.SelectedItem = !string.IsNullOrWhiteSpace(selectedName)
            ? UserPresetNames.FirstOrDefault(candidate => candidate.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
            : null;

        if (UserPresetComboBox.SelectedItem is null)
        {
            UserPresetComboBox.SelectedIndex = UserPresetNames.Count > 0 ? 0 : -1;
        }
    }

    private void UpdateAudioRecordingFolderText()
    {
        if (RecordingSaveLocationTextBox is not null)
        {
            RecordingSaveLocationTextBox.Text = _audioRecordingFolder;
        }

        ConfigureAudioRecordingFolderWatcher();
        RefreshAudioRecordingFiles(_lastAudioRecordingPath);
    }

    private void ConfigureAudioRecordingFolderWatcher()
    {
        DisposeAudioRecordingFolderWatcher();

        try
        {
            _audioRecordingFolderWatcher = FileBrowserWatcher.Start(
                _audioRecordingFolder,
                includeSubdirectories: false,
                NotifyFilters.FileName | NotifyFilters.CreationTime,
                AudioRecordingCatalog.IsSupportedRecordingFile,
                QueueAudioRecordingFilesRefresh);
        }
        catch
        {
            _audioRecordingFolderWatcher = null;
        }
    }

    private void DisposeAudioRecordingFolderWatcher()
    {
        var watcher = _audioRecordingFolderWatcher;
        if (watcher is null)
        {
            return;
        }

        _audioRecordingFolderWatcher = null;
        watcher.Dispose();
    }

    private void QueueAudioRecordingFilesRefresh()
    {
        if (_isClosing || System.Threading.Interlocked.Exchange(ref _audioRecordingFolderRefreshQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var timer = new DispatcherTimer
            {
                Interval = AudioRecordingFolderRefreshDelay
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                System.Threading.Volatile.Write(ref _audioRecordingFolderRefreshQueued, 0);
                RefreshAudioRecordingFiles(_lastAudioRecordingPath);
                UpdateStandaloneAudioRecordingTransportControls();
            };
            timer.Start();
        }, DispatcherPriority.Background);
    }

    private void UpdateSelectedAudioRecordingAnalysisStatus()
    {
        if (_isStandaloneAudioRecording || AudioRecordingStatusText is null)
        {
            return;
        }

        if (RecordingFilesListBox?.SelectedItem is AudioRecordingFileItem { Analysis: not null } item)
        {
            AudioRecordingStatusText.Text = $"{item.Name}: {item.Analysis.DetailSummary}.";
        }
    }

    private void UpdateSelectedKaraokeRecordingAnalysisStatus()
    {
        if (_isKaraokeVocalRecording || _karaokeRecordingPlaybackOutput is not null || KaraokeVocalStatusText is null)
        {
            return;
        }

        if (KaraokeRecordingFilesListBox?.SelectedItem is AudioRecordingFileItem { Analysis: not null } item)
        {
            KaraokeVocalStatusText.Text = $"{item.Name}: {item.Analysis.DetailSummary}.";
        }
    }

    private void RefreshAudioRecordingFiles(string? preferredPath = null)
    {
        if (RecordingFilesListBox is null)
        {
            return;
        }

        var selectedPath = preferredPath ?? GetSelectedAudioRecordingPath();
        var items = AudioRecordingCatalog.EnumerateRecordingFiles(
            _audioRecordingFolder,
            MaximumAnalyzedRecordingRows,
            EagerRecordingAnalysisDuration);

        _audioRecordingFiles.Clear();
        foreach (var item in items)
        {
            _audioRecordingFiles.Add(item);
        }

        AudioRecordingFileItem? selection = null;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            selection = _audioRecordingFiles.FirstOrDefault(item =>
                string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        }

        selection ??= _audioRecordingFiles.FirstOrDefault();
        RecordingFilesListBox.SelectedItem = selection;
        if (selection is not null)
        {
            RecordingFilesListBox.ScrollIntoView(selection);
        }
    }

    private void UpdateKaraokeRecordingFolderText()
    {
        if (KaraokeRecordingSaveLocationTextBox is not null)
        {
            KaraokeRecordingSaveLocationTextBox.Text = _karaokeRecordingFolder;
        }

        ConfigureKaraokeRecordingFolderWatcher();
        RefreshKaraokeRecordingFiles(_lastKaraokeRecordingPath);
    }

    private void ConfigureKaraokeRecordingFolderWatcher()
    {
        DisposeKaraokeRecordingFolderWatcher();

        try
        {
            _karaokeRecordingFolderWatcher = FileBrowserWatcher.Start(
                _karaokeRecordingFolder,
                includeSubdirectories: false,
                NotifyFilters.FileName | NotifyFilters.CreationTime,
                AudioRecordingCatalog.IsSupportedRecordingFile,
                QueueKaraokeRecordingFilesRefresh);
        }
        catch
        {
            _karaokeRecordingFolderWatcher = null;
        }
    }

    private void DisposeKaraokeRecordingFolderWatcher()
    {
        var watcher = _karaokeRecordingFolderWatcher;
        if (watcher is null)
        {
            return;
        }

        _karaokeRecordingFolderWatcher = null;
        watcher.Dispose();
    }

    private void QueueKaraokeRecordingFilesRefresh()
    {
        if (_isClosing || System.Threading.Interlocked.Exchange(ref _karaokeRecordingFolderRefreshQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var timer = new DispatcherTimer
            {
                Interval = AudioRecordingFolderRefreshDelay
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                System.Threading.Volatile.Write(ref _karaokeRecordingFolderRefreshQueued, 0);
                RefreshKaraokeRecordingFiles(_lastKaraokeRecordingPath);
                UpdateKaraokeTransportControls();
            };
            timer.Start();
        }, DispatcherPriority.Background);
    }

    private void RefreshKaraokeRecordingFiles(string? preferredPath = null)
    {
        if (KaraokeRecordingFilesListBox is null)
        {
            return;
        }

        var selectedPath = preferredPath ?? GetSelectedKaraokeRecordingPath();
        var items = AudioRecordingCatalog.EnumerateRecordingFiles(
            _karaokeRecordingFolder,
            MaximumAnalyzedRecordingRows,
            EagerRecordingAnalysisDuration);

        _karaokeRecordingFiles.Clear();
        foreach (var item in items)
        {
            _karaokeRecordingFiles.Add(item);
        }

        AudioRecordingFileItem? selection = null;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            selection = _karaokeRecordingFiles.FirstOrDefault(item =>
                string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        }

        selection ??= _karaokeRecordingFiles.FirstOrDefault();
        KaraokeRecordingFilesListBox.SelectedItem = selection;
        if (selection is not null)
        {
            KaraokeRecordingFilesListBox.ScrollIntoView(selection);
        }
    }

    private void ConfigureSessionFolderWatcher()
    {
        DisposeSessionFolderWatcher();

        try
        {
            _sessionFolderWatcher = FileBrowserWatcher.Start(
                _outputFolder,
                includeSubdirectories: true,
                NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
                SessionRecordingCatalog.IsSessionBrowserPath,
                QueueSessionRecordingsRefresh);
        }
        catch
        {
            _sessionFolderWatcher = null;
        }
    }

    private void DisposeSessionFolderWatcher()
    {
        var watcher = _sessionFolderWatcher;
        if (watcher is null)
        {
            return;
        }

        _sessionFolderWatcher = null;
        watcher.Dispose();
    }

    private void QueueSessionRecordingsRefresh()
    {
        if (_isClosing || System.Threading.Interlocked.Exchange(ref _sessionFolderRefreshQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var timer = new DispatcherTimer
            {
                Interval = AudioRecordingFolderRefreshDelay
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                System.Threading.Volatile.Write(ref _sessionFolderRefreshQueued, 0);
                RefreshSessionRecordings(_lastSessionRecordingPath);
                UpdateSessionPlaybackTransportControls();
            };
            timer.Start();
        }, DispatcherPriority.Background);
    }

    private void RefreshSessionRecordings(string? preferredPath = null)
    {
        if (SessionFilesListBox is null)
        {
            return;
        }

        var selectedPath = preferredPath ?? GetSelectedSessionRecordingPath();
        var items = SessionRecordingCatalog.EnumerateSessionRecordings(_outputFolder)
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .ToList();

        _sessionRecordings.Clear();
        foreach (var item in items)
        {
            _sessionRecordings.Add(item);
        }

        SessionRecordingItem? selection = null;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            selection = _sessionRecordings.FirstOrDefault(item =>
                string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        }

        selection ??= _sessionRecordings.FirstOrDefault();
        SessionFilesListBox.SelectedItem = selection;
        if (selection is not null)
        {
            SessionFilesListBox.ScrollIntoView(selection);
        }
    }

    private string? GetSelectedAudioRecordingPath()
    {
        return RecordingFilesListBox?.SelectedItem is AudioRecordingFileItem item
            ? item.Path
            : null;
    }

    private string? GetSelectedKaraokeRecordingPath()
    {
        return KaraokeRecordingFilesListBox?.SelectedItem is AudioRecordingFileItem item
            ? item.Path
            : null;
    }

    private SessionRecordingItem? GetSelectedSessionRecording()
    {
        return SessionFilesListBox?.SelectedItem as SessionRecordingItem;
    }

    private string? GetSelectedSessionRecordingPath()
    {
        return GetSelectedSessionRecording()?.Path;
    }

    private CameraProfile CaptureCameraProfile(string name)
    {
        return CameraProfileStore.Capture(
            name,
            CameraComboBox.SelectedItem as CameraDevice,
            CameraStatusText.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem),
            _isCameraEnabled,
            VideoDenoiseCheckBox?.IsChecked == true,
            VideoDenoiseSlider?.Value ?? _videoDenoiseSliderStrength,
            VideoColorPolishCheckBox?.IsChecked == true,
            new VideoFrameColorSettings(
                VideoColorPolishCheckBox?.IsChecked == true,
                VideoExposureSlider?.Value ?? _pendingVideoColorSettings.Exposure,
                VideoContrastSlider?.Value ?? _pendingVideoColorSettings.Contrast,
                VideoSaturationSlider?.Value ?? _pendingVideoColorSettings.Saturation,
                VideoWarmthSlider?.Value ?? _pendingVideoColorSettings.Warmth));
    }

    private CameraProfile? LoadCameraProfile(string name)
    {
        return CameraProfileStore.Load(CameraProfileFolder, name, UserPresetJsonOptions);
    }

    private void ApplyCameraProfile(CameraProfile profile)
    {
        var name = string.IsNullOrWhiteSpace(profile.Name) ? "Camera profile" : profile.Name.Trim();
        var camera = CameraProfileStore.FindCamera(
            CameraComboBox.Items.OfType<CameraDevice>(),
            profile);
        if (camera is null)
        {
            CameraProfileStatusText.Text = $"Camera profile loaded, but camera is not available: {profile.CameraName}";
            return;
        }

        _pendingCameraProfileModeLabel = string.IsNullOrWhiteSpace(profile.ModeLabel)
            ? CameraVideoMode.Auto.Label
            : profile.ModeLabel;
        CameraComboBox.SelectedItem = camera;

        if (VideoDenoiseCheckBox is not null)
        {
            VideoDenoiseCheckBox.IsChecked = profile.DenoiseEnabled;
        }

        if (VideoDenoiseSlider is not null && double.IsFinite(profile.DenoiseStrength))
        {
            VideoDenoiseSlider.Value = Math.Clamp(profile.DenoiseStrength, VideoDenoiseSlider.Minimum, VideoDenoiseSlider.Maximum);
        }

        if (VideoColorPolishCheckBox is not null)
        {
            VideoColorPolishCheckBox.IsChecked = profile.ColorPolishEnabled;
        }

        if (VideoExposureSlider is not null && double.IsFinite(profile.Exposure))
        {
            VideoExposureSlider.Value = Math.Clamp(profile.Exposure, VideoExposureSlider.Minimum, VideoExposureSlider.Maximum);
        }

        if (VideoContrastSlider is not null && double.IsFinite(profile.Contrast))
        {
            VideoContrastSlider.Value = Math.Clamp(profile.Contrast, VideoContrastSlider.Minimum, VideoContrastSlider.Maximum);
        }

        if (VideoSaturationSlider is not null && double.IsFinite(profile.Saturation))
        {
            VideoSaturationSlider.Value = Math.Clamp(profile.Saturation, VideoSaturationSlider.Minimum, VideoSaturationSlider.Maximum);
        }

        if (VideoWarmthSlider is not null && double.IsFinite(profile.Warmth))
        {
            VideoWarmthSlider.Value = Math.Clamp(profile.Warmth, VideoWarmthSlider.Minimum, VideoWarmthSlider.Maximum);
        }

        _isCameraEnabled = profile.CameraEnabled && _cameraAvailable;
        UpdateVideoDenoiseSettings(restartPreview: false);
        UpdateVideoColorPolishSettings();
        SelectPendingCameraProfileMode();
        UpdateCameraEnabledState();

        CameraProfileComboBox.SelectedItem = CameraProfileNames.FirstOrDefault(candidate =>
            candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        CameraProfileStatusText.Text = $"Camera profile loaded: {name}";
    }

    private void SelectPendingCameraProfileMode()
    {
        var modeLabel = _pendingCameraProfileModeLabel;
        if (CameraModeComboBox is null || string.IsNullOrWhiteSpace(modeLabel))
        {
            return;
        }

        var match = CameraModeComboBox.Items
            .OfType<CameraVideoMode>()
            .FirstOrDefault(mode => mode.Label.Equals(modeLabel, StringComparison.OrdinalIgnoreCase))
            ?? CameraModeComboBox.Items
                .OfType<CameraVideoMode>()
                .FirstOrDefault(mode => mode.IsAuto && modeLabel.Equals(CameraVideoMode.Auto.Label, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            CameraModeComboBox.SelectedItem = match;
            _pendingCameraProfileModeLabel = null;
            PersistAppState();
        }
    }

    private void LoadCameraProfileList()
    {
        var selectedName = CameraProfileComboBox.SelectedItem as string;
        CameraProfileNames.Clear();

        try
        {
            Directory.CreateDirectory(CameraProfileFolder);
            foreach (var path in Directory.EnumerateFiles(CameraProfileFolder, "*.json").OrderBy(path => path))
            {
                var name = CameraProfileStore.ReadName(path, UserPresetJsonOptions);
                if (!string.IsNullOrWhiteSpace(name)
                    && !CameraProfileNames.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    CameraProfileNames.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            CameraProfileStatusText.Text = $"Could not read camera profiles: {ex.Message}";
        }

        CameraProfileComboBox.SelectedItem = !string.IsNullOrWhiteSpace(selectedName)
            ? CameraProfileNames.FirstOrDefault(candidate => candidate.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
            : null;

        if (CameraProfileComboBox.SelectedItem is null)
        {
            CameraProfileComboBox.SelectedIndex = CameraProfileNames.Count > 0 ? 0 : -1;
        }
    }

    private string GetCameraProfileNameFromEntry()
    {
        var typed = CameraProfileNameTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(typed))
        {
            return typed;
        }

        return CameraProfileComboBox.SelectedItem as string ?? string.Empty;
    }

    private UserVoicePreset CaptureUserPreset(string name)
    {
        var preset = new UserVoicePreset
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(PresetDescriptionText.Text)
                ? $"User preset: {name}"
                : PresetDescriptionText.Text,
            AnalyzerSmoothing = _activeMicChannel?.AnalyzerSmoothing ?? DefaultAnalyzerSmoothingPercent,
            Bands = Bands
                .Select(band => new UserEqualizerBandPreset
                {
                    Label = band.Label,
                    FrequencyHz = band.CenterFrequencyHz,
                    GainDb = band.GainDb,
                    IsEnabled = band.IsEnabled
                })
                .ToList()
        };

        foreach (var property in UserPresetSettingProperties)
        {
            if (property.PropertyType == typeof(double))
            {
                var value = (double)(property.GetValue(Settings) ?? 0d);
                if (double.IsFinite(value))
                {
                    preset.NumberSettings[property.Name] = value;
                }
            }
            else if (property.PropertyType == typeof(bool))
            {
                preset.BooleanSettings[property.Name] = (bool)(property.GetValue(Settings) ?? false);
            }
        }

        return preset;
    }

    private UserVoicePreset? LoadUserPreset(string name)
    {
        var path = GetUserPresetPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UserVoicePreset>(json, UserPresetJsonOptions);
    }

    private void ApplyUserPreset(UserVoicePreset preset)
    {
        var booleanSettings = preset.BooleanSettings ?? [];
        var numberSettings = preset.NumberSettings ?? [];
        var bands = preset.Bands ?? [];

        foreach (var setting in booleanSettings)
        {
            SetUserPresetSetting(setting.Key, setting.Value);
        }

        foreach (var setting in numberSettings)
        {
            if (double.IsFinite(setting.Value))
            {
                SetUserPresetSetting(setting.Key, setting.Value);
            }
        }

        for (var i = 0; i < Bands.Count; i++)
        {
            if (i >= bands.Count)
            {
                Bands[i].IsEnabled = true;
                Bands[i].GainDb = 0d;
                continue;
            }

            Bands[i].IsEnabled = bands[i].IsEnabled;
            Bands[i].GainDb = Math.Clamp(bands[i].GainDb, -12d, 12d);
        }

        if (double.IsFinite(preset.AnalyzerSmoothing))
        {
            _activeMicChannel.AnalyzerSmoothing = Math.Clamp(preset.AnalyzerSmoothing, 0d, 100d);
        }
        else
        {
            _activeMicChannel.AnalyzerSmoothing = DefaultAnalyzerSmoothingPercent;
        }

        var name = string.IsNullOrWhiteSpace(preset.Name) ? "User preset" : preset.Name.Trim();
        UserPresetComboBox.SelectedItem = UserPresetNames.FirstOrDefault(candidate =>
            candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        SyncEqualizerSettings();
        SetProcessingSliderDefaults(string.IsNullOrWhiteSpace(preset.Description)
            ? $"User preset: {name}"
            : preset.Description);
        SetActivePresetButton(null);
        _lastLoadedPresetName = name;
        _lastLoadedPresetIsUserPreset = true;
        _activeMicChannel.ActivePresetName = name;
        _activeMicChannel.ActivePresetIsUserPreset = true;
        _activeMicChannel.PresetDescription = string.IsNullOrWhiteSpace(preset.Description)
            ? $"User preset: {name}"
            : preset.Description;
        StatusText.Text = $"User preset loaded: {name}";
        ConfigureLiveMixFromChannels();
        PersistAppState();
    }

    private void SetActivePresetButton(string? presetName)
    {
        SetPresetButtonState(FlatPresetButton, presetName, "Flat");
        SetPresetButtonState(PodcastCleanPresetButton, presetName, "Podcast Clean");
        SetPresetButtonState(WarmRadioPresetButton, presetName, "Warm Radio");
        SetPresetButtonState(DeepWarmPresetButton, presetName, "Deep Warm");
        SetPresetButtonState(NoisyRoomPresetButton, presetName, "Noisy Room");
        SetPresetButtonState(BrightHeadsetPresetButton, presetName, "Bright Headset");
    }

    private static void SetPresetButtonState(ToggleButton? button, string? activePresetName, string presetName)
    {
        if (button is not null)
        {
            button.IsChecked = activePresetName?.Equals(presetName, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    private void SetUserPresetSetting<T>(string name, T value)
    {
        var property = UserPresetSettingProperties.FirstOrDefault(candidate =>
            candidate.Name.Equals(name, StringComparison.Ordinal)
            && candidate.PropertyType == typeof(T));
        property?.SetValue(Settings, value);
    }

    private string GetUserPresetNameFromEntry()
    {
        var typed = UserPresetNameTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(typed))
        {
            return typed;
        }

        return UserPresetComboBox.SelectedItem as string ?? string.Empty;
    }

    private static string ReadUserPresetName(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var preset = JsonSerializer.Deserialize<UserVoicePreset>(json, UserPresetJsonOptions);
            if (!string.IsNullOrWhiteSpace(preset?.Name))
            {
                return preset.Name.Trim();
            }
        }
        catch
        {
            // Bad user preset files are ignored so the preset picker can still load.
        }

        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static string GetUserPresetPath(string name)
    {
        return System.IO.Path.Combine(UserPresetFolder, $"{SanitizePresetFileName(name)}.json");
    }

    private static string SanitizePresetFileName(string name)
    {
        var invalidCharacters = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());

        sanitized = sanitized.Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Preset";
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80].Trim();
    }

    private void SetProcessingSliderDefaults(string description)
    {
        PresetDescriptionText.Text = description;
        SetProcessingSliderDefault(InputTrimSlider, Settings.InputTrimDb);
        SetProcessingSliderDefault(HighPassSlider, Settings.HighPassFrequencyHz);
        SetProcessingSliderDefault(LowPassSlider, Settings.LowPassFrequencyHz);
        SetProcessingSliderDefault(HumRemovalFrequencySlider, Settings.HumRemovalFrequencyHz);
        SetProcessingSliderDefault(NotchFrequencySlider, Settings.NotchFilterFrequencyHz);
        SetProcessingSliderDefault(NotchDepthSlider, Settings.NotchFilterDepthDb);
        SetProcessingSliderDefault(NotchQSlider, Settings.NotchFilterQ);
        SetProcessingSliderDefault(ParametricEqFrequencySlider, Settings.ParametricEqFrequencyHz);
        SetProcessingSliderDefault(ParametricEqGainSlider, Settings.ParametricEqGainDb);
        SetProcessingSliderDefault(ParametricEqQSlider, Settings.ParametricEqQ);
        SetProcessingSliderDefault(LowShelfFrequencySlider, Settings.LowShelfFrequencyHz);
        SetProcessingSliderDefault(LowShelfGainSlider, Settings.LowShelfGainDb);
        SetProcessingSliderDefault(HighShelfFrequencySlider, Settings.HighShelfFrequencyHz);
        SetProcessingSliderDefault(HighShelfGainSlider, Settings.HighShelfGainDb);
        SetProcessingSliderDefault(NAudioLowPassFrequencySlider, Settings.NAudioLowPassFrequencyHz);
        SetProcessingSliderDefault(NAudioLowPassQSlider, Settings.NAudioLowPassQ);
        SetProcessingSliderDefault(NAudioHighPassFrequencySlider, Settings.NAudioHighPassFrequencyHz);
        SetProcessingSliderDefault(NAudioHighPassQSlider, Settings.NAudioHighPassQ);
        SetProcessingSliderDefault(NAudioBandPassPeakFrequencySlider, Settings.NAudioBandPassPeakFrequencyHz);
        SetProcessingSliderDefault(NAudioBandPassPeakQSlider, Settings.NAudioBandPassPeakQ);
        SetProcessingSliderDefault(NAudioBandPassSkirtFrequencySlider, Settings.NAudioBandPassSkirtFrequencyHz);
        SetProcessingSliderDefault(NAudioBandPassSkirtQSlider, Settings.NAudioBandPassSkirtQ);
        SetProcessingSliderDefault(NAudioNotchFrequencySlider, Settings.NAudioNotchFrequencyHz);
        SetProcessingSliderDefault(NAudioNotchQSlider, Settings.NAudioNotchQ);
        SetProcessingSliderDefault(NAudioAllPassFrequencySlider, Settings.NAudioAllPassFrequencyHz);
        SetProcessingSliderDefault(NAudioAllPassQSlider, Settings.NAudioAllPassQ);
        SetProcessingSliderDefault(NAudioPeakingEqFrequencySlider, Settings.NAudioPeakingEqFrequencyHz);
        SetProcessingSliderDefault(NAudioPeakingEqQSlider, Settings.NAudioPeakingEqQ);
        SetProcessingSliderDefault(NAudioPeakingEqGainSlider, Settings.NAudioPeakingEqGainDb);
        SetProcessingSliderDefault(NAudioLowShelfFrequencySlider, Settings.NAudioLowShelfFrequencyHz);
        SetProcessingSliderDefault(NAudioLowShelfSlopeSlider, Settings.NAudioLowShelfSlope);
        SetProcessingSliderDefault(NAudioLowShelfGainSlider, Settings.NAudioLowShelfGainDb);
        SetProcessingSliderDefault(NAudioHighShelfFrequencySlider, Settings.NAudioHighShelfFrequencyHz);
        SetProcessingSliderDefault(NAudioHighShelfSlopeSlider, Settings.NAudioHighShelfSlope);
        SetProcessingSliderDefault(NAudioHighShelfGainSlider, Settings.NAudioHighShelfGainDb);
        SetProcessingSliderDefault(NAudioPitchShiftSemitonesSlider, Settings.NAudioPitchShiftSemitones);
        SetProcessingSliderDefault(NAudioPitchShiftFftSizeSlider, Settings.NAudioPitchShiftFftSize);
        SetProcessingSliderDefault(NAudioPitchShiftOversamplingSlider, Settings.NAudioPitchShiftOversampling);
        SetProcessingSliderDefault(NAudioPitchShiftMixSlider, Settings.NAudioPitchShiftMix);
        SetProcessingSliderDefault(NAudioConvolutionLengthSlider, Settings.NAudioConvolutionLengthMs);
        SetProcessingSliderDefault(NAudioConvolutionPreDelaySlider, Settings.NAudioConvolutionPreDelayMs);
        SetProcessingSliderDefault(NAudioConvolutionDecaySlider, Settings.NAudioConvolutionDecay);
        SetProcessingSliderDefault(NAudioConvolutionMixSlider, Settings.NAudioConvolutionMix);
        SetProcessingSliderDefault(NAudioEnvelopeThresholdSlider, Settings.NAudioEnvelopeTriggerThresholdDb);
        SetProcessingSliderDefault(NAudioEnvelopeAttackSlider, Settings.NAudioEnvelopeAttackMs);
        SetProcessingSliderDefault(NAudioEnvelopeDecaySlider, Settings.NAudioEnvelopeDecayMs);
        SetProcessingSliderDefault(NAudioEnvelopeSustainSlider, Settings.NAudioEnvelopeSustainLevel);
        SetProcessingSliderDefault(NAudioEnvelopeReleaseSlider, Settings.NAudioEnvelopeReleaseMs);
        SetProcessingSliderDefault(NAudioEnvelopeMixSlider, Settings.NAudioEnvelopeMix);
        SetProcessingSliderDefault(NAudioDmoChorusWetDryMixSlider, Settings.NAudioDmoChorusWetDryMix);
        SetProcessingSliderDefault(NAudioDmoChorusDepthSlider, Settings.NAudioDmoChorusDepth);
        SetProcessingSliderDefault(NAudioDmoChorusFeedbackSlider, Settings.NAudioDmoChorusFeedback);
        SetProcessingSliderDefault(NAudioDmoChorusFrequencySlider, Settings.NAudioDmoChorusFrequencyHz);
        SetProcessingSliderDefault(NAudioDmoChorusWaveFormSlider, Settings.NAudioDmoChorusWaveForm);
        SetProcessingSliderDefault(NAudioDmoChorusDelaySlider, Settings.NAudioDmoChorusDelayMs);
        SetProcessingSliderDefault(NAudioDmoChorusPhaseSlider, Settings.NAudioDmoChorusPhase);
        SetProcessingSliderDefault(NAudioDmoFlangerWetDryMixSlider, Settings.NAudioDmoFlangerWetDryMix);
        SetProcessingSliderDefault(NAudioDmoFlangerDepthSlider, Settings.NAudioDmoFlangerDepth);
        SetProcessingSliderDefault(NAudioDmoFlangerFeedbackSlider, Settings.NAudioDmoFlangerFeedback);
        SetProcessingSliderDefault(NAudioDmoFlangerFrequencySlider, Settings.NAudioDmoFlangerFrequencyHz);
        SetProcessingSliderDefault(NAudioDmoFlangerWaveFormSlider, Settings.NAudioDmoFlangerWaveForm);
        SetProcessingSliderDefault(NAudioDmoFlangerDelaySlider, Settings.NAudioDmoFlangerDelayMs);
        SetProcessingSliderDefault(NAudioDmoFlangerPhaseSlider, Settings.NAudioDmoFlangerPhase);
        SetProcessingSliderDefault(NAudioDmoEchoWetDryMixSlider, Settings.NAudioDmoEchoWetDryMix);
        SetProcessingSliderDefault(NAudioDmoEchoFeedbackSlider, Settings.NAudioDmoEchoFeedback);
        SetProcessingSliderDefault(NAudioDmoEchoLeftDelaySlider, Settings.NAudioDmoEchoLeftDelayMs);
        SetProcessingSliderDefault(NAudioDmoEchoRightDelaySlider, Settings.NAudioDmoEchoRightDelayMs);
        SetProcessingSliderDefault(NAudioDmoEchoPanDelaySlider, Settings.NAudioDmoEchoPanDelay);
        SetProcessingSliderDefault(NAudioDmoDistortionGainSlider, Settings.NAudioDmoDistortionGainDb);
        SetProcessingSliderDefault(NAudioDmoDistortionEdgeSlider, Settings.NAudioDmoDistortionEdge);
        SetProcessingSliderDefault(NAudioDmoDistortionPostEqCenterSlider, Settings.NAudioDmoDistortionPostEqCenterFrequencyHz);
        SetProcessingSliderDefault(NAudioDmoDistortionPostEqBandwidthSlider, Settings.NAudioDmoDistortionPostEqBandwidthHz);
        SetProcessingSliderDefault(NAudioDmoDistortionPreLowPassSlider, Settings.NAudioDmoDistortionPreLowPassCutoffHz);
        SetProcessingSliderDefault(NAudioDmoCompressorGainSlider, Settings.NAudioDmoCompressorGainDb);
        SetProcessingSliderDefault(NAudioDmoCompressorAttackSlider, Settings.NAudioDmoCompressorAttackMs);
        SetProcessingSliderDefault(NAudioDmoCompressorReleaseSlider, Settings.NAudioDmoCompressorReleaseMs);
        SetProcessingSliderDefault(NAudioDmoCompressorThresholdSlider, Settings.NAudioDmoCompressorThresholdDb);
        SetProcessingSliderDefault(NAudioDmoCompressorRatioSlider, Settings.NAudioDmoCompressorRatio);
        SetProcessingSliderDefault(NAudioDmoCompressorPreDelaySlider, Settings.NAudioDmoCompressorPreDelayMs);
        SetProcessingSliderDefault(NAudioDmoParamEqCenterSlider, Settings.NAudioDmoParamEqCenterFrequencyHz);
        SetProcessingSliderDefault(NAudioDmoParamEqBandwidthSlider, Settings.NAudioDmoParamEqBandwidthHz);
        SetProcessingSliderDefault(NAudioDmoParamEqGainSlider, Settings.NAudioDmoParamEqGainDb);
        SetProcessingSliderDefault(NAudioDmoGargleRateSlider, Settings.NAudioDmoGargleRateHz);
        SetProcessingSliderDefault(NAudioDmoGargleWaveShapeSlider, Settings.NAudioDmoGargleWaveShape);
        SetProcessingSliderDefault(NAudioDmoI3DL2RoomSlider, Settings.NAudioDmoI3DL2Room);
        SetProcessingSliderDefault(NAudioDmoI3DL2RoomHfSlider, Settings.NAudioDmoI3DL2RoomHf);
        SetProcessingSliderDefault(NAudioDmoI3DL2RolloffSlider, Settings.NAudioDmoI3DL2RoomRolloffFactor);
        SetProcessingSliderDefault(NAudioDmoI3DL2DecayTimeSlider, Settings.NAudioDmoI3DL2DecayTimeSeconds);
        SetProcessingSliderDefault(NAudioDmoI3DL2DecayHfRatioSlider, Settings.NAudioDmoI3DL2DecayHfRatio);
        SetProcessingSliderDefault(NAudioDmoI3DL2ReflectionsSlider, Settings.NAudioDmoI3DL2Reflections);
        SetProcessingSliderDefault(NAudioDmoI3DL2ReflectionsDelaySlider, Settings.NAudioDmoI3DL2ReflectionsDelaySeconds);
        SetProcessingSliderDefault(NAudioDmoI3DL2ReverbSlider, Settings.NAudioDmoI3DL2Reverb);
        SetProcessingSliderDefault(NAudioDmoI3DL2ReverbDelaySlider, Settings.NAudioDmoI3DL2ReverbDelaySeconds);
        SetProcessingSliderDefault(NAudioDmoI3DL2DiffusionSlider, Settings.NAudioDmoI3DL2Diffusion);
        SetProcessingSliderDefault(NAudioDmoI3DL2DensitySlider, Settings.NAudioDmoI3DL2Density);
        SetProcessingSliderDefault(NAudioDmoI3DL2HfReferenceSlider, Settings.NAudioDmoI3DL2HfReferenceHz);
        SetProcessingSliderDefault(NAudioDmoI3DL2QualitySlider, Settings.NAudioDmoI3DL2Quality);
        SetProcessingSliderDefault(NAudioDmoWavesReverbInGainSlider, Settings.NAudioDmoWavesReverbInGainDb);
        SetProcessingSliderDefault(NAudioDmoWavesReverbMixSlider, Settings.NAudioDmoWavesReverbMixDb);
        SetProcessingSliderDefault(NAudioDmoWavesReverbTimeSlider, Settings.NAudioDmoWavesReverbTimeMs);
        SetProcessingSliderDefault(NAudioDmoWavesReverbHighFreqRatioSlider, Settings.NAudioDmoWavesReverbHighFreqRtRatio);
        SetProcessingSliderDefault(DePopperSlider, Settings.DePopperAmountDb);
        SetProcessingSliderDefault(DePopperFrequencySlider, Settings.DePopperFrequencyHz);
        SetProcessingSliderDefault(DePopperThresholdSlider, Settings.DePopperThresholdDb);
        SetProcessingSliderDefault(GateThresholdSlider, Settings.NoiseGateThresholdDb);
        SetProcessingSliderDefault(GateAttackSlider, Settings.NoiseGateAttackMs);
        SetProcessingSliderDefault(GateHoldSlider, Settings.NoiseGateHoldMs);
        SetProcessingSliderDefault(GateReleaseSlider, Settings.NoiseGateReleaseMs);
        SetProcessingSliderDefault(GateRangeSlider, Settings.NoiseGateRangeDb);
        SetProcessingSliderDefault(ExpanderThresholdSlider, Settings.ExpanderThresholdDb);
        SetProcessingSliderDefault(ExpanderRatioSlider, Settings.ExpanderRatio);
        SetProcessingSliderDefault(ExpanderRangeSlider, Settings.ExpanderRangeDb);
        SetProcessingSliderDefault(ExpanderAttackSlider, Settings.ExpanderAttackMs);
        SetProcessingSliderDefault(ExpanderHoldSlider, Settings.ExpanderHoldMs);
        SetProcessingSliderDefault(ExpanderReleaseSlider, Settings.ExpanderReleaseMs);
        SetProcessingSliderDefault(NoiseSuppressionSlider, Settings.NoiseSuppressionAmountDb);
        SetProcessingSliderDefault(NoiseSuppressionSensitivitySlider, Settings.NoiseSuppressionSensitivity);
        SetProcessingSliderDefault(EchoReducerSlider, Settings.EchoReducerAmountDb);
        SetProcessingSliderDefault(EchoReducerSensitivitySlider, Settings.EchoReducerSensitivity);
        SetProcessingSliderDefault(CompressorThresholdSlider, Settings.CompressorThresholdDb);
        SetProcessingSliderDefault(CompressorRatioSlider, Settings.CompressorRatio);
        SetProcessingSliderDefault(CompressorAttackSlider, Settings.CompressorAttackMs);
        SetProcessingSliderDefault(CompressorReleaseSlider, Settings.CompressorReleaseMs);
        SetProcessingSliderDefault(CompressorKneeSlider, Settings.CompressorKneeDb);
        SetProcessingSliderDefault(DeEsserSlider, Settings.DeEsserAmountDb);
        SetProcessingSliderDefault(DeEsserFrequencySlider, Settings.DeEsserFrequencyHz);
        SetProcessingSliderDefault(DeEsserThresholdSlider, Settings.DeEsserThresholdDb);
        SetProcessingSliderDefault(DeEsserRangeSlider, Settings.DeEsserRangeDb);
        SetProcessingSliderDefault(BreathReducerSlider, Settings.BreathReducerAmountDb);
        SetProcessingSliderDefault(BreathReducerSensitivitySlider, Settings.BreathReducerSensitivity);
        SetProcessingSliderDefault(PresenceEnhancerSlider, Settings.PresenceEnhancerAmountDb);
        SetProcessingSliderDefault(PresenceFrequencySlider, Settings.PresenceEnhancerFrequencyHz);
        SetProcessingSliderDefault(PresenceWidthSlider, Settings.PresenceEnhancerWidthHz);
        SetProcessingSliderDefault(SaturationSlider, Settings.SaturationAmount);
        SetProcessingSliderDefault(MakeupGainSlider, Settings.MakeupGainDb);
        SetProcessingSliderDefault(LimiterCeilingSlider, Settings.LimiterCeilingDb);
        SetProcessingSliderDefault(LimiterDriveSlider, Settings.LimiterSoftClipDriveDb);
        SetProcessingSliderDefault(LimiterLookaheadSlider, Settings.LimiterLookaheadMs);
        SetProcessingSliderDefault(LimiterReleaseSlider, Settings.LimiterReleaseMs);
    }

    private void SetProcessingSliderDefault(Slider slider, double value)
    {
        _processingSliderDefaults[slider] = value;
        slider.Ticks = [value];
        if (!_processingSliderBaseToolTips.TryGetValue(slider, out var baseText))
        {
            baseText = slider.ToolTip is TextBlock textBlock
                ? textBlock.Text
                : slider.ToolTip?.ToString() ?? "Drag near the yellow preset tick to return home.";
            _processingSliderBaseToolTips[slider] = baseText;
        }

        slider.ToolTip = CreateWrappedToolTip($"{baseText} Preset default: {value:0.##}. Drag near the yellow tick to snap back.");
    }

    private static TextBlock CreateWrappedToolTip(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        };
    }

    private static ObservableCollection<MicChannelStrip> CreateDefaultMicChannels()
    {
        var channels = new ObservableCollection<MicChannelStrip>
        {
            new MicChannelStrip(
                SystemAudioLoopbackChannelNumber,
                "Computer Audio",
                CreateDefaultEqualizerBands())
            {
                IsEnabled = true,
                IsMuted = true,
                InputGainDb = -6d,
                InputChannelMode = InputChannelMode.StereoPair
            }
        };

        for (var channelNumber = 1; channelNumber <= MaximumMicChannelCount; channelNumber++)
        {
            channels.Add(new MicChannelStrip(
                channelNumber,
                $"Mic {channelNumber}",
                CreateDefaultEqualizerBands())
            {
                IsEnabled = true,
                InputChannelMode = channelNumber == 2 ? InputChannelMode.Input2Right : InputChannelMode.MonoSum
            });
        }

        return channels;
    }

    private static ObservableCollection<EqualizerBand> CreateDefaultEqualizerBands()
    {
        return
        [
            new EqualizerBand("31", 31),
            new EqualizerBand("45", 45),
            new EqualizerBand("63", 63),
            new EqualizerBand("90", 90),
            new EqualizerBand("125", 125),
            new EqualizerBand("180", 180),
            new EqualizerBand("250", 250),
            new EqualizerBand("355", 355),
            new EqualizerBand("500", 500),
            new EqualizerBand("710", 710),
            new EqualizerBand("1k", 1000),
            new EqualizerBand("1.4k", 1400),
            new EqualizerBand("2k", 2000),
            new EqualizerBand("2.8k", 2800),
            new EqualizerBand("4k", 4000),
            new EqualizerBand("5.6k", 5600),
            new EqualizerBand("8k", 8000),
            new EqualizerBand("11k", 11200),
            new EqualizerBand("16k", 16000),
            new EqualizerBand("20k", 20000)
        ];
    }

    private sealed class CoreAudioSessionControlItem : INotifyPropertyChanged
    {
        private bool _isMuted;
        private double _volumePercent;

        public CoreAudioSessionControlItem(CoreAudioSessionSnapshot snapshot)
        {
            DisplayTitle = snapshot.DisplayTitle;
            State = snapshot.State;
            ProcessId = snapshot.ProcessId;
            ProcessName = snapshot.ProcessName;
            ControlTargets = snapshot.ControlTargets;
            SessionCount = snapshot.SessionCount;
            PeakDisplayText = FormatPeak(snapshot.PeakLevel);
            PeakLevelPercent = Math.Clamp(snapshot.PeakLevel * 100d, 0d, 100d);
            ProcessDisplayText = CoreAudioSessionCatalog.FormatProcessIdentity(snapshot);
            _isMuted = snapshot.IsMuted;
            _volumePercent = Math.Clamp(snapshot.Volume * 100d, 0d, 100d);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DisplayTitle { get; }

        public string State { get; }

        public int ProcessId { get; }

        public string ProcessName { get; }

        public string PeakDisplayText { get; }

        public double PeakLevelPercent { get; }

        public string ProcessDisplayText { get; }

        public IReadOnlyList<CoreAudioSessionControlTarget> ControlTargets { get; }

        public int SessionCount { get; }

        public string Details => $"{ProcessDisplayText} | {SessionCountText}{State} | {(IsMuted ? "muted" : VolumeDisplayText)} | peak {PeakDisplayText}";

        private string SessionCountText => SessionCount > 1
            ? $"{SessionCount} sessions | "
            : string.Empty;

        public string VolumeDisplayText => $"{VolumePercent:0}%";

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (SetField(ref _isMuted, value))
                {
                    OnPropertyChanged(nameof(Details));
                }
            }
        }

        public double VolumePercent
        {
            get => _volumePercent;
            set
            {
                var normalized = Math.Clamp(double.IsFinite(value) ? value : 0d, 0d, 100d);
                if (SetField(ref _volumePercent, normalized))
                {
                    OnPropertyChanged(nameof(VolumeDisplayText));
                    OnPropertyChanged(nameof(Details));
                }
            }
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string FormatPeak(float peakLevel)
        {
            if (peakLevel <= 0f)
            {
                return "-inf dB";
            }

            return $"{20d * Math.Log10(Math.Clamp(peakLevel, float.Epsilon, 1f)):0.0} dB";
        }
    }

    private sealed class MicChannelStrip : INotifyPropertyChanged
    {
        private string _displayName;
        private AudioInputDevice? _selectedDevice;
        private InputChannelMode _inputChannelMode = InputChannelMode.MonoSum;
        private bool _isEnabled = true;
        private bool _isSelected;
        private bool _isMuted;
        private bool _isSoloed;
        private bool _polarityInverted;
        private double _volumePercent = 100d;
        private double _inputGainDb;
        private double _pan;
        private double _delayMilliseconds;
        private string _activePresetName = "Custom";
        private bool _activePresetIsUserPreset;
        private string _presetDescription = "Custom channel settings.";
        private double _analyzerSmoothing = 80d;
        private double _levelMeterScale;
        private double _rmsMeterScale;
        private double _peakHoldMeterScale;
        private bool _isClipping;
        private int _clipHoldFrames;
        private int _peakHoldFrames;
        private string _syncStatusText = "Primary capture";

        public MicChannelStrip(int channelNumber, string displayName, ObservableCollection<EqualizerBand> bands)
        {
            ChannelNumber = channelNumber;
            DefaultDisplayName = displayName;
            _displayName = displayName;
            Bands = bands;
            ProcessorSettings = new VoiceProcessorSettings();
        }

        public int ChannelNumber { get; }

        public string DefaultDisplayName { get; }

        public bool IsSystemAudioLoopbackChannel => ChannelNumber == SystemAudioLoopbackChannelNumber;

        public ObservableCollection<EqualizerBand> Bands { get; }

        public VoiceProcessorSettings ProcessorSettings { get; }

        public string DisplayName
        {
            get => _displayName;
            set => SetField(ref _displayName, string.IsNullOrWhiteSpace(value) ? DefaultDisplayName : value.Trim());
        }

        public AudioInputDevice? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (Equals(_selectedDevice, value))
                {
                    return;
                }

                _selectedDevice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RouteText));
            }
        }

        public InputChannelMode InputChannelMode
        {
            get => _inputChannelMode;
            set
            {
                if (_inputChannelMode == value)
                {
                    return;
                }

                _inputChannelMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RouteText));
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetField(ref _isEnabled, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool IsMuted
        {
            get => _isMuted;
            set => SetField(ref _isMuted, value);
        }

        public bool IsSoloed
        {
            get => _isSoloed;
            set => SetField(ref _isSoloed, value);
        }

        public bool PolarityInverted
        {
            get => _polarityInverted;
            set => SetField(ref _polarityInverted, value);
        }

        public double VolumePercent
        {
            get => _volumePercent;
            set
            {
                var normalized = Math.Clamp(double.IsFinite(value) ? value : 100d, 0d, 150d);
                if (Math.Abs(_volumePercent - normalized) < 0.01d)
                {
                    return;
                }

                _volumePercent = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeDisplayText));
            }
        }

        public double InputGainDb
        {
            get => _inputGainDb;
            set
            {
                var normalized = Math.Clamp(double.IsFinite(value) ? value : 0d, -24d, 24d);
                if (Math.Abs(_inputGainDb - normalized) < 0.01d)
                {
                    return;
                }

                _inputGainDb = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InputGainDisplayText));
            }
        }

        public double Pan
        {
            get => _pan;
            set
            {
                var normalized = Math.Clamp(double.IsFinite(value) ? value : 0d, -100d, 100d);
                if (Math.Abs(_pan - normalized) < 0.01d)
                {
                    return;
                }

                _pan = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PanDisplayText));
            }
        }

        public double DelayMilliseconds
        {
            get => _delayMilliseconds;
            set
            {
                var normalized = Math.Clamp(double.IsFinite(value) ? value : 0d, 0d, 250d);
                if (Math.Abs(_delayMilliseconds - normalized) < 0.01d)
                {
                    return;
                }

                _delayMilliseconds = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DelayDisplayText));
            }
        }

        public string ActivePresetName
        {
            get => _activePresetName;
            set => SetField(ref _activePresetName, string.IsNullOrWhiteSpace(value) ? "Custom" : value.Trim());
        }

        public bool ActivePresetIsUserPreset
        {
            get => _activePresetIsUserPreset;
            set => SetField(ref _activePresetIsUserPreset, value);
        }

        public string PresetDescription
        {
            get => _presetDescription;
            set => SetField(ref _presetDescription, string.IsNullOrWhiteSpace(value) ? $"{DefaultDisplayName} preset" : value.Trim());
        }

        public double AnalyzerSmoothing
        {
            get => _analyzerSmoothing;
            set => SetField(ref _analyzerSmoothing, Math.Clamp(double.IsFinite(value) ? value : 80d, 0d, 100d));
        }

        private string RouteModeText => InputChannelModeInfo.GetDisplayLabel(InputChannelMode);

        public string RouteText => SelectedDevice is null
            ? $"{RouteModeText} | no source"
            : $"{SelectedDevice.Name} | {RouteModeText}";

        public string VolumeDisplayText => $"{VolumePercent:0}%";

        public string InputGainDisplayText => $"{InputGainDb:+0.0;-0.0;0.0} dB";

        public string PanDisplayText => Pan < -0.5d
            ? $"L {Math.Abs(Pan):0}"
            : Pan > 0.5d
            ? $"R {Pan:0}"
            : "C";

        public string DelayDisplayText => $"{DelayMilliseconds:0} ms";

        public double LevelMeterScale => _levelMeterScale;

        public double RmsMeterScale => _rmsMeterScale;

        public double PeakHoldMeterScale => _peakHoldMeterScale;

        public bool IsClipping => _isClipping;

        public string SyncStatusText
        {
            get => _syncStatusText;
            private set => SetField(ref _syncStatusText, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => DisplayName;

        public void UpdateLevelMeter(double peakLevel)
        {
            UpdateLevelMeter(peakLevel, peakLevel * 0.6d);
        }

        public void UpdateLevelMeter(double peakLevel, double rmsLevel)
        {
            var target = Math.Clamp((double.IsFinite(peakLevel) ? peakLevel : 0d) / 0.85d, 0d, 1d);
            var rmsTarget = Math.Clamp((double.IsFinite(rmsLevel) ? rmsLevel : 0d) / 0.45d, 0d, 1d);
            var amount = target > _levelMeterScale ? 0.42d : 0.14d;
            var next = _levelMeterScale + (target - _levelMeterScale) * amount;
            var rmsNext = _rmsMeterScale + (rmsTarget - _rmsMeterScale) * (rmsTarget > _rmsMeterScale ? 0.30d : 0.10d);
            double peakHoldNext;
            if (target >= _peakHoldMeterScale)
            {
                peakHoldNext = target;
                _peakHoldFrames = 42;
            }
            else if (_peakHoldFrames > 0)
            {
                _peakHoldFrames--;
                peakHoldNext = _peakHoldMeterScale;
            }
            else
            {
                peakHoldNext = _peakHoldMeterScale + (target - _peakHoldMeterScale) * 0.035d;
            }

            if (Math.Abs(next - _levelMeterScale) < 0.004d)
            {
                next = target <= 0.004d ? 0d : next;
            }

            if (Math.Abs(rmsNext - _rmsMeterScale) < 0.004d)
            {
                rmsNext = rmsTarget <= 0.004d ? 0d : rmsNext;
            }

            if (Math.Abs(peakHoldNext - _peakHoldMeterScale) < 0.003d)
            {
                peakHoldNext = target <= 0.004d && _peakHoldFrames <= 0 ? 0d : peakHoldNext;
            }

            if (peakLevel >= 0.98d)
            {
                _clipHoldFrames = 18;
            }
            else if (_clipHoldFrames > 0)
            {
                _clipHoldFrames--;
            }

            var clipping = _clipHoldFrames > 0;
            if (Math.Abs(next - _levelMeterScale) < 0.002d
                && Math.Abs(rmsNext - _rmsMeterScale) < 0.002d
                && Math.Abs(peakHoldNext - _peakHoldMeterScale) < 0.002d
                && clipping == _isClipping)
            {
                return;
            }

            _levelMeterScale = Math.Clamp(next, 0d, 1d);
            _rmsMeterScale = Math.Clamp(rmsNext, 0d, 1d);
            _peakHoldMeterScale = Math.Clamp(peakHoldNext, 0d, 1d);
            _isClipping = clipping;
            OnPropertyChanged(nameof(LevelMeterScale));
            OnPropertyChanged(nameof(RmsMeterScale));
            OnPropertyChanged(nameof(PeakHoldMeterScale));
            OnPropertyChanged(nameof(IsClipping));
        }

        public void ClearLevelMeter()
        {
            if (_levelMeterScale <= 0d
                && _rmsMeterScale <= 0d
                && _peakHoldMeterScale <= 0d
                && !_isClipping
                && _clipHoldFrames == 0
                && _peakHoldFrames == 0)
            {
                return;
            }

            _levelMeterScale = 0d;
            _rmsMeterScale = 0d;
            _peakHoldMeterScale = 0d;
            _isClipping = false;
            _clipHoldFrames = 0;
            _peakHoldFrames = 0;
            OnPropertyChanged(nameof(LevelMeterScale));
            OnPropertyChanged(nameof(RmsMeterScale));
            OnPropertyChanged(nameof(PeakHoldMeterScale));
            OnPropertyChanged(nameof(IsClipping));
        }

        public void UpdateSyncStatus(double bufferedMilliseconds, double targetMilliseconds, int underflowCount, int driftTrimCount)
        {
            var text = targetMilliseconds <= 0.01d
                ? "Primary capture"
                : $"Aux buffer {bufferedMilliseconds:0} ms / target {targetMilliseconds:0} ms, underflows {underflowCount}, trims {driftTrimCount}";
            SyncStatusText = text;
        }

        public void ResetMixerControls()
        {
            IsMuted = false;
            IsSoloed = false;
            PolarityInverted = false;
            VolumePercent = 100d;
            InputGainDb = 0d;
            Pan = 0d;
            DelayMilliseconds = 0d;
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class UserVoicePreset
    {
        public int Version { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public double AnalyzerSmoothing { get; set; } = 80d;

        public List<UserEqualizerBandPreset> Bands { get; set; } = [];

        public Dictionary<string, double> NumberSettings { get; set; } = [];

        public Dictionary<string, bool> BooleanSettings { get; set; } = [];
    }

    private sealed class UserEqualizerBandPreset
    {
        public string Label { get; set; } = string.Empty;

        public double FrequencyHz { get; set; }

        public double GainDb { get; set; }

        public bool IsEnabled { get; set; } = true;
    }

    private sealed record VoiceZone(string Name, double StartFrequencyHz, double EndFrequencyHz, string Description);

    private sealed record InputChannelOption(InputChannelMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record KaraokeTrackItem(string Path, string Name, string Artist, TimeSpan Duration, string DurationText);

    private sealed record KaraokeBrowserFileItem(string Path, string Name, string Details);

    private readonly record struct KaraokeMp4AtomRange(int Start, int End, int Depth);

    private sealed record KaraokeLyricLineItem(string Text, TimeSpan? Start, TimeSpan? End, List<KaraokeLyricToken> Tokens);

    private sealed record KaraokeTimedLyricLine(TimeSpan Start, string Text, IReadOnlyList<KaraokeLyricToken>? Tokens = null);

    private sealed record KaraokeLyricToken(string Text, TimeSpan? Start, TimeSpan? End, bool IsSingable);

    private sealed record KaraokeRawLyricToken(string Text, bool IsSingable);

    private sealed record KaraokeAudioEnergyWindow(double TimeSeconds, double Energy);

    private sealed record KaraokeAiToolchain(string? PythonPath, string? DemucsPath, string? WhisperXPath);

    private sealed record KaraokeDetectedLyrics(string LyricsText, int LineCount, int TimedTokenCount);

    private sealed record KaraokeDetectedWord(string Text, TimeSpan Start, TimeSpan End, IReadOnlyList<KaraokeDetectedCharacter>? Characters = null);

    private sealed record KaraokeDetectedCharacter(string Text, TimeSpan? Start, TimeSpan? End);

}
