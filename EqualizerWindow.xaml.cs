using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
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
using PodcastWorkbench.Audio;
using PodcastWorkbench.Video;
using ShapePath = System.Windows.Shapes.Path;

namespace PodcastWorkbench;

public partial class EqualizerWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const uint SeeMaskInvokeIdList = 0x0000000C;
    private const int SwShowNormal = 1;
    private const double MinimumDisplayFrequency = 40d;
    private const double MaximumDisplayFrequency = 20000d;
    private const double EqualizerHoverQ = 1.35d;
    private const int DefaultWaveformSampleRate = 44100;
    private const int MaximumWaveformHistorySeconds = 3;
    private const double MicAnalysisDurationSeconds = 8d;
    private const double SequentialMicAnalysisPhaseSeconds = 5d;
    private const double ExpandedControlRailWidth = 360d;
    private const double CollapsedControlRailWidth = 44d;
    private const double RightControlRailWidth = 360d;
    private const double EqualizerFaceplateOuterGap = 18d;
    private const double DenoiseDefaultStrength = 2d;
    private const double DenoiseMaximumStrength = 5d;
    private static readonly TimeSpan AudioRecordingFolderRefreshDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AudioDeviceFormatPollInterval = TimeSpan.FromSeconds(2);
    private static readonly Regex PodcastSessionFolderRegex = new(@"^Podcast_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex NumberedRecordingFileRegex = new(@"^(?:video|mix|raw_backup)_(?<number>\d{3,})\.(?:mp4|wav)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KaraokeLyricTimestampRegex = new(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex KaraokeInlineLyricTimestampRegex = new(@"<(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?>", RegexOptions.Compiled);
    private static readonly string UserPresetFolder = AppStoragePaths.UserPresetFolder;
    private static readonly string CameraProfileFolder = AppStoragePaths.CameraProfileFolder;
    private static readonly string KaraokeAiToolsFolder = System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "Tools", "KaraokeAi");
    private static readonly string KaraokeAiWorkFolder = System.IO.Path.Combine(AppStoragePaths.SettingsFolder, "KaraokeAiWork");
    private static readonly string DefaultKaraokeRecordingFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Podcast Workbench Karaoke Recordings");
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
    private readonly MicrophoneSpectrumService _spectrumService = new();
    private readonly MediaFoundationCameraPreviewService _cameraPreviewService = new();
    private readonly DirectShowCameraPreviewService _directShowPreviewService = new();
    private readonly MediaFoundationCameraModeService _cameraModeService = new();
    private readonly DirectShowCameraControlService _cameraControlService = new();
    private readonly AppSettingsState _appSettings = AppStateStore.LoadSettings();
    private readonly AppStartupRecovery _startupRecovery = AppStateStore.StartupRecovery;
    private readonly DispatcherTimer _audioDeviceFormatTimer = new();
    private readonly DispatcherTimer _sessionPlaybackPositionTimer = new();
    private readonly List<Line> _gridLines = [];
    private readonly List<Line> _waveformGridLines = [];
    private readonly object _waveformLock = new();
    private readonly object _micAnalysisLock = new();
    private readonly Queue<float> _rawWaveformHistory = new();
    private readonly Queue<float> _processedWaveformHistory = new();
    private int _waveformSampleRate = DefaultWaveformSampleRate;
    private readonly List<Border> _zoneBands = [];
    private readonly List<Border> _zoneLabels = [];
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
    private readonly ShapePath _rawWaveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(167, 176, 188)),
        StrokeThickness = 1.5,
        Opacity = 0.68,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly ShapePath _processedWaveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 2.5,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly ShapePath _recordingSignalTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 2,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly ShapePath _recordingRawSignalTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(167, 176, 188)),
        StrokeThickness = 1.5,
        Opacity = 0.68,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly SolidColorBrush _recordingDspActiveBrush = new(Color.FromRgb(66, 215, 125));
    private readonly SolidColorBrush _recordingNaturalAudioBrush = new(Color.FromRgb(215, 178, 32));
    private readonly SolidColorBrush _waveformGridBrush = new(Color.FromRgb(36, 45, 54));
    private readonly SolidColorBrush _waveformCenterGridBrush = new(Color.FromRgb(83, 101, 117));
    private readonly SolidColorBrush _meterMutedBrush = new(Color.FromRgb(105, 132, 156));
    private readonly SolidColorBrush _meterTextMutedBrush = new(Color.FromRgb(184, 199, 217));
    private readonly SolidColorBrush _meterGoodBrush = new(Color.FromRgb(66, 215, 125));
    private readonly SolidColorBrush _meterWarnBrush = new(Color.FromRgb(215, 178, 32));
    private readonly SolidColorBrush _meterDangerBrush = new(Color.FromRgb(218, 80, 72));
    private SpectrumFrame? _latestFrame;
    private double[] _renderedMagnitudes = [];
    private double[] _renderedRawMagnitudes = [];
    private double[] _renderedRecordingMagnitudes = [];
    private double[] _renderedRecordingRawMagnitudes = [];
    private double[] _renderedInput1Magnitudes = [];
    private double[] _renderedInput2Magnitudes = [];
    private double _visualCeiling = 0.25d;
    private double _recordingVisualCeiling = 0.25d;
    private double _displayInputPeak;
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
    private bool _isSnappingVideoDenoiseSlider;
    private AudioInputDevice? _selectedDevice;
    private AudioOutputDevice? _selectedOutputDevice;
    private AudioDeviceFormat? _selectedDeviceFormat;
    private bool _isRestartingAudioStream;
    private bool _isCheckingAudioDeviceFormat;
    private bool _isClosing;
    private bool _isRestoringAppState;
    private bool _safeStartCameraRecoveryActive;
    private bool _safeStartDx12Disabled;
    private bool _isUpdatingOutputRoutingUi;
    private int _audioStreamOperationVersion;
    private InputChannelMode _selectedInputChannelMode = InputChannelMode.MonoSum;
    private string _outputFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Podcast Workbench Sessions");
    private string _audioRecordingFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Podcast Workbench Audio Recordings");
    private string? _activeAudioRecordingPath;
    private DateTime _audioRecordingStartedAt;
    private DateTime _audioRecordingPausedAt;
    private TimeSpan _audioRecordingPausedDuration;
    private bool _isStandaloneAudioRecording;
    private bool _isStandaloneAudioRecordingPaused;
    private string? _lastAudioRecordingPath;
    private readonly ObservableCollection<AudioRecordingFileItem> _audioRecordingFiles = [];
    private FileBrowserWatcher? _audioRecordingFolderWatcher;
    private int _audioRecordingFolderRefreshQueued;
    private WaveOutEvent? _audioPlaybackOutput;
    private AudioFileReader? _audioPlaybackReader;
    private string? _audioPlaybackPath;
    private bool _isStoppingAudioPlayback;
    private readonly DispatcherTimer _karaokePlaybackPositionTimer = new();
    private WaveOutEvent? _karaokeTrackOutput;
    private AudioFileReader? _karaokeTrackReader;
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
    private WaveOutEvent? _karaokeRecordingPlaybackOutput;
    private AudioFileReader? _karaokeRecordingPlaybackReader;
    private string? _karaokeRecordingPlaybackPath;
    private bool _isStoppingKaraokeRecordingPlayback;
    private readonly ObservableCollection<KaraokeTrackItem> _karaokeQueue = [];
    private readonly ObservableCollection<KaraokeBrowserFileItem> _karaokeBrowserFiles = [];
    private readonly ObservableCollection<KaraokeLyricLineItem> _karaokeLyricDisplayLines = [];
    private List<KaraokeTimedLyricLine> _karaokeTimedLyrics = [];
    private int _karaokeActiveLineIndex = -1;
    private int _karaokeActiveTokenIndex = -1;
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
    private bool _isCameraFrameUpdateQueued;
    private bool _isCameraServiceStopPending;
    private bool _isDirectShowPreviewActive;
    private int _cameraServiceStopOperationVersion;
    private bool _pendingVideoDenoiseEnabled;
    private double _pendingVideoDenoiseStrength = DenoiseDefaultStrength;
    private double _videoDenoiseSliderStrength = DenoiseDefaultStrength;
    private VideoFrameColorSettings _pendingVideoColorSettings = VideoFrameColorSettings.Off;
    private CameraFrame? _pendingCameraFrame;
    private WriteableBitmap? _cameraPreviewBitmap;
    private Dx12Camera? _dx12Camera;
    private TextureNativeFrameInfo? _pendingTextureNativeFrameInfo;
    private Direct3D12PreviewHost? _direct3D12PreviewHost;
    private string? _lastTextureNativeCameraError;
    private TextureNativeCameraRecordingSession? _textureNativeRecordingSession;
    private TextureNativeRecordingResult? _lastTextureNativeRecordingResult;
    private string? _lastPreviewRecordingDiagnostics;
    private int _textureNativeFrameUpdateQueued;
    private long _cameraBgraPreviewFrameNumber;
    private CancellationTokenSource? _cameraModeLoadCancellation;
    private SpectrumFrame? _pendingSpectrumFrame;
    private int _spectrumFrameUpdateQueued;
    private Waveform3DWindow? _waveform3DWindow;
    private EqualizerBand? _hoveredEqualizerBand;
    private bool _showWaveform;
    private bool _showWaveform3D;
    private bool _showMicCompare;
    private bool _isLeftControlRailCollapsed;
    private string _lastLoadedPresetName = "Warm Radio";
    private bool _lastLoadedPresetIsUserPreset;
    private bool _isRecordingSession;
    private bool _isRecordingPaused;
    private string? _pendingCameraProfileModeLabel;
    private readonly ObservableCollection<SessionRecordingItem> _sessionRecordings = [];
    private FileBrowserWatcher? _sessionFolderWatcher;
    private int _sessionFolderRefreshQueued;
    private string? _sessionPlaybackPath;
    private bool _isScrubbingSessionPlayback;
    private bool _isUpdatingSessionPlaybackPosition;
    private bool _isSessionPlaybackPlaying;
    private bool _isSessionPlaybackEnded;
    private bool _startSessionPlaybackFromBeginning;
    private bool _isMicAnalysisRunning;
    private bool _micAnalysisReadyToFinalize;
    private DateTime _micAnalysisStartedAt;
    private double[] _micAnalysisInput1Sums = [];
    private double[] _micAnalysisInput2Sums = [];
    private int _micAnalysisInput1FrameCount;
    private int _micAnalysisInput2FrameCount;
    private double _micAnalysisInput1Squares;
    private double _micAnalysisInput2Squares;
    private long _micAnalysisInput1SampleCount;
    private long _micAnalysisInput2SampleCount;
    private double _micAnalysisInput1DisplayOffset;
    private double _micAnalysisInput2DisplayOffset;
    private bool _micAnalysisSequential;
    private bool _micAnalysisNormalizeVolume = true;

    public EqualizerWindow()
    {
        InitializeComponent();
        ApplyPersistedWindowPlacement();
        _safeStartCameraRecoveryActive = _startupRecovery.PreviousRunDidNotCloseCleanly;
        _safeStartDx12Disabled = _startupRecovery.PreviousRunDidNotCloseCleanly;
        FreezeSharedBrushes();
        OrderMainTabs();
        DataContext = Settings;
        Bands =
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

        EqBandPanel.ItemsSource = Bands;
        foreach (var band in Bands)
        {
            band.PropertyChanged += EqualizerBandPropertyChanged;
        }

        SyncEqualizerSettings();
        SpectrumCanvas.Children.Add(_equalizerHoverRegion);
        SpectrumCanvas.Children.Add(_input1Trace);
        SpectrumCanvas.Children.Add(_input2Trace);
        SpectrumCanvas.Children.Add(_averageTrace);
        SpectrumCanvas.Children.Add(_liveTrace);
        WaveformCanvas.Children.Add(_rawWaveTrace);
        WaveformCanvas.Children.Add(_processedWaveTrace);
        RecordingSignalCanvas.Children.Add(_recordingRawSignalTrace);
        RecordingSignalCanvas.Children.Add(_recordingSignalTrace);
        _spectrumService.SpectrumAvailable += SpectrumAvailable;
        _spectrumService.StreamStatusChanged += SpectrumServiceStreamStatusChanged;
        _cameraPreviewService.FrameAvailable += CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged += CameraPreviewStatusChanged;
        _directShowPreviewService.FrameAvailable += CameraPreviewFrameAvailable;
        _directShowPreviewService.StatusChanged += CameraPreviewStatusChanged;
        _audioDeviceFormatTimer.Interval = AudioDeviceFormatPollInterval;
        _audioDeviceFormatTimer.Tick += AudioDeviceFormatTimerTick;
        _sessionPlaybackPositionTimer.Interval = TimeSpan.FromMilliseconds(120);
        _sessionPlaybackPositionTimer.Tick += SessionPlaybackPositionTimerTick;
        _karaokePlaybackPositionTimer.Interval = TimeSpan.FromMilliseconds(120);
        _karaokePlaybackPositionTimer.Tick += KaraokePlaybackPositionTimerTick;
        CompositionTarget.Rendering += CompositionTargetRendering;
    }

    private void FreezeSharedBrushes()
    {
        FreezeBrush(_recordingDspActiveBrush);
        FreezeBrush(_recordingNaturalAudioBrush);
        FreezeBrush(_waveformGridBrush);
        FreezeBrush(_waveformCenterGridBrush);
        FreezeBrush(_meterMutedBrush);
        FreezeBrush(_meterTextMutedBrush);
        FreezeBrush(_meterGoodBrush);
        FreezeBrush(_meterWarnBrush);
        FreezeBrush(_meterDangerBrush);
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
            "Karaoke"
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

    public ObservableCollection<EqualizerBand> Bands { get; }

    public ObservableCollection<string> UserPresetNames { get; } = [];

    public ObservableCollection<string> CameraProfileNames { get; } = [];

    public VoiceProcessorSettings Settings { get; } = new();

    private void EqualizerBandPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EqualizerBand.GainDb) or nameof(EqualizerBand.IsEnabled))
        {
            SyncEqualizerSettings();
        }
    }

    private void SyncEqualizerSettings()
    {
        var gains = new double[Bands.Count];
        for (var i = 0; i < Bands.Count; i++)
        {
            gains[i] = Bands[i].IsEnabled ? Bands[i].GainDb : 0d;
        }

        Settings.SetEqualizerGains(gains);
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        _isRestoringAppState = true;
        var hasPersistedState = HasPersistedAppState();

        RestorePersistedFolders();
        RestorePersistedVideoDenoise();
        UpdateVideoColorPolishSettings();
        UpdateAdvancedCameraControlsVisibility(loadWhenOpened: false);

        ApplyDarkTitleBar();
        InputChannelComboBox.ItemsSource = new[]
        {
            new InputChannelOption(InputChannelMode.MonoSum, "Sum L+R"),
            new InputChannelOption(InputChannelMode.Input1Left, "Input 1 L"),
            new InputChannelOption(InputChannelMode.Input2Right, "Input 2 R")
        };
        RestoreInputChannelSelection();

        var devices = MicrophoneSpectrumService.GetInputDevices();
        MicrophoneComboBox.ItemsSource = devices;
        if (devices.Count > 0)
        {
            MicrophoneComboBox.SelectedItem = FindAudioInputDevice(devices, _appSettings.MicrophoneName) ?? devices[0];
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

        if (ProcessedOutputCheckBox is not null)
        {
            SetProcessedOutputToggleState(_appSettings.ProcessedOutputEnabled);
        }

        UserPresetComboBox.ItemsSource = UserPresetNames;
        LoadUserPresetList();
        CameraProfileComboBox.ItemsSource = CameraProfileNames;
        LoadCameraProfileList();

        var cameras = Dx12Camera.GetCameras();

        CameraComboBox.ItemsSource = cameras;
        if (cameras.Count > 0)
        {
            CameraComboBox.SelectedItem = Dx12Camera.FindCamera(
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
        _isCameraEnabled = hasPersistedState && _appSettings.CameraEnabled && !_safeStartCameraRecoveryActive;
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

        RestorePersistedPresetOrDefault();
        _audioDeviceFormatTimer.Start();
        UpdateAudioFormatRouteText();
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

        if (KaraokeLyricsTextBox is not null)
        {
            KaraokeLyricsTextBox.Text = _appSettings.KaraokeLyrics ?? string.Empty;
        }

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
            UpdateKaraokeTrackUi();
        }

        UpdateKaraokeLyricsDisplay();
        UpdateKaraokeAdjustmentStatus();
        UpdateKaraokeVideoPreviewState();
        UpdateKaraokeTransportControls();
    }

    private void RestoreInputChannelSelection()
    {
        var mode = Enum.TryParse<InputChannelMode>(_appSettings.InputChannelMode, out var parsedMode)
            ? parsedMode
            : InputChannelMode.MonoSum;
        var option = InputChannelComboBox.Items
            .OfType<InputChannelOption>()
            .FirstOrDefault(candidate => candidate.Mode == mode);
        InputChannelComboBox.SelectedItem = option ?? InputChannelComboBox.Items.OfType<InputChannelOption>().FirstOrDefault();
    }

    private static AudioInputDevice? FindAudioInputDevice(
        IReadOnlyList<AudioInputDevice> devices,
        string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? null
            : devices.FirstOrDefault(device =>
                device.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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

        LoadWarmRadioPreset();
    }

    private bool ApplyBuiltInPreset(string presetName)
    {
        if (presetName.Equals("Flat", StringComparison.OrdinalIgnoreCase))
        {
            FlatPresetClicked(this, new RoutedEventArgs());
            return true;
        }

        if (presetName.Equals("Podcast Clean", StringComparison.OrdinalIgnoreCase))
        {
            PodcastCleanPresetClicked(this, new RoutedEventArgs());
            return true;
        }

        if (presetName.Equals("Warm Radio", StringComparison.OrdinalIgnoreCase))
        {
            LoadWarmRadioPreset();
            return true;
        }

        if (presetName.Equals("Deep Warm", StringComparison.OrdinalIgnoreCase))
        {
            DeepWarmPresetClicked(this, new RoutedEventArgs());
            return true;
        }

        if (presetName.Equals("Noisy Room", StringComparison.OrdinalIgnoreCase))
        {
            NoisyRoomPresetClicked(this, new RoutedEventArgs());
            return true;
        }

        if (presetName.Equals("Bright Headset", StringComparison.OrdinalIgnoreCase))
        {
            BrightHeadsetPresetClicked(this, new RoutedEventArgs());
            return true;
        }

        return false;
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
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
        if (_isRestoringAppState || _isClosing)
        {
            return;
        }

        SaveAppStateNow();
    }

    private void SaveAppStateNow()
    {
        AppStateStore.SaveSettings(CaptureAppSettingsState());
    }

    private AppSettingsState CaptureAppSettingsState()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var camera = CameraComboBox?.SelectedItem as CameraDevice;
        var mode = Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem);
        var selectedAudioPath = GetSelectedAudioRecordingPath();
        var selectedSessionPath = GetSelectedSessionRecordingPath();
        var selectedKaraokeRecordingPath = GetSelectedKaraokeRecordingPath();
        return new AppSettingsState
        {
            WindowLeft = double.IsFinite(bounds.Left) ? bounds.Left : null,
            WindowTop = double.IsFinite(bounds.Top) ? bounds.Top : null,
            WindowWidth = double.IsFinite(bounds.Width) ? bounds.Width : null,
            WindowHeight = double.IsFinite(bounds.Height) ? bounds.Height : null,
            WindowMaximized = WindowState == WindowState.Maximized,
            LeftControlRailCollapsed = _isLeftControlRailCollapsed,
            MicrophoneName = _selectedDevice?.Name,
            InputChannelMode = _selectedInputChannelMode.ToString(),
            OutputDeviceName = _selectedOutputDevice?.Name,
            OutputEndpointId = _selectedOutputDevice?.EndpointId,
            ProcessedOutputEnabled = ProcessedOutputCheckBox?.IsChecked == true,
            OutputFolder = _outputFolder,
            AudioRecordingFolder = _audioRecordingFolder,
            LastAudioRecordingPath = !string.IsNullOrWhiteSpace(selectedAudioPath) ? selectedAudioPath : _lastAudioRecordingPath,
            LastSessionRecordingPath = !string.IsNullOrWhiteSpace(selectedSessionPath) ? selectedSessionPath : _lastSessionRecordingPath,
            KaraokeTrackPath = _karaokeTrackPath,
            KaraokeTrackPaths = _karaokeQueue.Select(track => track.Path).ToList(),
            KaraokeBrowserFolder = _karaokeBrowserFolder,
            KaraokeRecordingFolder = _karaokeRecordingFolder,
            LastKaraokeRecordingPath = !string.IsNullOrWhiteSpace(selectedKaraokeRecordingPath) ? selectedKaraokeRecordingPath : _lastKaraokeRecordingPath,
            KaraokeRecordVideoEnabled = KaraokeRecordVideoCheckBox?.IsChecked == true,
            KaraokeLyrics = KaraokeLyricsTextBox?.Text,
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

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        SaveAppStateNow();
        _isClosing = true;
        _audioStreamOperationVersion++;
        CompositionTarget.Rendering -= CompositionTargetRendering;
        _audioDeviceFormatTimer.Stop();
        _audioDeviceFormatTimer.Tick -= AudioDeviceFormatTimerTick;
        _spectrumService.SpectrumAvailable -= SpectrumAvailable;
        _spectrumService.StreamStatusChanged -= SpectrumServiceStreamStatusChanged;
        DisposeAudioRecordingFolderWatcher();
        DisposeKaraokeRecordingFolderWatcher();
        DisposeSessionFolderWatcher();
        StopAudioPlayback();
        StopKaraokeRecordingPlayback();
        StopKaraokePlayback(clearTrack: false);
        if (_isKaraokeVocalRecording)
        {
            _spectrumService.StopProcessedAudioRecording();
            _isKaraokeVocalRecording = false;
        }

        StopSessionPlayback();
        _cameraPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged -= CameraPreviewStatusChanged;
        _directShowPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
        _directShowPreviewService.StatusChanged -= CameraPreviewStatusChanged;
        _directShowPreviewService.Dispose();
        StopTextureNativeCameraStream();
        Dx12Camera.CloseActive(collectGarbage: true);
        _cameraModeLoadCancellation?.Cancel();
        _cameraModeLoadCancellation?.Dispose();
        if (_waveform3DWindow is not null)
        {
            _waveform3DWindow.Close();
            _waveform3DWindow = null;
        }

        _textureNativeRecordingSession?.Dispose();
        _textureNativeRecordingSession = null;
        _cameraPreviewService.Dispose();
        _spectrumService.Dispose();
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
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
        WaveformCanvas.Margin = centerMargin;
        AnalyzerToolbar.Margin = centerMargin;
        InlineWaveform3DHost.Margin = new Thickness(leftWidth, 58, RightControlRailWidth, 280);
        EqualizerFaceplate.Margin = new Thickness(
            leftWidth + EqualizerFaceplateOuterGap,
            0,
            RightControlRailWidth + EqualizerFaceplateOuterGap,
            EqualizerFaceplateOuterGap);
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
            AudioRecordingStatusText.Text = "Another processed audio recording is already running.";
            UpdateStandaloneAudioRecordingTransportControls();
            UpdateKaraokeTransportControls();
            return;
        }

        if (!_spectrumService.IsRunning)
        {
            AudioRecordingStatusText.Text = "Select a microphone before recording.";
            return;
        }

        try
        {
            var path = CreateAudioRecordingFilePath(DateTime.Now);
            _spectrumService.StartProcessedAudioRecording(path);
            _activeAudioRecordingPath = path;
            _audioRecordingStartedAt = DateTime.UtcNow;
            _audioRecordingPausedAt = default;
            _audioRecordingPausedDuration = TimeSpan.Zero;
            _isStandaloneAudioRecording = true;
            _isStandaloneAudioRecordingPaused = false;
            AudioRecordingStatusText.Text = $"Recording processed audio: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            AudioRecordingStatusText.Text = $"Audio recording failed: {ex.Message}";
            _activeAudioRecordingPath = null;
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
            AudioRecordingStatusText.Text = "Audio recording resumed.";
        }
        else
        {
            _audioRecordingPausedAt = DateTime.UtcNow;
            _isStandaloneAudioRecordingPaused = true;
            _spectrumService.PauseProcessedAudioRecording();
            AudioRecordingStatusText.Text = "Audio recording paused.";
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

        AudioRecordingStatusText.Text = string.IsNullOrWhiteSpace(savedPath)
            ? $"Audio recording stopped at {FormatDuration(elapsed)}."
            : $"Saved {System.IO.Path.GetFileName(savedPath)} ({FormatDuration(elapsed)}).";
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

    private void DeleteSelectedRecordingMenuClicked(object sender, RoutedEventArgs e)
    {
        DeleteSelectedAudioRecording();
    }

    private void OpenAudioRecordingLocationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_audioRecordingFolder);
            var selectedPath = GetSelectedAudioRecordingPath();
            if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
            {
                Process.Start("explorer.exe", $"/select,\"{selectedPath}\"");
                AudioRecordingStatusText.Text = $"Opened location for {System.IO.Path.GetFileName(selectedPath)}.";
                return;
            }

            Process.Start("explorer.exe", $"\"{_audioRecordingFolder}\"");
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
                Process.Start("explorer.exe", $"/select,\"{selected.Path}\"");
                RecordingStatusText.Text = $"Opened location for {System.IO.Path.GetFileName(selected.Path)}.";
                return;
            }

            Process.Start("explorer.exe", $"\"{_outputFolder}\"");
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

        var deleteFolder = IsPodcastSessionFolder(selected.SessionFolder);
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

        var recordingTarget = CreatePodcastRecordingTarget();
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
            ? $"Recording GPU video set {FormatRecordingSetNumber(_activeRecordingSetNumber)}: {videoPath}"
            : videoStarted
                ? $"Recording video set {FormatRecordingSetNumber(_activeRecordingSetNumber)}: {videoPath}"
            : $"Recording set {FormatRecordingSetNumber(_activeRecordingSetNumber)} started: {_activeRecordingSessionFolder}";
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
            else if (Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice))
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
            else if (Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice))
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
        var mode = Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem);
        return Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice)
            ? _directShowPreviewService.StartRecording(videoPath, mode)
            : _cameraPreviewService.StartRecording(videoPath, mode);
    }

    private string? StopActivePreviewRecording()
    {
        if (Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice))
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
                    Dx12Camera.ShouldRecordProcessedTextureOutput(_pendingVideoDenoiseEnabled),
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength);
            }
            catch (Exception ex)
            {
                RecordingStatusText.Text = $"Shared GPU recording path unavailable: {ex.Message}";
                return false;
            }
        }

        if (!Dx12Camera.ShouldUseTextureNativeRecording()
            || !_isCameraEnabled
            || string.IsNullOrWhiteSpace(videoPath)
            || CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            return false;
        }

        if (Dx12Camera.IsCpuCameraPreviewOwningCamera(
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
                Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem),
                videoPath,
                new TextureNativeRecordingOptions(
                    Dx12Camera.ShouldRecordProcessedTextureOutput(_pendingVideoDenoiseEnabled),
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

    private void MainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl) || _isClosing)
        {
            return;
        }

        if (!_isCameraEnabled)
        {
            return;
        }

        if (IsPodcastTabSelected() || IsKaraokeTabSelected() && _karaokeRecordVideoEnabled)
        {
            ReinitializeCameraForFocusedPreview();
        }
    }

    private void ReinitializeCameraForFocusedPreview()
    {
        if (!_isCameraEnabled || _isLoadingCameraModes)
        {
            return;
        }

        DisposeActiveCameraForReinitialize();
        if (_dx12Camera is null)
        {
            StartCameraPreview();
        }
    }

    private void DisposeActiveCameraForReinitialize()
    {
        StopTextureNativeCameraStream();
        try
        {
            _cameraPreviewService.Stop();
        }
        catch
        {
        }

        try
        {
            _directShowPreviewService.Stop();
        }
        catch
        {
        }

        _dx12Camera = null;
        _isDirectShowPreviewActive = false;
        _isCameraFrameUpdateQueued = false;
        _pendingCameraFrame = null;
        _pendingTextureNativeFrameInfo = null;
        ClearPodcastCameraPreviewSurface();
        ClearKaraokeVideoPreviewBitmap();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void UpdateCameraEnabledState()
    {
        if (CameraEnabledToggle is null || CameraPreviewStatusText is null || CameraComboBox is null || CameraModeComboBox is null)
        {
            return;
        }

        _isUpdatingCameraUi = true;
        try
        {
            CameraEnabledToggle.IsEnabled = _cameraAvailable && !_isCameraServiceStopPending;
            CameraComboBox.IsEnabled = _cameraAvailable && !_isCameraServiceStopPending;
            CameraModeComboBox.IsEnabled = _cameraAvailable && !_isCameraServiceStopPending && CameraModeComboBox.Items.Count > 0;

            if (!_cameraAvailable)
            {
                _isCameraEnabled = false;
                CameraEnabledToggle.IsChecked = false;
                CameraEnabledToggle.Content = "No Camera";
                CameraPreviewStatusText.Text = "No camera source found";
                CameraModeComboBox.ItemsSource = new[] { CameraVideoMode.Auto };
                CameraModeComboBox.SelectedIndex = 0;
                CameraPreviewImage.Source = null;
                CameraPreviewImage.Visibility = Visibility.Collapsed;
                CameraPlaceholder.Visibility = Visibility.Visible;
                UpdateKaraokeVideoPreviewState();
                return;
            }

            CameraEnabledToggle.IsChecked = _isCameraEnabled;
            CameraEnabledToggle.Content = _isCameraEnabled ? "Camera On" : "Camera Off";
        }
        finally
        {
            _isUpdatingCameraUi = false;
        }

        if (_isCameraServiceStopPending)
        {
            return;
        }

        if (_isCameraEnabled)
        {
            if (_isLoadingCameraModes)
            {
                CameraPreviewStatusText.Text = CameraComboBox.SelectedItem is CameraDevice loadingCamera
                    ? $"Loading modes before preview: {loadingCamera.Name}"
                    : "Loading camera modes before preview";
                return;
            }

            StartCameraPreview();
        }
        else
        {
            StopCameraPreview(Dx12Camera.FormatCameraDisabledStatus(Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem)));
        }

        UpdateKaraokeVideoPreviewState();
    }

    private void StartCameraPreview()
    {
        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            StopCameraPreview("Choose a camera source");
            return;
        }

        StopSessionPlayback();

        CameraPlaceholder.Visibility = Visibility.Collapsed;
        CameraPreviewImage.Visibility = Visibility.Visible;

        var mode = CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
        if (Dx12Camera.ShouldUseSharedTextureCameraStream(_safeStartDx12Disabled))
        {
            if (Dx12Camera.TryGetTextureNativePreviewFailure(camera, mode, out var cachedTextureFailure))
            {
                _lastTextureNativeCameraError = cachedTextureFailure;
                CameraPreviewStatusText.Text = $"Shared GPU stream skipped for {camera.Name}: {cachedTextureFailure}";
            }
            else if (StartTextureNativeCameraStream(camera, mode))
            {
                Dx12Camera.ForgetTextureNativePreviewFailure(camera, mode);
                return;
            }
            else
            {
                Dx12Camera.RememberTextureNativePreviewFailure(
                    camera,
                    mode,
                    _lastTextureNativeCameraError ?? "shared texture preview attempt failed");
            }

            CameraPreviewStatusText.Text = $"Shared GPU stream unavailable; trying CPU preview for {camera.Name}: {_lastTextureNativeCameraError ?? "unknown error"}";
        }

        StopTextureNativeCameraStream();
        _directShowPreviewService.Stop();
        _isDirectShowPreviewActive = false;
        HideDirect3D12PreviewHost();
        CameraPreviewImage.Visibility = Visibility.Visible;

        if (Dx12Camera.IsDirectShowCamera(camera))
        {
            if (TryStartDirectShowPreview(camera, camera, mode, isFallback: false))
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
            if (TryStartDirectShowFallbackPreview(camera, mode))
            {
                return;
            }

            _isCameraEnabled = false;
            StopCameraPreview("Windows Media Foundation camera preview is unavailable");
            UpdateCameraEnabledState();
            return;
        }

        _cameraPreviewService.DenoiseEnabled = _pendingVideoDenoiseEnabled;
        _cameraPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;
        _cameraPreviewService.DenoiseHandledByPreviewRenderer = _direct3D12PreviewHost?.IsReady == true;
        _cameraPreviewService.ColorSettings = _pendingVideoColorSettings;
        if (_cameraPreviewService.Start(camera, mode))
        {
            _isDirectShowPreviewActive = false;
            ClaimFallbackCameraOwner(
                camera,
                mode,
                "Media Foundation CPU preview",
                () => _cameraPreviewService.Stop());
            CameraPreviewStatusText.Text = Dx12Camera.FormatCameraStatus("Starting", camera, mode);
            return;
        }

        if (_pendingVideoDenoiseEnabled)
        {
            _cameraPreviewService.DenoiseEnabled = false;
            if (_cameraPreviewService.Start(camera, mode))
            {
                _isDirectShowPreviewActive = false;
                ClaimFallbackCameraOwner(
                    camera,
                    mode,
                    "Media Foundation CPU preview with denoise bypassed",
                    () => _cameraPreviewService.Stop());
                CameraPreviewStatusText.Text = $"{Dx12Camera.FormatCameraStatus("Starting", camera, mode)} - denoise bypassed because the camera rejected it";
                return;
            }
        }

        if (TryStartDirectShowFallbackPreview(camera, mode))
        {
            return;
        }

        _isCameraEnabled = false;
        StopCameraPreview($"Could not open preview for {camera.Name}");
        UpdateCameraEnabledState();
    }

    private bool TryStartDirectShowFallbackPreview(CameraDevice primaryCamera, CameraVideoMode mode)
    {
        var fallback = primaryCamera.FallbackDevice;
        return fallback is not null
            && Dx12Camera.IsDirectShowCamera(fallback)
            && TryStartDirectShowPreview(primaryCamera, fallback, mode, isFallback: true);
    }

    private bool TryStartDirectShowPreview(
        CameraDevice displayCamera,
        CameraDevice directShowCamera,
        CameraVideoMode mode,
        bool isFallback)
    {
        _cameraPreviewService.Stop();
        _directShowPreviewService.DenoiseEnabled = _pendingVideoDenoiseEnabled;
        _directShowPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;
        _directShowPreviewService.ColorSettings = _pendingVideoColorSettings;
        CameraPreviewStatusText.Text = isFallback
            ? $"Starting DirectShow fallback for {displayCamera.Name}"
            : $"Starting DirectShow preview for {directShowCamera.Name}";
        if (_directShowPreviewService.Start(directShowCamera, mode))
        {
            _isDirectShowPreviewActive = true;
            ClaimFallbackCameraOwner(
                displayCamera,
                mode,
                isFallback ? "DirectShow CPU fallback preview" : "DirectShow CPU preview",
                () => _directShowPreviewService.Stop());
            CameraPreviewStatusText.Text = isFallback
                ? $"{Dx12Camera.FormatCameraStatus("Starting", displayCamera, mode)} - DirectShow fallback"
                : Dx12Camera.FormatCameraStatus("Starting", displayCamera, mode);
            return true;
        }

        return false;
    }

    private bool StartTextureNativeCameraStream(CameraDevice camera, CameraVideoMode mode)
    {
        _lastTextureNativeCameraError = null;
        _cameraPreviewService.Stop();
        _directShowPreviewService.Stop();
        _isDirectShowPreviewActive = false;
        _isCameraFrameUpdateQueued = false;
        _pendingCameraFrame = null;
        _cameraPreviewBitmap = null;
        CameraPreviewImage.Source = null;
        CameraPreviewImage.Visibility = Visibility.Collapsed;
        CameraPlaceholder.Visibility = Visibility.Visible;
        CameraPreviewStatusText.Text = $"{Dx12Camera.FormatCameraStatus("GPU stream starting", camera, mode)} - waiting for texture frames";

        try
        {
            HideDirect3D12PreviewHost();
            if (_dx12Camera is null)
            {
                var dx12Camera = new Dx12Camera(camera, mode, CreateActiveDx12CameraPreviewTarget());
                dx12Camera.Denoise(_pendingVideoDenoiseEnabled, _pendingVideoDenoiseStrength);
                AttachDx12Camera(dx12Camera);
            }
            else
            {
                ShutDownBecauseCameraWasNotDead();
                return false;
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

    private void AttachDx12Camera(Dx12Camera camera)
    {
        if (ReferenceEquals(_dx12Camera, camera))
        {
            return;
        }

        DetachDx12Camera();
        _dx12Camera = camera;
        camera.FrameAvailable += TextureNativeCameraFrameAvailable;
        camera.StatusChanged += TextureNativeCameraStatusChanged;
    }

    private void DetachDx12Camera()
    {
        var camera = _dx12Camera;
        if (camera is null)
        {
            return;
        }

        camera.FrameAvailable -= TextureNativeCameraFrameAvailable;
        camera.StatusChanged -= TextureNativeCameraStatusChanged;
        _dx12Camera = null;
    }

    private void ClaimFallbackCameraOwner(
        CameraDevice camera,
        CameraVideoMode mode,
        string fallbackDescription,
        Action fallbackStop)
    {
        if (_dx12Camera is null)
        {
            var owner = Dx12Camera.ClaimFallback(
                camera,
                mode,
                CreateActiveDx12CameraPreviewTarget(),
                fallbackDescription,
                fallbackStop);
            AttachDx12Camera(owner);
        }
        else
        {
            ShutDownBecauseCameraWasNotDead();
        }
    }

    private void ShutDownBecauseCameraWasNotDead()
    {
        MessageBox.Show(
            this,
            "camera was not extremely dead",
            "Camera lifecycle violation",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        System.Windows.Application.Current.Shutdown();
    }

    private Dx12Camera.PreviewTarget CreateActiveDx12CameraPreviewTarget()
    {
        if (IsKaraokeTabSelected() && _karaokeRecordVideoEnabled && KaraokeVideoPreviewSurfaceGrid is not null)
        {
            ClearPodcastCameraPreviewSurface();
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

    private bool IsKaraokeTabSelected()
    {
        return MainTabControl?.SelectedItem is TabItem { Header: string header }
            && header.Equals("Karaoke", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPodcastTabSelected()
    {
        return MainTabControl?.SelectedItem is TabItem { Header: string header }
            && header.Equals("Podcast", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearPodcastCameraPreviewSurface()
    {
        _cameraPreviewBitmap = null;
        if (CameraPreviewImage is not null)
        {
            CameraPreviewImage.Source = null;
            CameraPreviewImage.Visibility = Visibility.Collapsed;
        }

        if (CameraPlaceholder is not null)
        {
            CameraPlaceholder.Visibility = Visibility.Visible;
        }

        if (CameraPreviewStatusText is not null)
        {
            CameraPreviewStatusText.Text = "Camera preview is owned by Karaoke.";
        }
    }

    private void StopCameraPreview(string status)
    {
        StopTextureNativeCameraStream();
        _isCameraFrameUpdateQueued = false;
        _isDirectShowPreviewActive = false;
        _pendingCameraFrame = null;
        _cameraPreviewBitmap = null;
        CameraPreviewImage.Source = null;
        CameraPreviewImage.Visibility = Visibility.Collapsed;
        CameraPlaceholder.Visibility = Visibility.Visible;
        CameraPreviewStatusText.Text = status;
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
            try
            {
                _cameraPreviewService.Stop();
            }
            catch
            {
            }

            try
            {
                _directShowPreviewService.Stop();
            }
            catch
            {
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (operationVersion != System.Threading.Volatile.Read(ref _cameraServiceStopOperationVersion))
                {
                    return;
                }

                _isCameraServiceStopPending = false;
                _isUpdatingCameraUi = true;
                try
                {
                    CameraEnabledToggle.IsEnabled = _cameraAvailable;
                    CameraComboBox.IsEnabled = _cameraAvailable;
                    CameraModeComboBox.IsEnabled = _cameraAvailable && CameraModeComboBox.Items.Count > 0;
                    CameraEnabledToggle.IsChecked = _isCameraEnabled;
                    CameraEnabledToggle.Content = _isCameraEnabled ? "Camera On" : _cameraAvailable ? "Camera Off" : "No Camera";
                }
                finally
                {
                    _isUpdatingCameraUi = false;
                }

                if (!_isCameraEnabled)
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
            HideDirect3D12PreviewHost();
            return;
        }

        DetachDx12Camera();
        try
        {
            if (camera.IsRecording)
            {
                _lastTextureNativeRecordingResult = camera.StopMP4();
            }
        }
        finally
        {
            Dx12Camera.CloseActive(collectGarbage: false);
            _pendingTextureNativeFrameInfo = null;
            System.Threading.Volatile.Write(ref _textureNativeFrameUpdateQueued, 0);
        }
    }

    private void ShowDirect3D12PreviewHost(IntPtr nativeD3D12Device)
    {
        if (CameraPreviewSurfaceGrid is null)
        {
            if (nativeD3D12Device != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.Release(nativeD3D12Device);
            }

            return;
        }

        if (_direct3D12PreviewHost is null)
        {
            _direct3D12PreviewHost = new Direct3D12PreviewHost(nativeD3D12Device)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _direct3D12PreviewHost.StatusChanged += Direct3D12PreviewHostStatusChanged;
            CameraPreviewSurfaceGrid.Children.Insert(1, _direct3D12PreviewHost);
        }
        else if (nativeD3D12Device != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(nativeD3D12Device);
        }

        _direct3D12PreviewHost.Visibility = Visibility.Visible;
    }

    private void HideDirect3D12PreviewHost()
    {
        var host = _direct3D12PreviewHost;
        if (host is null)
        {
            return;
        }

        _direct3D12PreviewHost = null;
        host.StatusChanged -= Direct3D12PreviewHostStatusChanged;
        if (CameraPreviewSurfaceGrid is not null)
        {
            CameraPreviewSurfaceGrid.Children.Remove(host);
        }

        host.Dispose();
    }

    private void Direct3D12PreviewHostStatusChanged(object? sender, string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isCameraEnabled && IsPodcastTabSelected())
            {
                CameraPreviewStatusText.Text = status;
            }
        });
    }

    private void CameraPreviewFrameAvailable(object? sender, CameraFrame frame)
    {
        _pendingCameraFrame = frame;
        if (_isCameraFrameUpdateQueued)
        {
            return;
        }

        _isCameraFrameUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            var latestFrame = _pendingCameraFrame;
            _pendingCameraFrame = null;
            _isCameraFrameUpdateQueued = false;

            if (latestFrame is null || !_isCameraEnabled)
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
            if (_direct3D12PreviewHost?.IsReady == true)
            {
                _direct3D12PreviewHost.RenderBgraFrame(
                    latestFrame,
                    System.Threading.Interlocked.Increment(ref _cameraBgraPreviewFrameNumber),
                    _pendingVideoColorSettings,
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength);
                CameraPreviewImage.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (latestFrame.HasBgra)
                {
                    UpdateCameraPreviewBitmap(latestFrame);
                    CameraPreviewImage.Visibility = Visibility.Visible;
                }
                else
                {
                    CameraPreviewImage.Visibility = Visibility.Collapsed;
                }
            }

            CameraPlaceholder.Visibility = Visibility.Collapsed;

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                var renderer = _direct3D12PreviewHost?.IsReady == true
                    ? latestFrame.HasNv12 ? "DX12 NV12" : "DX12 BGRA"
                    : latestFrame.HasBgra ? "WPF BGRA" : "waiting for BGRA fallback";
                var status = $"{Dx12Camera.FormatCameraStatus("Live", camera, Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem))} - {renderer}";
                CameraPreviewStatusText.Text = status;
            }
        });
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
            KaraokeVideoPreviewImage.Source = null;
            KaraokeVideoPreviewImage.Visibility = Visibility.Collapsed;
            KaraokeVideoPreviewPlaceholder.Visibility = Visibility.Visible;
            KaraokeVideoPreviewPlaceholder.Text = "Video preview off";
            if (KaraokeVideoPreviewStatusText is not null)
            {
                KaraokeVideoPreviewStatusText.Text = "Karaoke camera preview is off.";
            }

            return;
        }

        if (!_isCameraEnabled)
        {
            KaraokeVideoPreviewImage.Visibility = Visibility.Collapsed;
            KaraokeVideoPreviewPlaceholder.Visibility = Visibility.Visible;
            KaraokeVideoPreviewPlaceholder.Text = "Turn camera on for preview";
            ClearKaraokeVideoPreviewBitmap();
            if (KaraokeVideoPreviewStatusText is not null)
            {
                KaraokeVideoPreviewStatusText.Text = "Turn on the camera to start Karaoke video preview.";
            }

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
        if (KaraokeVideoPreviewImage is not null)
        {
            KaraokeVideoPreviewImage.Source = null;
            KaraokeVideoPreviewImage.Visibility = Visibility.Collapsed;
        }

        if (KaraokeVideoPreviewPlaceholder is not null)
        {
            KaraokeVideoPreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void TextureNativeCameraFrameAvailable(object? sender, TextureNativeFrameInfo frame)
    {
        System.Threading.Interlocked.Exchange(ref _pendingTextureNativeFrameInfo, frame);
        if (System.Threading.Interlocked.Exchange(ref _textureNativeFrameUpdateQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke((Action)ProcessPendingTextureNativeFrame, DispatcherPriority.Background);
    }

    private void ProcessPendingTextureNativeFrame()
    {
        var frame = System.Threading.Interlocked.Exchange(ref _pendingTextureNativeFrameInfo, null);
        if (frame is not null && _isCameraEnabled && IsPodcastTabSelected())
        {
            CameraPreviewImage.Visibility = Visibility.Collapsed;
            CameraPlaceholder.Visibility = Visibility.Visible;

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                CameraPreviewStatusText.Text = Dx12Camera.FormatTextureNativeCameraStatus(
                    _dx12Camera,
                    "GPU stream live",
                    camera,
                    frame,
                    _pendingVideoDenoiseEnabled,
                    _pendingVideoDenoiseStrength,
                    Dx12Camera.ShouldRecordProcessedTextureOutput(_pendingVideoDenoiseEnabled));
            }
        }

        System.Threading.Volatile.Write(ref _textureNativeFrameUpdateQueued, 0);
        if (System.Threading.Volatile.Read(ref _pendingTextureNativeFrameInfo) is not null
            && System.Threading.Interlocked.Exchange(ref _textureNativeFrameUpdateQueued, 1) == 0)
        {
            Dispatcher.BeginInvoke((Action)ProcessPendingTextureNativeFrame, DispatcherPriority.Background);
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

        if (Dx12Camera.IsDirectShowCamera(camera))
        {
            _isLoadingCameraModes = false;
            CameraModeComboBox.IsEnabled = true;
            SelectPendingCameraProfileMode();
            CameraPreviewStatusText.Text = Dx12Camera.FormatDirectShowCameraSelectedStatus(camera);
            UpdateCameraEnabledState();

            return;
        }

        try
        {
            CameraPreviewStatusText.Text = Dx12Camera.FormatLoadingCameraModesStatus(
                camera,
                Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem));
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
                CameraPreviewStatusText.Text = CameraComboBox.SelectedItem is CameraDevice camera
                    ? $"{Dx12Camera.FormatCameraStatus("Preview", camera, Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem))} - {status}"
                    : status;
            }
        });
    }

    private void UpdateCameraIdleStatus()
    {
        if (!_cameraAvailable)
        {
            CameraPreviewStatusText.Text = "No camera source found";
            return;
        }

        CameraPreviewStatusText.Text = Dx12Camera.FormatCameraDisabledStatus(Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem));
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
            CameraControlStatusText.Text = "Choose a camera to load controls.";
            return;
        }

        var controls = _cameraControlService.GetControls(camera);
        if (controls.Count == 0)
        {
            CameraControlStatusText.Text = "No standard Windows camera controls were exposed by this source.";
            return;
        }

        CameraControlStatusText.Text = $"{controls.Count} Windows camera controls exposed by {camera.Name}.";
        RenderCameraControls(camera, controls);
    }

    private void ResetCameraControlPanel(string status)
    {
        if (CameraControlPanel is not null)
        {
            CameraControlPanel.Children.Clear();
        }

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
            Text = Dx12Camera.FormatCameraControlValue(control.Value),
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
            SmallChange = Dx12Camera.GetCameraControlNudgeStep(control),
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

        if (Dx12Camera.UsesCameraControlNudgeButtons(control))
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

            var rounded = Dx12Camera.ApplyCameraControlDefaultMagnet(Dx12Camera.RoundCameraControlToStep(e.NewValue, control), control);
            valueText.Text = Dx12Camera.FormatCameraControlValue(rounded);
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
            if (!_isUpdatingCameraControls)
            {
                if (!SetCameraControl(camera, control, control.Value, isAuto: true))
                {
                    RestoreCameraAutoCheckBox(autoCheckBox, control);
                }
            }
        };

        autoCheckBox.Unchecked += (_, _) =>
        {
            if (!_isUpdatingCameraControls)
            {
                if (!SetCameraControl(camera, control, control.Value, isAuto: false))
                {
                    RestoreCameraAutoCheckBox(autoCheckBox, control);
                }
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
                valueText.Text = Dx12Camera.FormatCameraControlValue(control.DefaultValue);
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
        var nextValue = Math.Clamp(control.Value + direction * Dx12Camera.GetCameraControlNudgeStep(control), control.Minimum, control.Maximum);
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
        valueText.Text = Dx12Camera.FormatCameraControlValue(nextValue);
        _isUpdatingCameraControls = false;
    }

    private bool SetCameraControl(CameraDevice camera, CameraControlItem control, int value, bool isAuto)
    {
        var success = _cameraControlService.SetControl(camera, control, value, isAuto);
        if (!success)
        {
            CameraControlStatusText.Text = $"Could not set {control.Name}. The camera may be busy or this control may be locked by its driver.";
            return false;
        }

        control.Value = value;
        control.IsAuto = isAuto;
        CameraControlStatusText.Text = $"{control.Name}: {(isAuto ? "Auto" : Dx12Camera.FormatCameraControlValue(value))}";
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
        var effectiveStrength = strength;
        _pendingVideoDenoiseEnabled = VideoDenoiseCheckBox.IsChecked == true;
        if (VideoDenoiseControlsPanel is not null)
        {
            VideoDenoiseControlsPanel.Visibility = _pendingVideoDenoiseEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        _pendingVideoDenoiseStrength = effectiveStrength;
        _dx12Camera?.Denoise(_pendingVideoDenoiseEnabled, _pendingVideoDenoiseStrength);
        _cameraPreviewService.DenoiseEnabled = _pendingVideoDenoiseEnabled;
        _cameraPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;
        _cameraPreviewService.DenoiseHandledByPreviewRenderer = _direct3D12PreviewHost?.IsReady == true;
        _directShowPreviewService.DenoiseEnabled = _pendingVideoDenoiseEnabled;
        _directShowPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;

        if (VideoDenoiseValueText is not null)
        {
            VideoDenoiseValueText.Text = $"{effectiveStrength:0.0}";
        }

        if (restartPreview && _isCameraEnabled)
        {
            StartCameraPreview();
            return;
        }

        if (_isCameraEnabled)
        {
            if (_dx12Camera?.IsTextureNative == true)
            {
                CameraControlStatusText.Text = _pendingVideoDenoiseEnabled
                    ? "DX12 video grain reduction is live on the preview and will be included in texture-native recordings."
                    : "DX12 video grain reduction is off on the preview.";
                return;
            }

            CameraControlStatusText.Text = _pendingVideoDenoiseEnabled
                ? "Video grain reduction is live on the preview and CPU recording path."
                : "Video grain reduction is off on the preview and CPU recording path.";
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

        if (_dx12Camera?.IsTextureNative == true)
        {
            CameraControlStatusText.Text = _pendingVideoColorSettings.HasVisibleAdjustments
                ? "Color polish is armed for CPU fallback/recording. The current DX12 texture preview remains raw."
                : "Color polish is neutral.";
            return;
        }

        CameraControlStatusText.Text = _pendingVideoColorSettings.HasVisibleAdjustments
            ? "Color polish is live on the DX12 preview shader and recording path."
            : "Color polish is neutral on the preview and recording path.";
    }

    private void ApplyVideoColorSettingsToCpuServices()
    {
        _cameraPreviewService.ColorSettings = _pendingVideoColorSettings;
        _directShowPreviewService.ColorSettings = _pendingVideoColorSettings;
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
            var previewFolder = ResolvePodcastSessionFolderForPreview();
            NextSessionFolderText.Text = previewFolder;
            if (NextRecordingFilesText is not null)
            {
                var setNumber = Directory.Exists(previewFolder)
                    ? GetNextRecordingSetNumber(previewFolder)
                    : 1;
                NextRecordingFilesText.Text = FormatRecordingFileSet(setNumber);
            }
        }

        ConfigureSessionFolderWatcher();
        RefreshSessionRecordings(_lastSessionRecordingPath);
    }

    private RecordingTarget CreatePodcastRecordingTarget()
    {
        var sessionFolder = ResolvePodcastSessionFolderForRecording();
        Directory.CreateDirectory(sessionFolder);
        var setNumber = GetNextRecordingSetNumber(sessionFolder);
        return new RecordingTarget(sessionFolder, setNumber);
    }

    private string ResolvePodcastSessionFolderForRecording()
    {
        Directory.CreateDirectory(_outputFolder);

        if (IsPodcastSessionFolder(_outputFolder))
        {
            return _outputFolder;
        }

        var now = DateTime.Now;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var timestamp = attempt == 0
                ? now
                : now.AddSeconds(attempt);
            var sessionFolder = System.IO.Path.Combine(_outputFolder, CreatePodcastSessionFolderName(timestamp));
            if (Directory.Exists(sessionFolder))
            {
                continue;
            }

            Directory.CreateDirectory(sessionFolder);
            return sessionFolder;
        }

        var fallbackFolder = System.IO.Path.Combine(_outputFolder, $"Podcast_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallbackFolder);
        return fallbackFolder;
    }

    private string ResolvePodcastSessionFolderForPreview()
    {
        return IsPodcastSessionFolder(_outputFolder)
            ? _outputFolder
            : System.IO.Path.Combine(_outputFolder, CreatePodcastSessionFolderName(DateTime.Now));
    }

    private static string CreatePodcastSessionFolderName(DateTime timestamp)
    {
        return $"Podcast_{timestamp:yyyy-MM-dd_HH-mm-ss}";
    }

    private static bool IsPodcastSessionFolder(string folder)
    {
        return PodcastSessionFolderRegex.IsMatch(System.IO.Path.GetFileName(folder.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar)));
    }

    private static int GetNextRecordingSetNumber(string sessionFolder)
    {
        if (!Directory.Exists(sessionFolder))
        {
            return 1;
        }

        var highest = Directory.EnumerateFiles(sessionFolder)
            .Select(file => NumberedRecordingFileRegex.Match(System.IO.Path.GetFileName(file)))
            .Where(match => match.Success)
            .Select(match => int.TryParse(match.Groups["number"].Value, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return highest + 1;
    }

    private static string FormatRecordingSetNumber(int setNumber)
    {
        return setNumber.ToString("000");
    }

    private static string FormatRecordingFileSet(int setNumber)
    {
        var number = FormatRecordingSetNumber(setNumber);
        return $"video_{number}.mp4{Environment.NewLine}mix_{number}.wav{Environment.NewLine}raw_backup_{number}.wav{Environment.NewLine}session.json";
    }

    private string? GetActiveRecordingVideoPath()
    {
        if (string.IsNullOrWhiteSpace(_activeRecordingSessionFolder) || _activeRecordingSetNumber <= 0)
        {
            return null;
        }

        return System.IO.Path.Combine(
            _activeRecordingSessionFolder,
            $"video_{FormatRecordingSetNumber(_activeRecordingSetNumber)}.mp4");
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
                mode = Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem).Label,
                denoiseEnabled = _pendingVideoDenoiseEnabled,
                denoiseSliderStrength = _videoDenoiseSliderStrength,
                denoiseStrength = _pendingVideoDenoiseStrength,
                video = string.IsNullOrWhiteSpace(videoPath) ? null : System.IO.Path.GetFileName(videoPath),
                engine = textureResult is not null
                    ? _dx12Camera?.IsTextureNative == true
                        ? "Windows Media Foundation shared texture-native GPU stream"
                        : "Windows Media Foundation texture-native GPU samples"
                    : Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice)
                        ? "DirectShow CPU frames with Media Foundation MP4 writer"
                        : "Windows Media Foundation CPU frames",
                videoProcessing = BuildVideoProcessingMetadata(textureResult),
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
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, UserPresetJsonOptions));
        }
        catch (Exception ex)
        {
            RecordingStatusText.Text = $"Recording stopped, but session metadata could not be written: {ex.Message}";
        }
    }

    private object BuildVideoProcessingMetadata(TextureNativeRecordingResult? textureResult)
    {
        if (textureResult is not null)
        {
            return new
            {
                previewPipeline = "Direct3D 12 NV12 shader preview",
                previewRenderPath = _dx12Camera?.PreviewPathDescription ?? "DX12 preview path unavailable",
                previewDenoiseApplied = _pendingVideoDenoiseEnabled,
                previewDenoiseSliderStrength = _videoDenoiseSliderStrength,
                previewDenoiseStrength = _pendingVideoDenoiseStrength,
                previewColorPolishApplied = false,
                previewColorPolish = CreateVideoColorMetadata(),
                recordingPipeline = textureResult.RecordingPipeline,
                recordingDenoiseApplied = textureResult.RecordingDenoiseApplied,
                recordingMatchesPreviewDenoise = textureResult.RecordingMatchesPreviewDenoise,
                recordingColorPolishApplied = false,
                recordingMatchesPreviewColor = !_pendingVideoColorSettings.HasVisibleAdjustments,
                note = _pendingVideoColorSettings.HasVisibleAdjustments
                    ? "Color polish is CPU-only in this build; the saved texture-native video is raw color output."
                    : textureResult.RecordingPipeline.Contains("processed", StringComparison.OrdinalIgnoreCase)
                    ? "Texture-native recording matched the preview denoise setting through the processed bridge."
                    : _pendingVideoDenoiseEnabled
                        ? "DX12 denoise was visible in preview only; the saved texture-native video is raw camera output."
                        : "DX12 preview denoise was off; the saved texture-native video is raw camera output."
            };
        }

        var isDirectShow = Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice);
        return new
        {
            previewPipeline = Dx12Camera.FormatCpuCameraPreviewPipeline(isDirectShow, _direct3D12PreviewHost?.IsReady == true),
            previewDenoiseApplied = _pendingVideoDenoiseEnabled,
            previewDenoiseSliderStrength = _videoDenoiseSliderStrength,
            previewDenoiseStrength = _pendingVideoDenoiseStrength,
            previewColorPolishApplied = _pendingVideoColorSettings.HasVisibleAdjustments,
            previewColorPolish = CreateVideoColorMetadata(),
            recordingPipeline = isDirectShow ? "DirectShow CPU frames to Media Foundation MP4 writer" : "Media Foundation CPU frame writer",
            recordingDenoiseApplied = _pendingVideoDenoiseEnabled,
            recordingMatchesPreviewDenoise = true,
            recordingColorPolishApplied = _pendingVideoColorSettings.HasVisibleAdjustments,
            recordingMatchesPreviewColor = true,
            note = _pendingVideoDenoiseEnabled && _pendingVideoColorSettings.HasVisibleAdjustments
                ? "Preview denoise and color polish were applied before recording frames were written."
                : _pendingVideoDenoiseEnabled
                ? "Preview denoise was applied before recording frames were written."
                : _pendingVideoColorSettings.HasVisibleAdjustments
                    ? "Color polish was applied before recording frames were written."
                : "Preview denoise was off."
        };
    }

    private object CreateVideoColorMetadata()
    {
        return new
        {
            _pendingVideoColorSettings.Enabled,
            _pendingVideoColorSettings.Exposure,
            _pendingVideoColorSettings.Contrast,
            _pendingVideoColorSettings.Saturation,
            _pendingVideoColorSettings.Warmth
        };
    }

    private static string CreateAudioRecordingFileName(DateTime timestamp)
    {
        return $"pwRecording_{timestamp:yyyy-MM-dd_HH-mm-ss}.wav";
    }

    private string CreateAudioRecordingFilePath(DateTime timestamp)
    {
        Directory.CreateDirectory(_audioRecordingFolder);
        var baseName = System.IO.Path.GetFileNameWithoutExtension(CreateAudioRecordingFileName(timestamp));
        var path = System.IO.Path.Combine(_audioRecordingFolder, $"{baseName}.wav");
        for (var attempt = 1; File.Exists(path) && attempt < 100; attempt++)
        {
            path = System.IO.Path.Combine(_audioRecordingFolder, $"{baseName}_{attempt:00}.wav");
        }

        return path;
    }

    private void SpectrumViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = false;
        _showWaveform3D = false;
        _showMicCompare = false;
        _spectrumService.StereoInputAnalysisEnabled = false;
        UpdateWaveformSampleRetention();
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Visible;
        MicCompareLegendPanel.Visibility = Visibility.Collapsed;
        UpdateMicCompareUiState();
    }

    private void WaveformViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = true;
        _showWaveform3D = false;
        _showMicCompare = false;
        _spectrumService.StereoInputAnalysisEnabled = false;
        UpdateWaveformSampleRetention();
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Visible;
        MicCompareLegendPanel.Visibility = Visibility.Collapsed;
        UpdateMicCompareUiState();
    }

    private void MicCompareViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = false;
        _showWaveform3D = false;
        _showMicCompare = true;
        _spectrumService.StereoInputAnalysisEnabled = true;
        UpdateWaveformSampleRetention();
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Collapsed;
        MicCompareLegendPanel.Visibility = Visibility.Visible;
        UpdateMicCompareUiState();
    }

    private void Waveform3DClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = false;
        _showWaveform3D = true;
        _showMicCompare = false;
        _spectrumService.StereoInputAnalysisEnabled = false;
        EnsureInlineWaveform3DView();
        UpdateWaveformSampleRetention();
        UpdateGraphSurfaceVisibility();
        NormalSpectrumLegendPanel.Visibility = Visibility.Collapsed;
        MicCompareLegendPanel.Visibility = Visibility.Collapsed;
        UpdateMicCompareUiState();

        if (_latestFrame is not null)
        {
            _waveform3DWindow?.AcceptFrame(_latestFrame);
        }
    }

    private void UpdateWaveformSampleRetention()
    {
        _spectrumService.WaveformSamplesEnabled = _showWaveform || _showWaveform3D;
    }

    private void UpdateGraphSurfaceVisibility()
    {
        SpectrumCanvas.Visibility = _showWaveform || _showWaveform3D
            ? Visibility.Collapsed
            : Visibility.Visible;
        WaveformCanvas.Visibility = _showWaveform
            ? Visibility.Visible
            : Visibility.Collapsed;
        InlineWaveform3DHost.Visibility = _showWaveform3D
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateGraphViewButtonStates();
        UpdateEqualizerHoverRegion();
    }

    private void UpdateGraphViewButtonStates()
    {
        SpectrumViewButton.IsChecked = !_showWaveform && !_showWaveform3D && !_showMicCompare;
        WaveformViewButton.IsChecked = _showWaveform;
        Waveform3DButton.IsChecked = _showWaveform3D;
        MicCompareViewButton.IsChecked = _showMicCompare;
    }

    private void EnsureInlineWaveform3DView()
    {
        if (_waveform3DWindow is not null)
        {
            return;
        }

        var waveform3DWindow = new Waveform3DWindow();
        var content = waveform3DWindow.Content;
        waveform3DWindow.Content = null;
        InlineWaveform3DHost.Content = content;
        _waveform3DWindow = waveform3DWindow;
    }

    private void UpdateMicCompareUiState()
    {
        MicAnalysisPanel.Visibility = _showMicCompare ? Visibility.Visible : Visibility.Collapsed;
        EqBandPanel.IsEnabled = !_showMicCompare;
        EqBandPanel.Opacity = _showMicCompare ? 0.35d : 1d;
        WaveformOptionsPanel.Visibility = _showWaveform ? Visibility.Visible : Visibility.Collapsed;
        AnalyzerSmoothingPanel.Visibility = _showWaveform || _showWaveform3D ? Visibility.Collapsed : Visibility.Visible;
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
        }
    }

    private void EqBandMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EqualizerBand band }
            && ReferenceEquals(_hoveredEqualizerBand, band))
        {
            _hoveredEqualizerBand = null;
            UpdateEqualizerHoverRegion();
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

    private void DisplayGainSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.ToolTip = "6 is the normal display gain. Drag near the yellow mark to snap back.";
        }
    }

    private void DisplayGainSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        const double neutralGain = 6d;
        if (sender is not Slider slider || Math.Abs(e.NewValue - neutralGain) > 0.35d || Math.Abs(e.NewValue - neutralGain) < 0.001d)
        {
            return;
        }

        slider.Value = neutralGain;
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
        _selectedDevice = MicrophoneComboBox.SelectedItem as AudioInputDevice;
        var selectedDevice = _selectedDevice;
        var selectedDeviceFormat = await GetDeviceFormatAsync(selectedDevice);
        if (!Equals(_selectedDevice, selectedDevice))
        {
            return;
        }

        _selectedDeviceFormat = selectedDeviceFormat;
        await RestartSelectedAudioStreamAsync("Listening");
        PersistAppState();
    }

    private async void InputChannelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputChannelComboBox.SelectedItem is InputChannelOption option)
        {
            _selectedInputChannelMode = option.Mode;
        }

        await RestartSelectedAudioStreamAsync("Listening");
        PersistAppState();
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

            _spectrumService.ConfigureProcessedOutput(enabled, _selectedOutputDevice);
            OutputStatusText.Text = enabled
                ? $"Sending DSP processed mic to selected output: {_selectedOutputDevice.Name}. {BuildOutputFormatStatus()}"
                : "Output off. Pick a virtual cable input when you want podcast routing.";
            if (KaraokeMonitorStatusText is not null)
            {
                KaraokeMonitorStatusText.Text = enabled
                    ? $"Live vocal monitor is feeding {_selectedOutputDevice.Name}. {BuildOutputFormatStatus()}"
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

    private string BuildOutputFormatStatus()
    {
        var inputFormat = _selectedDeviceFormat ?? GetSelectedDeviceFormat();
        var outputFormat = _selectedOutputDevice is null
            ? null
            : MicrophoneSpectrumService.TryGetOutputDeviceFormat(_selectedOutputDevice);
        if (inputFormat is null || outputFormat is null)
        {
            return $"{_spectrumService.ProcessedOutputFormatStatus} Virtual audio cables are supported; choose the matching cable output as the mic in your DAW or podcast app.";
        }

        return inputFormat.Value.SampleRate == outputFormat.Value.SampleRate
            ? $"{_spectrumService.ProcessedOutputFormatStatus} Direct-rate path: mic {inputFormat.Value}, output {outputFormat.Value}. No output resampling."
            : $"{_spectrumService.ProcessedOutputFormatStatus} High-quality output resampling: mic {inputFormat.Value}, output {outputFormat.Value}. Match Windows sample rates for the cleanest possible path.";
    }

    private void UpdateAudioFormatRouteText()
    {
        if (AudioFormatRouteText is null)
        {
            return;
        }

        var inputFormat = _selectedDeviceFormat ?? GetSelectedDeviceFormat();
        var receiving = _spectrumService.IsRunning
            ? _spectrumService.ActiveInputFormatStatus
            : inputFormat?.ToString() ?? "not open";
        var targetOutput = _spectrumService.IsRunning
            ? _spectrumService.TargetProcessedOutputFormatStatus
            : BuildTargetOutputFormatStatus(inputFormat);
        var routed = IsProcessedOutputRequested() && _spectrumService.IsProcessedOutputEnabled;
        var constrained = routed && _spectrumService.IsProcessedOutputFormatConstrained;
        var text = $"Receiving: {receiving} | Trying output: {targetOutput}";

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

    private void StartSelectedDevice()
    {
        if (_selectedDevice is null || _isRestartingAudioStream)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        try
        {
            _spectrumService.Start(_selectedDevice.DeviceNumber, Settings, _selectedInputChannelMode);
            StatusText.Text = "Listening";
            _selectedDeviceFormat ??= GetSelectedDeviceFormat();
            UpdateAudioFormatRouteText();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Mic unavailable: {ex.Message}";
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
                _selectedDeviceFormat = GetSelectedDeviceFormat();
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

        if (!_spectrumService.IsRunning)
        {
            await RestartSelectedAudioStreamAsync("Audio stream stopped; reopened mic stream.");
            return;
        }

        if (_spectrumService.AreAudioCallbacksStale(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(1400)))
        {
            await RestartSelectedAudioStreamAsync("Audio callback stopped; reopened mic stream.");
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

    private async Task RestartSelectedAudioStreamAsync(string statusMessage)
    {
        var selectedDevice = _selectedDevice;
        if (selectedDevice is null || _isClosing)
        {
            return;
        }

        var operationVersion = ++_audioStreamOperationVersion;
        var inputChannelMode = _selectedInputChannelMode;
        _isRestartingAudioStream = true;
        StatusText.Text = "Reopening audio stream...";
        UpdateAudioFormatRouteText();
        try
        {
            await Task.Run(() =>
            {
                _spectrumService.RestartCapture(selectedDevice.DeviceNumber, Settings, inputChannelMode, TimeSpan.FromMilliseconds(850));
            });
            if (_isClosing || operationVersion != _audioStreamOperationVersion || !Equals(_selectedDevice, selectedDevice))
            {
                return;
            }

            StatusText.Text = statusMessage;
            _selectedDeviceFormat ??= await GetDeviceFormatAsync(selectedDevice);
            UpdateAudioFormatRouteText();
        }
        catch (Exception ex)
        {
            if (_isClosing || operationVersion != _audioStreamOperationVersion)
            {
                return;
            }

            StatusText.Text = $"Audio stream refresh failed: {ex.Message}";
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

    private void StartMicAnalysisClicked(object sender, RoutedEventArgs e)
    {
        if (!_showMicCompare)
        {
            MicCompareViewClicked(sender, e);
        }

        if (_latestFrame?.HasStereoInput != true)
        {
            MicAnalysisStatusText.Text = "No stereo mic stream yet. Choose the Scarlett/2-channel input and use Sum L+R.";
            MicAnalysisResultText.Text = "Waiting for both mic channels.";
            return;
        }

        lock (_micAnalysisLock)
        {
            _micAnalysisStartedAt = DateTime.UtcNow;
            _isMicAnalysisRunning = true;
            _micAnalysisReadyToFinalize = false;
            _micAnalysisSequential = MicAnalysisModeComboBox.SelectedIndex == 1;
            _micAnalysisNormalizeVolume = MicAnalysisNormalizeCheckBox.IsChecked == true;
            _micAnalysisInput1Sums = [];
            _micAnalysisInput2Sums = [];
            _micAnalysisInput1FrameCount = 0;
            _micAnalysisInput2FrameCount = 0;
            _micAnalysisInput1Squares = 0d;
            _micAnalysisInput2Squares = 0d;
            _micAnalysisInput1SampleCount = 0;
            _micAnalysisInput2SampleCount = 0;
        }

        _micAnalysisInput1DisplayOffset = 0d;
        _micAnalysisInput2DisplayOffset = 0d;
        StartMicAnalysisButton.IsEnabled = false;
        MicAnalysisModeComboBox.IsEnabled = false;
        MicAnalysisNormalizeCheckBox.IsEnabled = false;
        MicAnalysisProgressMeter.Width = 0;
        MicAnalysisStatusText.Text = _micAnalysisSequential
            ? "Sequential test: speak into mic 1 first."
            : "Testing... speak normally toward both mics.";
        MicAnalysisResultText.Text = _micAnalysisSequential
            ? "Collecting mic 1, then mic 2."
            : "Collecting raw mic 1 and mic 2 data.";
    }

    private void CollectMicAnalysisFrame(SpectrumFrame frame)
    {
        if (!_isMicAnalysisRunning || !frame.HasStereoInput)
        {
            return;
        }

        lock (_micAnalysisLock)
        {
            if (!_isMicAnalysisRunning)
            {
                return;
            }

            if (_micAnalysisInput1Sums.Length != frame.Input1Magnitudes.Length)
            {
                _micAnalysisInput1Sums = new double[frame.Input1Magnitudes.Length];
                _micAnalysisInput2Sums = new double[frame.Input2Magnitudes.Length];
                _micAnalysisInput1FrameCount = 0;
                _micAnalysisInput2FrameCount = 0;
            }

            var elapsed = (DateTime.UtcNow - _micAnalysisStartedAt).TotalSeconds;
            var collectInput1 = !_micAnalysisSequential || elapsed < SequentialMicAnalysisPhaseSeconds;
            var collectInput2 = !_micAnalysisSequential || elapsed >= SequentialMicAnalysisPhaseSeconds;

            for (var i = 0; i < _micAnalysisInput1Sums.Length; i++)
            {
                if (collectInput1)
                {
                    _micAnalysisInput1Sums[i] += frame.Input1Magnitudes[i];
                }

                if (collectInput2)
                {
                    _micAnalysisInput2Sums[i] += frame.Input2Magnitudes[Math.Min(i, frame.Input2Magnitudes.Length - 1)];
                }
            }

            if (collectInput1)
            {
                foreach (var sample in frame.Input1Samples)
                {
                    _micAnalysisInput1Squares += sample * sample;
                }

                _micAnalysisInput1SampleCount += frame.Input1Samples.Length;
                _micAnalysisInput1FrameCount++;
            }

            if (collectInput2)
            {
                foreach (var sample in frame.Input2Samples)
                {
                    _micAnalysisInput2Squares += sample * sample;
                }

                _micAnalysisInput2SampleCount += frame.Input2Samples.Length;
                _micAnalysisInput2FrameCount++;
            }

            var totalDuration = _micAnalysisSequential
                ? SequentialMicAnalysisPhaseSeconds * 2d
                : MicAnalysisDurationSeconds;
            if (elapsed >= totalDuration)
            {
                _isMicAnalysisRunning = false;
                _micAnalysisReadyToFinalize = true;
            }
        }
    }

    private void UpdateMicAnalysisProgress()
    {
        if (_isMicAnalysisRunning)
        {
            var elapsed = (DateTime.UtcNow - _micAnalysisStartedAt).TotalSeconds;
            var totalDuration = _micAnalysisSequential
                ? SequentialMicAnalysisPhaseSeconds * 2d
                : MicAnalysisDurationSeconds;
            var progress = Math.Clamp(elapsed / totalDuration, 0d, 1d);
            MicAnalysisProgressMeter.Width = progress * Math.Max(1d, MicAnalysisProgressMeter.ActualWidth == 0 ? 280d : ((FrameworkElement)MicAnalysisProgressMeter.Parent).ActualWidth);
            MicAnalysisStatusText.Text = _micAnalysisSequential
                ? elapsed < SequentialMicAnalysisPhaseSeconds
                    ? $"Sequential: speak into mic 1. {Math.Max(0d, SequentialMicAnalysisPhaseSeconds - elapsed):0.0}s left"
                    : $"Sequential: now speak into mic 2. {Math.Max(0d, totalDuration - elapsed):0.0}s left"
                : $"Testing both mics... {Math.Max(0d, totalDuration - elapsed):0.0}s left";
        }

        if (_micAnalysisReadyToFinalize)
        {
            FinalizeMicAnalysis();
        }
    }

    private void FinalizeMicAnalysis()
    {
        double[] input1;
        double[] input2;
        int input1FrameCount;
        int input2FrameCount;
        double input1Squares;
        double input2Squares;
        long input1SampleCount;
        long input2SampleCount;

        lock (_micAnalysisLock)
        {
            if (!_micAnalysisReadyToFinalize)
            {
                return;
            }

            input1 = [.. _micAnalysisInput1Sums];
            input2 = [.. _micAnalysisInput2Sums];
            input1FrameCount = _micAnalysisInput1FrameCount;
            input2FrameCount = _micAnalysisInput2FrameCount;
            input1Squares = _micAnalysisInput1Squares;
            input2Squares = _micAnalysisInput2Squares;
            input1SampleCount = _micAnalysisInput1SampleCount;
            input2SampleCount = _micAnalysisInput2SampleCount;
            _micAnalysisReadyToFinalize = false;
        }

        StartMicAnalysisButton.IsEnabled = true;
        MicAnalysisModeComboBox.IsEnabled = true;
        MicAnalysisNormalizeCheckBox.IsEnabled = true;
        MicAnalysisProgressMeter.Width = ((FrameworkElement)MicAnalysisProgressMeter.Parent).ActualWidth;

        if (input1FrameCount < 8 || input2FrameCount < 8 || input1SampleCount == 0 || input2SampleCount == 0 || input1.Length == 0 || input2.Length == 0)
        {
            MicAnalysisStatusText.Text = "Test did not capture enough stereo data.";
            MicAnalysisResultText.Text = "Try again with Sum L+R selected and both mics active.";
            return;
        }

        for (var i = 0; i < input1.Length; i++)
        {
            input1[i] /= input1FrameCount;
            input2[i] /= input2FrameCount;
        }

        var rms1 = Math.Sqrt(input1Squares / Math.Max(1, input1SampleCount));
        var rms2 = Math.Sqrt(input2Squares / Math.Max(1, input2SampleCount));
        var levelDeltaDb = 20d * Math.Log10((rms1 + 0.0000001d) / (rms2 + 0.0000001d));
        ApplyMicCompareLevelMatch(_micAnalysisNormalizeVolume ? levelDeltaDb : 0d);

        var result = BuildMicAnalysisResult(input1, input2, levelDeltaDb, _micAnalysisNormalizeVolume);
        MicAnalysisStatusText.Text = _micAnalysisNormalizeVolume
            ? "Analysis complete. Live compare lines are volume-normalized."
            : "Analysis complete. Volume normalization was off.";
        MicAnalysisResultText.Text = result;
    }

    private void ApplyMicCompareLevelMatch(double mic1MinusMic2Db)
    {
        _micAnalysisInput1DisplayOffset = 0d;
        _micAnalysisInput2DisplayOffset = 0d;

        var visualOffset = Math.Abs(mic1MinusMic2Db) / 70d;
        if (mic1MinusMic2Db > 0.25d)
        {
            _micAnalysisInput1DisplayOffset = -visualOffset;
        }
        else if (mic1MinusMic2Db < -0.25d)
        {
            _micAnalysisInput2DisplayOffset = -visualOffset;
        }
    }

    private string BuildMicAnalysisResult(double[] input1, double[] input2, double levelDeltaDb, bool normalizeVolume)
    {
        var lines = new List<string>();
        var louder = Math.Abs(levelDeltaDb) < 0.6d
            ? "Levels are very close."
            : normalizeVolume
                ? levelDeltaDb > 0d
                    ? $"Mic 1 is {levelDeltaDb:0.0} dB louder; it was normalized down for comparison."
                    : $"Mic 2 is {Math.Abs(levelDeltaDb):0.0} dB louder; it was normalized down for comparison."
                : levelDeltaDb > 0d
                    ? $"Mic 1 is {levelDeltaDb:0.0} dB louder; tone comparison includes that level difference."
                    : $"Mic 2 is {Math.Abs(levelDeltaDb):0.0} dB louder; tone comparison includes that level difference.";
        lines.Add(louder);

        var comparisonLevelDeltaDb = normalizeVolume ? levelDeltaDb : 0d;
        var zoneDifferences = _voiceZones
            .Select(zone => CreateZoneDifference(zone, input1, input2, comparisonLevelDeltaDb))
            .OrderByDescending(zone => Math.Abs(zone.DifferenceDb))
            .ToList();

        var strongest = zoneDifferences.FirstOrDefault(zone => Math.Abs(zone.DifferenceDb) >= 0.8d);
        if (strongest is null)
        {
            lines.Add("After level matching, their spectral shape is extremely similar.");
        }
        else
        {
            lines.Add(strongest.DifferenceDb > 0d
                ? $"Biggest split: Mic 1 has more {strongest.ZoneName.ToLowerInvariant()} by about {strongest.DifferenceDb:0.0} dB."
                : $"Biggest split: Mic 2 has more {strongest.ZoneName.ToLowerInvariant()} by about {Math.Abs(strongest.DifferenceDb):0.0} dB.");
        }

        foreach (var zone in zoneDifferences.Where(zone => Math.Abs(zone.DifferenceDb) >= 0.8d).Take(3))
        {
            lines.Add(zone.DifferenceDb > 0d
                ? $"Mic 1 +{zone.DifferenceDb:0.0} dB in {zone.ZoneName}."
                : $"Mic 2 +{Math.Abs(zone.DifferenceDb):0.0} dB in {zone.ZoneName}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static MicZoneDifference CreateZoneDifference(VoiceZone zone, double[] input1, double[] input2, double levelDeltaDb)
    {
        var start = BarIndexForFrequency(zone.StartFrequencyHz, input1.Length);
        var end = Math.Max(start + 1, BarIndexForFrequency(zone.EndFrequencyHz, input1.Length));
        end = Math.Min(end, input1.Length);

        var average1 = AverageRange(input1, start, end);
        var average2 = AverageRange(input2, start, end);
        var zoneDeltaDb = 70d * (average1 - average2) - levelDeltaDb;
        return new MicZoneDifference(zone.Name, zoneDeltaDb);
    }

    private static int BarIndexForFrequency(double frequency, int barCount)
    {
        var clamped = Math.Clamp(frequency, MinimumDisplayFrequency, MaximumDisplayFrequency);
        var position = Math.Log(clamped / MinimumDisplayFrequency) / Math.Log(MaximumDisplayFrequency / MinimumDisplayFrequency);
        return Math.Clamp((int)Math.Round(position * Math.Max(1, barCount - 1)), 0, Math.Max(0, barCount - 1));
    }

    private static double AverageRange(double[] values, int start, int end)
    {
        var sum = 0d;
        var count = 0;
        for (var i = start; i < end && i < values.Length; i++)
        {
            sum += values[i];
            count++;
        }

        return count == 0 ? 0d : sum / count;
    }

    private void SpectrumAvailable(object? sender, SpectrumFrame frame)
    {
        System.Threading.Interlocked.Exchange(ref _pendingSpectrumFrame, frame);
        if (System.Threading.Interlocked.Exchange(ref _spectrumFrameUpdateQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke((Action)ProcessPendingSpectrumFrame, DispatcherPriority.Background);
    }

    private void ProcessPendingSpectrumFrame()
    {
        var frame = System.Threading.Interlocked.Exchange(ref _pendingSpectrumFrame, null);
        if (frame is not null)
        {
            AcceptSpectrumFrame(frame);
        }

        System.Threading.Volatile.Write(ref _spectrumFrameUpdateQueued, 0);
        if (System.Threading.Volatile.Read(ref _pendingSpectrumFrame) is not null
            && System.Threading.Interlocked.Exchange(ref _spectrumFrameUpdateQueued, 1) == 0)
        {
            Dispatcher.BeginInvoke((Action)ProcessPendingSpectrumFrame, DispatcherPriority.Background);
        }
    }

    private void AcceptSpectrumFrame(SpectrumFrame frame)
    {
        _latestFrame = frame;
        _waveformSampleRate = Math.Max(8000, frame.SampleRate);
        CollectMicAnalysisFrame(frame);
        if (_showWaveform3D)
        {
            _waveform3DWindow?.AcceptFrame(frame);
        }

        AppendWaveformHistory(frame.RawSamples, frame.ProcessedSamples);
    }

    private void CompositionTargetRendering(object? sender, EventArgs e)
    {
        UpdateRecordingTimer();
        UpdateRecordingHealthPanel();
        SyncStandaloneAudioRecordingState();
        UpdateMicAnalysisProgress();

        if (_latestFrame is null)
        {
            return;
        }

        RenderSpectrum(_latestFrame);
        RenderWaveform(_latestFrame);
        RenderRecordingSignalStrip(_latestFrame);
    }

    private void UpdateRecordingTimer()
    {
        if (!_isRecordingSession)
        {
            return;
        }

        RecordingTimerText.Text = FormatDuration(GetRecordingElapsed());
    }

    private void UpdateRecordingHealthPanel()
    {
        if (RecordingHealthText is null || PreviewRecordParityText is null || VideoPipelineText is null)
        {
            return;
        }

        var pipeline = Dx12Camera.FormatActiveVideoPipeline(
            _cameraAvailable,
            _isCameraEnabled,
            _dx12Camera,
            _pendingVideoDenoiseEnabled,
            _pendingVideoColorSettings.HasVisibleAdjustments,
            Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice),
            _direct3D12PreviewHost?.IsReady == true,
            _lastTextureNativeCameraError);
        VideoPipelineText.Text = $"Pipeline: {pipeline}";
        var parity = Dx12Camera.FormatPreviewRecordParity(
            _isCameraEnabled,
            _dx12Camera,
            _pendingVideoDenoiseEnabled,
            _pendingVideoColorSettings.HasVisibleAdjustments,
            out var parityIsGood);
        PreviewRecordParityText.Text = $"Preview/record: {parity}";
        PreviewRecordParityText.Foreground = parityIsGood ? _meterGoodBrush : _meterWarnBrush;

        if (!_isRecordingSession)
        {
            RecordingHealthText.Text = "Idle";
            RecordingHealthText.Foreground = _meterTextMutedBrush;
            return;
        }

        var (offered, written, skipped) = GetActiveRecordingCounters();
        var encoderDelay = Math.Max(0, offered - written);
        var audioPeak = _latestFrame?.PeakLevel ?? 0d;
        var diskStatus = GetRecordingDiskStatus();
        RecordingHealthText.Text = $"Frames {written}/{Math.Max(offered, written)}  skipped {skipped}  enc lag {encoderDelay}  audio {audioPeak:P0}  disk {diskStatus}";
        RecordingHealthText.Foreground = skipped > 0 || encoderDelay > 3
            ? _meterWarnBrush
            : _meterGoodBrush;
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

        return Dx12Camera.IsSelectedDirectShowCamera(_isDirectShowPreviewActive, CameraComboBox.SelectedItem as CameraDevice)
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
                ? FormatFileSize(new FileInfo(path).Length)
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
            var output = new WaveOutEvent();
            output.PlaybackStopped += AudioPlaybackStopped;
            output.Init(reader);

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
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac|All files|*.*",
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
            StopKaraokePlayback(clearTrack: false);
            var track = _karaokeQueue.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
                ?? CreateKaraokeTrackItem(path);
            _karaokeTrackPath = path;
            _karaokeTrackDuration = track.Duration;
            if (updateQueueSelection && KaraokeQueueDataGrid is not null)
            {
                KaraokeQueueDataGrid.SelectedItem = _karaokeQueue.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (KaraokeQueueDataGrid.SelectedItem is not null)
                {
                    KaraokeQueueDataGrid.ScrollIntoView(KaraokeQueueDataGrid.SelectedItem);
                }
            }

            UpdateKaraokeTrackUi();
            var lyricsLoaded = TryAutoLoadKaraokeLyricsForTrack(path);
            KaraokePlaybackStatusText.Text = $"Loaded {track.Name}.";
            if (lyricsLoaded)
            {
                KaraokeStatusText.Text = "Backing track ready with matching local lyrics.";
            }
            else
            {
                KaraokeStatusText.Text = "Backing track ready.";
                if (!_isRestoringAppState && string.IsNullOrWhiteSpace(KaraokeLyricsTextBox?.Text))
                {
                    _ = DetectKaraokeLyricsForTrackAsync(path, tryLocalSidecarFirst: false);
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
        var dialog = new OpenFolderDialog
        {
            Title = "Choose karaoke song folder",
            InitialDirectory = initialFolder
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _karaokeBrowserFolder = dialog.FolderName;
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

        foreach (var path in Directory.EnumerateFiles(_karaokeBrowserFolder)
                     .Where(IsSupportedKaraokeTrackFile)
                     .OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var track = CreateKaraokeTrackItem(path);
                _karaokeBrowserFiles.Add(new KaraokeBrowserFileItem(
                    path,
                    track.Name,
                    $"{track.Artist}  {track.DurationText}"));
            }
            catch
            {
            }
        }

        KaraokeStatusText.Text = _karaokeBrowserFiles.Count == 0
            ? "No supported WAV or MP3 backing tracks found in that folder."
            : $"Found {_karaokeBrowserFiles.Count} backing tracks.";
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
        foreach (var path in paths
                     .Where(IsSupportedKaraokeTrackFile)
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
                KaraokeStatusText.Text = $"Could not add {System.IO.Path.GetFileName(path)}: {ex.Message}";
            }
        }

        if (added.Count > 0)
        {
            var selectedTrack = selectFirstAdded
                ? added[0]
                : _karaokeQueue.FirstOrDefault(item => string.Equals(item.Path, _karaokeTrackPath, StringComparison.OrdinalIgnoreCase))
                  ?? _karaokeQueue.FirstOrDefault();
            if (selectedTrack is not null)
            {
                KaraokeQueueDataGrid.SelectedItem = selectedTrack;
                SetKaraokeTrack(selectedTrack.Path);
            }

            KaraokeStatusText.Text = added.Count == 1
                ? $"Queued {added[0].Name}."
                : $"Queued {added.Count} backing tracks.";
        }

        UpdateKaraokeQueueControls();
        UpdateKaraokeTransportControls();
        return added.Count;
    }

    private void KaraokeQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
            SetKaraokeTrack(item.Path);
            StartKaraokePlayback();
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

        if (removedCurrentTrack)
        {
            StopKaraokePlayback(clearTrack: true);
            var next = _karaokeQueue.Count == 0 ? null : _karaokeQueue[Math.Clamp(index, 0, _karaokeQueue.Count - 1)];
            if (next is not null)
            {
                KaraokeQueueDataGrid.SelectedItem = next;
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
        KaraokeQueueDataGrid.SelectedItem = next;
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
        using var reader = new AudioFileReader(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var artist = "Unknown";
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
            reader.TotalTime,
            FormatDuration(reader.TotalTime));
    }

    private static bool IsSupportedKaraokeTrackFile(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wma", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase);
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
            if (_karaokeTrackOutput is null || _karaokeTrackReader is null)
            {
                var reader = new AudioFileReader(_karaokeTrackPath);
                var rateProvider = new KaraokeRateSampleProvider(reader, GetKaraokeTempoRatio());
                var pitchProvider = new SmbPitchShiftingSampleProvider(rateProvider)
                {
                    PitchFactor = (float)GetKaraokePitchFactor()
                };
                var vocalReductionProvider = new KaraokeVocalReductionSampleProvider(pitchProvider, _karaokeVocalReductionEnabled);
                var output = new WaveOutEvent
                {
                    DeviceNumber = _selectedOutputDevice?.DeviceNumber ?? -1,
                    DesiredLatency = 90
                };
                output.PlaybackStopped += KaraokePlaybackStopped;
                output.Init(vocalReductionProvider);
                _karaokeTrackReader = reader;
                _karaokeTrackRateProvider = rateProvider;
                _karaokeTrackPitchProvider = pitchProvider;
                _karaokeTrackVocalReductionProvider = vocalReductionProvider;
                _karaokeTrackOutput = output;
                _karaokeTrackDuration = reader.TotalTime;
                SeekKaraokePlaybackTo(TimeSpan.FromSeconds(Math.Clamp(KaraokeSeekSlider.Value, 0d, Math.Max(0d, _karaokeTrackDuration.TotalSeconds))));
            }
            else
            {
                ApplyKaraokeTimePitchSettings();
                _karaokeTrackVocalReductionProvider?.SetEnabled(_karaokeVocalReductionEnabled);
            }

            _isStoppingKaraokePlayback = false;
            _karaokeTrackOutput.Play();
            _isKaraokeTrackPlaying = true;
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
        if (_karaokeTrackOutput is null)
        {
            return;
        }

        _karaokeTrackOutput.Pause();
        _isKaraokeTrackPlaying = false;
        _karaokePlaybackPositionTimer.Stop();
        UpdateKaraokePlaybackProgress();
        KaraokePlaybackStatusText.Text = "Backing track paused.";
        UpdateKaraokeTransportControls();
    }

    private void StopKaraokePlayback(bool clearTrack)
    {
        _karaokePlaybackPositionTimer.Stop();
        _isKaraokeTrackPlaying = false;
        _isScrubbingKaraokePlayback = false;
        if (KaraokeSeekSlider is not null && KaraokeSeekSlider.IsMouseCaptured)
        {
            KaraokeSeekSlider.ReleaseMouseCapture();
        }

        var output = _karaokeTrackOutput;
        var reader = _karaokeTrackReader;
        _karaokeTrackOutput = null;
        _karaokeTrackReader = null;
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

        _isStoppingKaraokePlayback = false;
        if (clearTrack)
        {
            _karaokeTrackPath = null;
            _karaokeTrackDuration = TimeSpan.Zero;
        }
    }

    private void KaraokePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var wasStoppedByUser = _isStoppingKaraokePlayback;
            var trackName = string.IsNullOrWhiteSpace(_karaokeTrackPath)
                ? "track"
                : System.IO.Path.GetFileName(_karaokeTrackPath);
            StopKaraokePlayback(clearTrack: false);
            if (e.Exception is not null)
            {
                KaraokePlaybackStatusText.Text = $"Playback failed: {e.Exception.Message}";
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
        }, DispatcherPriority.Background);
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

        var position = _karaokeTrackReader?.CurrentTime ?? TimeSpan.FromSeconds(Math.Clamp(KaraokeSeekSlider.Value, 0d, Math.Max(0d, _karaokeTrackDuration.TotalSeconds)));
        var duration = _karaokeTrackDuration;
        var maxSeconds = Math.Max(1d, duration.TotalSeconds);
        var positionSeconds = Math.Clamp(position.TotalSeconds, 0d, maxSeconds);

        _isUpdatingKaraokePlaybackPosition = true;
        try
        {
            KaraokeSeekSlider.Minimum = 0d;
            KaraokeSeekSlider.Maximum = maxSeconds;
            KaraokeSeekSlider.Value = positionSeconds;
        }
        finally
        {
            _isUpdatingKaraokePlaybackPosition = false;
        }

        KaraokePositionText.Text = FormatDuration(TimeSpan.FromSeconds(positionSeconds));
        KaraokeDurationText.Text = duration > TimeSpan.Zero ? FormatDuration(duration) : "00:00:00";
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
        UpdateActiveKaraokeLyric(clamped);
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
            KaraokeLyricsTextBox.Text = File.ReadAllText(dialog.FileName);
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
                    "Load a backing track, paste lyrics, and sing through the processed mic chain.",
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
        RenderKaraokeLyrics(-1, -1);

        var position = _karaokeTrackReader?.CurrentTime
            ?? TimeSpan.FromSeconds(Math.Clamp(KaraokeSeekSlider?.Value ?? 0d, 0d, Math.Max(0d, _karaokeTrackDuration.TotalSeconds)));
        UpdateActiveKaraokeLyric(position);
    }

    private bool TryAutoLoadKaraokeLyricsForTrack(string trackPath)
    {
        if (KaraokeLyricsTextBox is null
            || string.IsNullOrWhiteSpace(trackPath)
            || !File.Exists(trackPath)
            || !string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text))
        {
            return false;
        }

        var lyricsPath = FindLocalKaraokeLyricsFile(trackPath);
        if (string.IsNullOrWhiteSpace(lyricsPath))
        {
            return false;
        }

        try
        {
            KaraokeLyricsTextBox.Text = File.ReadAllText(lyricsPath);
            KaraokeStatusText.Text = $"Auto-loaded lyrics from {System.IO.Path.GetFileName(lyricsPath)}.";
            return true;
        }
        catch (Exception ex)
        {
            KaraokeStatusText.Text = $"Auto lyrics load failed: {ex.Message}";
            return false;
        }
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

        if (tryLocalSidecarFirst && string.IsNullOrWhiteSpace(KaraokeLyricsTextBox.Text) && TryAutoLoadKaraokeLyricsForTrack(trackPath))
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

        _isDetectingKaraokeLyrics = true;
        DetectKaraokeLyricsButton.IsEnabled = false;
        KaraokeStatusText.Text = "Separating lead vocal for pro lyric detection...";
        try
        {
            void Report(string message)
            {
                Dispatcher.Invoke(() => KaraokeStatusText.Text = message);
            }

            var detectedLyrics = await Task.Run(() => GenerateKaraokeLyricsWithAi(trackPath, toolchain, Report));
            if (!string.Equals(_karaokeTrackPath, trackPath, StringComparison.OrdinalIgnoreCase))
            {
                KaraokeStatusText.Text = "Lyric detection finished for a previous backing track.";
                return;
            }

            KaraokeLyricsTextBox.Text = detectedLyrics.LyricsText;
            KaraokeStatusText.Text = $"Detected {detectedLyrics.LineCount} lyric lines with {detectedLyrics.SyllableCount} timed syllables.";
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
        var rawTokens = SplitKaraokeSyllableTokens(text);
        if (rawTokens.Count == 0)
        {
            return [];
        }

        var singableTokens = rawTokens
            .Where(token => token.IsSingable)
            .ToList();
        if (singableTokens.Count == 0)
        {
            return rawTokens
                .Select(token => new KaraokeLyricToken(token.Text, null, null, false))
                .ToList();
        }

        var weights = singableTokens
            .Select(token => Math.Max(1d, token.Text.Count(char.IsLetterOrDigit) * 0.72d))
            .ToList();
        var totalWeight = Math.Max(0.01d, weights.Sum());
        var hasExplicitEnd = end.HasValue && end.Value > start;
        var duration = hasExplicitEnd
            ? end!.Value - start
            : TimeSpan.FromMilliseconds(Math.Clamp(totalWeight * 120d, 180d, 900d));
        var cursor = start;
        var singableIndex = 0;
        var result = new List<KaraokeLyricToken>(rawTokens.Count);
        foreach (var rawToken in rawTokens)
        {
            if (!rawToken.IsSingable)
            {
                result.Add(new KaraokeLyricToken(rawToken.Text, null, null, false));
                continue;
            }

            var tokenDuration = TimeSpan.FromSeconds(duration.TotalSeconds * weights[singableIndex] / totalWeight);
            TimeSpan? tokenEnd = singableIndex == singableTokens.Count - 1 && !hasExplicitEnd
                ? null
                : singableIndex == singableTokens.Count - 1
                    ? end!.Value
                : cursor + tokenDuration;
            if (!tokenEnd.HasValue || tokenEnd.Value <= cursor)
            {
                tokenEnd = singableIndex == singableTokens.Count - 1 && !hasExplicitEnd
                    ? null
                    : cursor + TimeSpan.FromMilliseconds(120);
            }

            result.Add(new KaraokeLyricToken(rawToken.Text, cursor, tokenEnd, true));
            cursor = tokenEnd ?? cursor + tokenDuration;
            singableIndex++;
        }

        return result;
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

        timestamp = TimeSpan.FromMilliseconds(minutes * 60000d + seconds * 1000d + milliseconds);
        return true;
    }

    private static List<KaraokeTimedLyricLine> GenerateKaraokeLyricTimingsFromAudio(string trackPath, IReadOnlyList<string> lyricLines)
    {
        using var reader = new AudioFileReader(trackPath);
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

    private static List<KaraokeAudioEnergyWindow> ReadKaraokeAudioEnergyWindows(AudioFileReader reader)
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
        Directory.CreateDirectory(KaraokeAiWorkFolder);
        var workFolder = System.IO.Path.Combine(
            KaraokeAiWorkFolder,
            $"detect_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workFolder);

        try
        {
            reportStatus("Separating lead vocal with Demucs...");
            var vocalPath = RunDemucsVocalSeparation(trackPath, workFolder, toolchain, reportStatus);

            reportStatus("Aligning separated vocal with WhisperX...");
            var whisperJsonPath = RunWhisperXAlignment(vocalPath, workFolder, toolchain, reportStatus);

            reportStatus("Building bouncing-ball lyric timing...");
            var words = ReadWhisperXWords(whisperJsonPath);
            if (words.Count == 0)
            {
                throw new InvalidOperationException("WhisperX did not return word-level timings for this track.");
            }

            var lyricLines = GroupKaraokeWordsIntoLines(words);
            var lyricText = BuildEnhancedKaraokeLrc(lyricLines, out var syllableCount);
            if (string.IsNullOrWhiteSpace(lyricText))
            {
                throw new InvalidOperationException("Lyric detection finished without usable lyric text.");
            }

            return new KaraokeDetectedLyrics(lyricText, lyricLines.Count, syllableCount);
        }
        finally
        {
            try
            {
                Directory.Delete(workFolder, recursive: true);
            }
            catch
            {
            }
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
        var arguments = $"--two-stems=vocals -o {QuoteProcessArgument(outputFolder)} {QuoteProcessArgument(trackPath)}";
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
            arguments = $"--two-stems=vocals --mp3 -o {QuoteProcessArgument(outputFolder)} {QuoteProcessArgument(trackPath)}";
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

        var gpuArguments = $"{QuoteProcessArgument(vocalPath)} --model large-v3 --language en --output_format json --output_dir {QuoteProcessArgument(outputFolder)} --align_model WAV2VEC2_ASR_LARGE_LV60K_960H --return_char_alignments --vad_method silero --batch_size 4 --compute_type float16";
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

            var cpuArguments = $"{QuoteProcessArgument(vocalPath)} --model medium --language en --output_format json --output_dir {QuoteProcessArgument(outputFolder)} --return_char_alignments --vad_method silero --device cpu --compute_type int8";
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
        string arguments,
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
            RunKaraokeProcess(pythonPath, $"-m {pythonModuleName} {arguments}", workingDirectory, timeout);
            return;
        }

        throw new InvalidOperationException($"{pythonModuleName} is not available.");
    }

    private static string RunKaraokeProcess(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

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
            var details = ShortenKaraokeProcessError(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
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
            var segmentCharacters = ReadWhisperXCharacters(segment);
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

                    var characters = ReadWhisperXCharacters(word);
                    if (characters.Count == 0 && segmentCharacters.Count > 0)
                    {
                        characters = GetWhisperXCharactersForWord(
                            segmentCharacters,
                            TimeSpan.FromSeconds(startSeconds),
                            TimeSpan.FromSeconds(endSeconds));
                    }

                    detectedWords.Add(new KaraokeDetectedWord(
                        wordText,
                        TimeSpan.FromSeconds(startSeconds),
                        TimeSpan.FromSeconds(endSeconds),
                        characters.Count > 0 ? characters : null));
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
        const double maxGapSeconds = 1.05d;
        const double maxLineSeconds = 5.8d;
        const int maxLineCharacters = 56;
        var lines = new List<List<KaraokeDetectedWord>>();
        var current = new List<KaraokeDetectedWord>();

        foreach (var word in words)
        {
            if (current.Count > 0)
            {
                var gapSeconds = (word.Start - current[^1].End).TotalSeconds;
                var lineSeconds = (word.End - current[0].Start).TotalSeconds;
                var characterCount = current.Sum(item => item.Text.Length) + current.Count + word.Text.Length;
                if (gapSeconds > maxGapSeconds || lineSeconds > maxLineSeconds || characterCount > maxLineCharacters)
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

    private static string BuildEnhancedKaraokeLrc(IReadOnlyList<List<KaraokeDetectedWord>> lyricLines, out int syllableCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[by:Podcast Workbench AI Lyric Detection]");
        builder.AppendLine("[re:Demucs vocals + WhisperX word alignment]");
        syllableCount = 0;

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
                    syllableCount++;
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
                var syllables = SplitKaraokeWordIntoSyllables(core);
                var characterTimedSyllables = CreateTimedKaraokeSyllablesFromCharacters(core, syllables, word);
                if (characterTimedSyllables.Count == syllables.Count)
                {
                    tokens.AddRange(characterTimedSyllables);
                }
                else
                {
                    var weights = syllables
                        .Select(syllable => Math.Max(1d, syllable.Count(char.IsLetterOrDigit) * 0.72d))
                        .ToList();
                    var totalWeight = Math.Max(1d, weights.Sum());
                    var duration = word.End > word.Start
                        ? word.End - word.Start
                        : TimeSpan.FromMilliseconds(Math.Max(160d, core.Length * 45d));
                    var cursor = word.Start;

                    for (var i = 0; i < syllables.Count; i++)
                    {
                        var tokenDuration = TimeSpan.FromSeconds(duration.TotalSeconds * weights[i] / totalWeight);
                        var tokenEnd = i == syllables.Count - 1 ? word.End : cursor + tokenDuration;
                        if (tokenEnd <= cursor)
                        {
                            tokenEnd = cursor + TimeSpan.FromMilliseconds(120);
                        }

                        tokens.Add(new KaraokeLyricToken(syllables[i], cursor, tokenEnd, true));
                        cursor = tokenEnd;
                    }
                }
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

    private static string QuoteProcessArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
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

        var activeLineIndex = 0;
        var timedIndexes = _karaokeLyricDisplayLines
            .Select((line, index) => new { Line = line, Index = index })
            .Where(item => item.Line.Start.HasValue)
            .ToList();
        if (timedIndexes.Count > 0)
        {
            activeLineIndex = timedIndexes.LastOrDefault(item => item.Line.Start!.Value <= position)?.Index
                ?? timedIndexes[0].Index;
        }
        else if (_karaokeTrackDuration > TimeSpan.Zero && _karaokeLyricDisplayLines.Count > 1)
        {
            var ratio = Math.Clamp(position.TotalSeconds / Math.Max(1d, _karaokeTrackDuration.TotalSeconds), 0d, 0.999d);
            activeLineIndex = (int)(ratio * _karaokeLyricDisplayLines.Count);
        }

        var activeLine = _karaokeLyricDisplayLines[Math.Clamp(activeLineIndex, 0, _karaokeLyricDisplayLines.Count - 1)];
        var activeTokenIndex = GetActiveKaraokeTokenIndex(activeLine, position);
        if (_karaokeActiveLineIndex == activeLineIndex && _karaokeActiveTokenIndex == activeTokenIndex)
        {
            return;
        }

        _karaokeActiveLineIndex = activeLineIndex;
        _karaokeActiveTokenIndex = activeTokenIndex;
        RenderKaraokeLyrics(activeLineIndex, activeTokenIndex, position);
    }

    private static int GetActiveKaraokeTokenIndex(KaraokeLyricLineItem line, TimeSpan position)
    {
        if (line.Tokens.Count == 0)
        {
            return -1;
        }

        if (line.Tokens.Any(token => token.IsSingable && token.Start.HasValue))
        {
            var timedTokenIndex = line.Tokens.FindLastIndex(token => token.IsSingable && token.Start.HasValue && token.Start.Value <= position);
            return timedTokenIndex >= 0 ? timedTokenIndex : -1;
        }

        if (!line.Start.HasValue || !line.End.HasValue || line.End <= line.Start)
        {
            return line.Tokens.FindIndex(token => token.IsSingable);
        }

        if (position < line.Start.Value)
        {
            return -1;
        }

        var singableTokens = line.Tokens
            .Select((token, index) => new { token, index })
            .Where(item => item.token.IsSingable)
            .ToList();
        if (singableTokens.Count == 0)
        {
            return -1;
        }

        var ratio = Math.Clamp((position - line.Start.Value).TotalSeconds / Math.Max(0.1d, (line.End.Value - line.Start.Value).TotalSeconds), 0d, 0.999d);
        return singableTokens[(int)(ratio * singableTokens.Count)].index;
    }

    private void RenderKaraokeLyrics(int activeLineIndex, int activeTokenIndex, TimeSpan? position = null)
    {
        if (KaraokeLyricsDisplayPanel is null)
        {
            return;
        }

        KaraokeLyricsDisplayPanel.Children.Clear();
        FrameworkElement? activeLineElement = null;
        for (var lineIndex = 0; lineIndex < _karaokeLyricDisplayLines.Count; lineIndex++)
        {
            var line = _karaokeLyricDisplayLines[lineIndex];
            var isActiveLine = lineIndex == activeLineIndex;
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = isActiveLine ? 38d : 30d,
                FontWeight = isActiveLine ? FontWeights.Bold : FontWeights.SemiBold,
                LineHeight = isActiveLine ? 54d : 44d,
                Foreground = new SolidColorBrush(isActiveLine ? Colors.White : Color.FromRgb(141, 162, 183)),
                Margin = new Thickness(0, 4, 0, 4)
            };

            if (line.Tokens.Count == 0)
            {
                textBlock.Text = line.Text;
            }
            else
            {
                for (var tokenIndex = 0; tokenIndex < line.Tokens.Count; tokenIndex++)
                {
                    var token = line.Tokens[tokenIndex];
                    var isActiveToken = isActiveLine && tokenIndex == activeTokenIndex && token.IsSingable;
                    var isSungToken = isActiveLine
                        && !isActiveToken
                        && position.HasValue
                        && token.IsSingable
                        && token.Start.HasValue
                        && token.Start.Value < position.Value;
                    if (isActiveToken)
                    {
                        textBlock.Inlines.Add(CreateKaraokeBallInline());
                    }

                    textBlock.Inlines.Add(new Run(token.Text)
                    {
                        Foreground = new SolidColorBrush(isActiveToken
                            ? Color.FromRgb(53, 232, 216)
                            : isSungToken
                                ? Color.FromRgb(121, 238, 226)
                            : isActiveLine
                                ? Colors.White
                                : Color.FromRgb(141, 162, 183)),
                        FontWeight = isActiveToken
                            ? FontWeights.ExtraBold
                            : isSungToken
                                ? FontWeights.Bold
                                : textBlock.FontWeight,
                        FontSize = isActiveToken ? textBlock.FontSize + 4d : textBlock.FontSize
                    });
                }
            }

            var border = new Border
            {
                Background = isActiveLine
                    ? new SolidColorBrush(Color.FromArgb(105, 38, 61, 74))
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2),
                Child = textBlock
            };
            KaraokeLyricsDisplayPanel.Children.Add(border);
            if (isActiveLine)
            {
                activeLineElement = border;
            }
        }

        activeLineElement?.BringIntoView();
    }

    private static InlineUIContainer CreateKaraokeBallInline()
    {
        var transform = new TranslateTransform();
        var ball = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = new SolidColorBrush(Color.FromRgb(53, 232, 216)),
            Stroke = new SolidColorBrush(Color.FromRgb(215, 255, 249)),
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 8, 0),
            RenderTransform = transform
        };
        ball.Loaded += (_, _) =>
        {
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = -10d,
                    To = 0d,
                    Duration = TimeSpan.FromMilliseconds(260),
                    EasingFunction = new BounceEase
                    {
                        Bounces = 1,
                        Bounciness = 2.2,
                        EasingMode = EasingMode.EaseOut
                    }
                });
        };

        return new InlineUIContainer(ball)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
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
                Process.Start("explorer.exe", $"/select,\"{selectedPath}\"");
                KaraokeVocalStatusText.Text = $"Opened location for {System.IO.Path.GetFileName(selectedPath)}.";
                return;
            }

            Process.Start("explorer.exe", $"\"{_karaokeRecordingFolder}\"");
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
            var output = new WaveOutEvent
            {
                DeviceNumber = _selectedOutputDevice?.DeviceNumber ?? -1,
                DesiredLatency = 90
            };
            output.PlaybackStopped += KaraokeRecordingPlaybackStopped;
            output.Init(reader);

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
            SessionPlaybackElement.Source = new Uri(path);
            SessionPlaybackElement.Visibility = Visibility.Visible;
            CameraPlaceholder.Visibility = Visibility.Collapsed;
            SessionPlaybackBar.Visibility = Visibility.Visible;
            ResetSessionPlaybackProgress();
            _sessionPlaybackPath = path;
            _lastSessionRecordingPath = path;
            _startSessionPlaybackFromBeginning = true;
            SessionPlaybackElement.Play();
            _isSessionPlaybackPlaying = true;
            _isSessionPlaybackEnded = false;
            _sessionPlaybackPositionTimer.Start();
            PersistAppState();

            RecordingStatusText.Text = $"Playing {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StopSessionPlayback();
            RecordingStatusText.Text = $"Session playback failed: {ex.Message}";
        }

        UpdateSessionPlaybackTransportControls();
    }

    private void StopSessionPlayback()
    {
        _sessionPlaybackPositionTimer.Stop();
        _isScrubbingSessionPlayback = false;
        _isSessionPlaybackPlaying = false;
        _isSessionPlaybackEnded = false;
        _startSessionPlaybackFromBeginning = false;
        if (SessionPlaybackSeekSlider is not null && SessionPlaybackSeekSlider.IsMouseCaptured)
        {
            SessionPlaybackSeekSlider.ReleaseMouseCapture();
        }

        try
        {
            SessionPlaybackElement.Stop();
            SessionPlaybackElement.Source = null;
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

    private void SessionPlaybackOpened(object sender, RoutedEventArgs e)
    {
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

        SessionPlaybackElement.Pause();
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

        var position = SessionPlaybackElement.Position;
        var duration = GetSessionPlaybackDuration();
        var maxSeconds = Math.Max(1d, duration.TotalSeconds);
        var positionSeconds = Math.Clamp(position.TotalSeconds, 0d, maxSeconds);

        _isUpdatingSessionPlaybackPosition = true;
        try
        {
            SessionPlaybackSeekSlider.Maximum = maxSeconds;
            SessionPlaybackSeekSlider.Value = positionSeconds;
        }
        finally
        {
            _isUpdatingSessionPlaybackPosition = false;
        }

        SessionPlaybackPositionText.Text = FormatDuration(TimeSpan.FromSeconds(positionSeconds));
        SessionPlaybackDurationText.Text = duration > TimeSpan.Zero
            ? FormatDuration(duration)
            : "00:00:00";
        UpdateSessionPlaybackBarControls();
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
        return SessionPlaybackElement.NaturalDuration.HasTimeSpan
            ? SessionPlaybackElement.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
    }

    private bool CanSeekSessionPlayback()
    {
        return !string.IsNullOrWhiteSpace(_sessionPlaybackPath)
            && SessionPlaybackElement.NaturalDuration.HasTimeSpan
            && SessionPlaybackElement.NaturalDuration.TimeSpan > TimeSpan.Zero;
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
        SessionPlaybackElement.Position = clamped;

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
        var path = _sessionPlaybackPath;
        _sessionPlaybackPositionTimer.Stop();
        _isScrubbingSessionPlayback = false;
        _isSessionPlaybackPlaying = false;
        _isSessionPlaybackEnded = true;
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

        EnsureVoiceZones(width, graphTop, graphBottom);
        EnsureGraphGrid(width, graphTop, graphBottom);
        UpdateEqualizerHoverRegion(width, graphTop, graphBottom);

        var livePoints = new PointCollection();
        var rawPoints = new PointCollection();
        var input1Points = new PointCollection();
        var input2Points = new PointCollection();
        var frameCeiling = 0.08d;
        if (_showMicCompare && frame.HasStereoInput)
        {
            for (var i = 0; i < frame.Input1Magnitudes.Length; i++)
            {
                frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.Input1Magnitudes[i]));
            }

            for (var i = 0; i < frame.Input2Magnitudes.Length; i++)
            {
                frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.Input2Magnitudes[i]));
            }
        }
        else
        {
            for (var i = 0; i < frame.Magnitudes.Length; i++)
            {
                frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.Magnitudes[i]));
            }

            for (var i = 0; i < frame.RawMagnitudes.Length; i++)
            {
                frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.RawMagnitudes[i]));
            }
        }

        _visualCeiling = frameCeiling > _visualCeiling
            ? Ease(_visualCeiling, frameCeiling, 0.08d)
            : Ease(_visualCeiling, frameCeiling, 0.015d);
        EnsureRenderBuffers(frame.Magnitudes.Length, frame.RawMagnitudes.Length);
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

        for (var i = 0; i < frame.RawMagnitudes.Length; i++)
        {
            var rawShaped = NormalizeForDisplay(ShapeMagnitude(frame.RawMagnitudes[i]));
            _renderedRawMagnitudes[i] = Ease(_renderedRawMagnitudes[i], rawShaped, analyzerSmoothingCoefficient);
            var x = frame.RawMagnitudes.Length == 1
                ? 0d
                : i / (double)(frame.RawMagnitudes.Length - 1) * width;
            rawPoints.Add(new Point(x, graphBottom - usableHeight * _renderedRawMagnitudes[i]));
        }

        if (_showMicCompare && frame.HasStereoInput)
        {
            var compareLength = Math.Min(frame.Input1Magnitudes.Length, frame.Input2Magnitudes.Length);
            for (var i = 0; i < compareLength; i++)
            {
                var input1Shaped = NormalizeForDisplay(Math.Clamp(ShapeMagnitude(frame.Input1Magnitudes[i]) + _micAnalysisInput1DisplayOffset, 0d, 1d));
                var input2Shaped = NormalizeForDisplay(Math.Clamp(ShapeMagnitude(frame.Input2Magnitudes[i]) + _micAnalysisInput2DisplayOffset, 0d, 1d));
                _renderedInput1Magnitudes[i] = Ease(_renderedInput1Magnitudes[i], input1Shaped, analyzerSmoothingCoefficient);
                _renderedInput2Magnitudes[i] = Ease(_renderedInput2Magnitudes[i], input2Shaped, analyzerSmoothingCoefficient);

                var x = compareLength == 1
                    ? 0d
                    : i / (double)(compareLength - 1) * width;
                input1Points.Add(new Point(x, graphBottom - usableHeight * _renderedInput1Magnitudes[i]));
                input2Points.Add(new Point(x, graphBottom - usableHeight * _renderedInput2Magnitudes[i]));
            }
        }

        _input1Trace.Data = CreateSmoothedGeometry(input1Points);
        _input2Trace.Data = CreateSmoothedGeometry(input2Points);
        _averageTrace.Visibility = _showMicCompare ? Visibility.Collapsed : Visibility.Visible;
        _liveTrace.Visibility = _showMicCompare ? Visibility.Collapsed : Visibility.Visible;
        _input1Trace.Visibility = _showMicCompare && frame.HasStereoInput ? Visibility.Visible : Visibility.Collapsed;
        _input2Trace.Visibility = _showMicCompare && frame.HasStereoInput ? Visibility.Visible : Visibility.Collapsed;
        _liveTrace.Data = CreateSmoothedGeometry(livePoints);
        _averageTrace.Data = CreateSmoothedGeometry(rawPoints);
        PeakText.Text = _showMicCompare && frame.HasStereoInput
            ? $"Mic 1 {frame.Input1PeakLevel:P0}  Mic 2 {frame.Input2PeakLevel:P0}"
            : $"Peak {frame.PeakLevel:P0}";
        UpdateAudioStability(frame);
        UpdateInputCoach(frame.RawPeakLevel);
        UpdateSignalStatus(frame.PeakLevel);
    }

    private double GetAnalyzerSmoothingCoefficient()
    {
        var smoothingPercent = AnalyzerSmoothingSlider?.Value ?? 80d;
        return Math.Clamp(0.36d - smoothingPercent * 0.0031d, 0.05d, 0.36d);
    }

    private double GetAnalyzerBottomInset()
    {
        var faceplateHeight = EqualizerFaceplate?.ActualHeight ?? 0d;
        return Math.Max(270d, faceplateHeight + 38d);
    }

    private void RenderWaveform(SpectrumFrame frame)
    {
        if (!_showWaveform)
        {
            return;
        }

        var width = Math.Max(1d, WaveformCanvas.ActualWidth);
        var height = Math.Max(1d, WaveformCanvas.ActualHeight);
        var topInset = 86d;
        var bottomInset = GetAnalyzerBottomInset();
        var usableHeight = Math.Max(1d, height - topInset - bottomInset);
        var graphTop = topInset;
        var graphBottom = graphTop + usableHeight;
        var centerY = graphTop + usableHeight / 2d;
        var halfHeight = usableHeight * 0.42d;

        EnsureWaveformGrid(width, graphTop, graphBottom, centerY);

        var sampleRate = Math.Max(8000, frame.SampleRate);
        var requestedSamples = Math.Clamp((int)(sampleRate * WaveformTimeSlider.Value / 1000d), 512, sampleRate);
        var snapshotSamples = WaveformTriggerCheckBox.IsChecked == true
            ? Math.Min(sampleRate * MaximumWaveformHistorySeconds, requestedSamples * 2)
            : requestedSamples;
        float[] rawSamples;
        float[] processedSamples;
        lock (_waveformLock)
        {
            rawSamples = CopyLatestWaveformSamples(_rawWaveformHistory, snapshotSamples);
            processedSamples = CopyLatestWaveformSamples(_processedWaveformHistory, snapshotSamples);
        }

        var startIndex = WaveformTriggerCheckBox.IsChecked == true
            ? FindTriggeredStart(processedSamples, requestedSamples)
            : Math.Max(0, processedSamples.Length - requestedSamples);

        _rawWaveTrace.Data = CreatePolylineGeometry(CreateWaveformPoints(rawSamples, startIndex, requestedSamples, width, centerY, halfHeight));
        _processedWaveTrace.Data = CreatePolylineGeometry(CreateWaveformPoints(processedSamples, startIndex, requestedSamples, width, centerY, halfHeight));
    }

    private void RenderRecordingSignalStrip(SpectrumFrame frame)
    {
        RecordingDspStatusText.Text = RecordProcessedAudioCheckBox.IsChecked == true
            ? "DSP active"
            : "Recording natural audio";
        RecordingDspIndicator.Fill = RecordProcessedAudioCheckBox.IsChecked == true
            ? _recordingDspActiveBrush
            : _recordingNaturalAudioBrush;

        var width = Math.Max(1d, RecordingSignalCanvas.ActualWidth);
        var height = Math.Max(1d, RecordingSignalCanvas.ActualHeight);
        var graphTop = 12d;
        var graphBottom = Math.Max(graphTop + 1d, height - 12d);
        var usableHeight = graphBottom - graphTop;

        var frameCeiling = 0.08d;
        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.Magnitudes[i]));
        }

        for (var i = 0; i < frame.RawMagnitudes.Length; i++)
        {
            frameCeiling = Math.Max(frameCeiling, ShapeMagnitude(frame.RawMagnitudes[i]));
        }

        _recordingVisualCeiling = frameCeiling > _recordingVisualCeiling
            ? Ease(_recordingVisualCeiling, frameCeiling, 0.10d)
            : Ease(_recordingVisualCeiling, frameCeiling, 0.025d);

        EnsureRecordingRenderBuffers(frame.Magnitudes.Length, frame.RawMagnitudes.Length);

        var points = new PointCollection();
        var rawPoints = new PointCollection();
        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            var shaped = ShapeMagnitude(frame.Magnitudes[i]);
            var normalized = Math.Clamp(shaped / Math.Max(0.08d, _recordingVisualCeiling) * 0.78d, 0d, 0.92d);
            _renderedRecordingMagnitudes[i] = Ease(_renderedRecordingMagnitudes[i], normalized, 0.14d);

            var x = frame.Magnitudes.Length == 1
                ? 0d
                : i / (double)(frame.Magnitudes.Length - 1) * width;
            points.Add(new Point(x, graphBottom - usableHeight * _renderedRecordingMagnitudes[i]));
        }

        for (var i = 0; i < frame.RawMagnitudes.Length; i++)
        {
            var normalized = Math.Clamp(ShapeMagnitude(frame.RawMagnitudes[i]) / Math.Max(0.08d, _recordingVisualCeiling) * 0.78d, 0d, 0.92d);
            _renderedRecordingRawMagnitudes[i] = Ease(_renderedRecordingRawMagnitudes[i], normalized, 0.14d);
            var x = frame.RawMagnitudes.Length == 1
                ? 0d
                : i / (double)(frame.RawMagnitudes.Length - 1) * width;
            rawPoints.Add(new Point(x, graphBottom - usableHeight * _renderedRecordingRawMagnitudes[i]));
        }

        _recordingSignalTrace.Data = CreateSmoothedGeometry(points);
        _recordingRawSignalTrace.Data = CreateSmoothedGeometry(rawPoints);
        PeakText.Text = $"Peak {frame.PeakLevel:P0}";
    }

    private void AppendWaveformHistory(float[] rawSamples, float[] processedSamples)
    {
        if (rawSamples.Length == 0 && processedSamples.Length == 0)
        {
            return;
        }

        lock (_waveformLock)
        {
            foreach (var sample in rawSamples)
            {
                _rawWaveformHistory.Enqueue(sample);
            }

            foreach (var sample in processedSamples)
            {
                _processedWaveformHistory.Enqueue(sample);
            }

            TrimWaveformHistory(_rawWaveformHistory, _waveformSampleRate);
            TrimWaveformHistory(_processedWaveformHistory, _waveformSampleRate);
        }
    }

    private static void TrimWaveformHistory(Queue<float> samples, int sampleRate)
    {
        var maximumSamples = Math.Max(DefaultWaveformSampleRate, sampleRate) * MaximumWaveformHistorySeconds;
        while (samples.Count > maximumSamples)
        {
            samples.Dequeue();
        }
    }

    private static float[] CopyLatestWaveformSamples(Queue<float> samples, int requestedSamples)
    {
        var copyCount = Math.Clamp(requestedSamples, 0, samples.Count);
        if (copyCount == 0)
        {
            return [];
        }

        var snapshot = new float[copyCount];
        var skipCount = samples.Count - copyCount;
        var index = 0;
        var queueIndex = 0;
        foreach (var sample in samples)
        {
            if (queueIndex++ < skipCount)
            {
                continue;
            }

            snapshot[index++] = sample;
        }

        return snapshot;
    }

    private void EnsureWaveformGrid(double width, double graphTop, double graphBottom, double centerY)
    {
        var neededLines = 14;
        while (_waveformGridLines.Count < neededLines)
        {
            var line = new Line
            {
                Stroke = _waveformGridBrush,
                StrokeThickness = 1,
                Opacity = 0.82
            };
            _waveformGridLines.Add(line);
            WaveformCanvas.Children.Insert(0, line);
        }

        for (var i = 0; i < 7; i++)
        {
            var y = graphTop + (graphBottom - graphTop) * i / 6d;
            var line = _waveformGridLines[i];
            line.X1 = 0;
            line.Y1 = y;
            line.X2 = width;
            line.Y2 = y;
            line.Opacity = Math.Abs(y - centerY) < 1d ? 1d : 0.55d;
            line.StrokeThickness = Math.Abs(y - centerY) < 1d ? 2d : 1d;
            line.Stroke = Math.Abs(y - centerY) < 1d
                ? _waveformCenterGridBrush
                : _waveformGridBrush;
        }

        for (var i = 0; i < 7; i++)
        {
            var x = width * i / 6d;
            var line = _waveformGridLines[7 + i];
            line.X1 = x;
            line.Y1 = graphTop;
            line.X2 = x;
            line.Y2 = graphBottom;
            line.Opacity = 0.55d;
            line.StrokeThickness = 1d;
            line.Stroke = _waveformGridBrush;
        }
    }

    private static int FindTriggeredStart(float[] samples, int requestedSamples)
    {
        if (samples.Length <= requestedSamples)
        {
            return 0;
        }

        var latestStart = Math.Max(0, samples.Length - requestedSamples);
        var searchStart = Math.Max(1, latestStart - requestedSamples);
        var searchEnd = latestStart;
        for (var i = searchEnd; i >= searchStart; i--)
        {
            if (samples[i - 1] < -0.01f && samples[i] >= 0.01f)
            {
                return Math.Min(i, samples.Length - requestedSamples);
            }
        }

        return latestStart;
    }

    private static PointCollection CreateWaveformPoints(float[] samples, int startIndex, int requestedSamples, double width, double centerY, double halfHeight)
    {
        if (samples.Length == 0)
        {
            return new PointCollection();
        }

        var availableSamples = Math.Clamp(samples.Length - startIndex, 0, requestedSamples);
        if (availableSamples <= 0)
        {
            return new PointCollection();
        }

        var stride = Math.Max(1, availableSamples / 1000);
        var pointCount = (availableSamples + stride - 1) / stride;
        var points = new PointCollection(pointCount);
        for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            var sampleIndex = Math.Min(samples.Length - 1, startIndex + pointIndex * stride);
            var sample = Math.Clamp(samples[sampleIndex], -1f, 1f);
            var x = pointCount == 1
                ? 0d
                : pointIndex / (double)(pointCount - 1) * width;
            points.Add(new Point(x, centerY - sample * halfHeight));
        }

        return points;
    }

    private void UpdateInputCoach(double rawPeakLevel)
    {
        _displayInputPeak = rawPeakLevel > _displayInputPeak
            ? Ease(_displayInputPeak, rawPeakLevel, 0.35d)
            : Ease(_displayInputPeak, rawPeakLevel, 0.06d);

        var peakDb = 20d * Math.Log10(Math.Max(0.000001d, _displayInputPeak));
        var meterHostWidth = InputLevelMeter.Parent is FrameworkElement meterHost && meterHost.ActualWidth > 0d
            ? meterHost.ActualWidth
            : 260d;
        InputLevelMeter.Width = Math.Clamp((_displayInputPeak / 0.95d) * meterHostWidth, 0d, meterHostWidth);

        if (peakDb < -36d)
        {
            InputCoachText.Text = $"Input: too quiet ({peakDb:0} dB)";
            InputCoachText.Foreground = _meterTextMutedBrush;
            InputLevelMeter.Fill = _meterMutedBrush;
        }
        else if (peakDb < -12d)
        {
            InputCoachText.Text = $"Input: good ({peakDb:0} dB)";
            InputCoachText.Foreground = _meterGoodBrush;
            InputLevelMeter.Fill = _meterGoodBrush;
        }
        else if (peakDb < -3d)
        {
            InputCoachText.Text = $"Input: hot ({peakDb:0} dB)";
            InputCoachText.Foreground = _meterWarnBrush;
            InputLevelMeter.Fill = _meterWarnBrush;
        }
        else
        {
            InputCoachText.Text = $"Input: clipping risk ({peakDb:0} dB)";
            InputCoachText.Foreground = _meterDangerBrush;
            InputLevelMeter.Fill = _meterDangerBrush;
        }
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
        var gain = DisplayGainSlider.Value;
        return Math.Clamp(magnitude / Math.Max(0.08d, _visualCeiling) * 0.62d * gain / 6d, 0d, 0.88d);
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

    private void EnsureRecordingRenderBuffers(int processedLength, int rawLength)
    {
        if (_renderedRecordingMagnitudes.Length != processedLength)
        {
            _renderedRecordingMagnitudes = new double[processedLength];
        }

        if (_renderedRecordingRawMagnitudes.Length != rawLength)
        {
            _renderedRecordingRawMagnitudes = new double[rawLength];
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
        if (peakLevel < 0.001d)
        {
            _silentFrameCount++;
        }
        else
        {
            _silentFrameCount = 0;
        }

        if (_silentFrameCount > 20)
        {
            StatusText.Text = "Listening, but this input is silent. Try another mic/device or raise input gain.";
        }
        else if (_selectedDevice is not null)
        {
            StatusText.Text = "Listening";
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

    private void EnsureVoiceZones(double width, double graphTop, double graphBottom)
    {
        while (_zoneBands.Count < _voiceZones.Count)
        {
            var band = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb((byte)(18 + _zoneBands.Count % 2 * 16), 88, 116, 134)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 130, 160, 180)),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            _zoneBands.Add(band);
            SpectrumCanvas.Children.Insert(0, band);

            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 8, 14, 20)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 217)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                }
            };
            _zoneLabels.Add(label);
            SpectrumCanvas.Children.Add(label);
        }

        for (var i = 0; i < _voiceZones.Count; i++)
        {
            var zone = _voiceZones[i];
            var left = FrequencyToX(zone.StartFrequencyHz, width);
            var right = FrequencyToX(zone.EndFrequencyHz, width);
            var zoneWidth = Math.Max(24d, right - left);

            _zoneBands[i].Width = zoneWidth;
            _zoneBands[i].Height = graphBottom - graphTop;
            Canvas.SetLeft(_zoneBands[i], left);
            Canvas.SetTop(_zoneBands[i], graphTop);

            if (_zoneLabels[i].Child is TextBlock textBlock)
            {
                textBlock.Text = zone.Name;
                _zoneLabels[i].ToolTip = zone.Description;
            }

            _zoneLabels[i].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelWidth = _zoneLabels[i].DesiredSize.Width;
            Canvas.SetLeft(_zoneLabels[i], left + Math.Max(2d, (zoneWidth - labelWidth) / 2d));
            Canvas.SetTop(_zoneLabels[i], graphTop + 8d);
        }
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
        if (_hoveredEqualizerBand is null || _showWaveform || _showWaveform3D || _showMicCompare)
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
        var clamped = Math.Clamp(frequency, MinimumDisplayFrequency, MaximumDisplayFrequency);
        var position = Math.Log(clamped / MinimumDisplayFrequency) / Math.Log(MaximumDisplayFrequency / MinimumDisplayFrequency);
        return position * width;
    }

    private void FlatPresetClicked(object sender, RoutedEventArgs e)
    {
        ApplyPreset(
            "Flat",
            "Neutral reference. No EQ curve, gentle defaults, useful when you want to hear the mic without a voice style.",
            80, 0, -55, 0, 0, -20, 2, 0, 0, -1,
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    }

    private void PodcastCleanPresetClicked(object sender, RoutedEventArgs e)
    {
        ApplyPreset(
            "Podcast Clean",
            "Cuts rumble and mud, adds presence for clearer speech, and uses moderate compression for steady level.",
            85, 4, -50, 4, 2, -18, 3, 4, 2.5, -1,
            [-4, -3, -2, -1, 0, 0.5, 0, -1, -1.5, -1, 0, 1, 2, 2.5, 2, 1, 0.5, 0, -1, -2]);
    }

    private void WarmRadioPresetClicked(object sender, RoutedEventArgs e)
    {
        LoadWarmRadioPreset();
    }

    private void DeepWarmPresetClicked(object sender, RoutedEventArgs e)
    {
        const string description = "Leans into a deep, warm broadcast tone: strong chest and low-mid body, carved mud, softened bite, and dense compression.";

        ApplyPreset(
            "Deep Warm",
            description,
            58, 4.5, -52, 2.5, 1.5, -22, 4.2, 2.5, 2.5, -1,
            [-6, -4, -1, 1.5, 3.5, 3, 2, 0.5, -1.5, -2, -1, -0.2, 0.6, 1, 0.5, -0.5, -1.5, -2, -2.5, -3]);

        Settings.InputTrimDb = 0;
        Settings.DePopperFrequencyHz = 150;
        Settings.DePopperThresholdDb = -32;
        Settings.ExpanderThresholdDb = -58;
        Settings.ExpanderRatio = 1.45;
        Settings.ExpanderRangeDb = 8;
        Settings.NoiseGateThresholdDb = -52;
        Settings.NoiseGateAttackMs = 8;
        Settings.NoiseGateHoldMs = 130;
        Settings.NoiseGateReleaseMs = 220;
        Settings.NoiseGateRangeDb = 20;
        Settings.NoiseSuppressionSensitivity = 3;
        Settings.EchoReducerSensitivity = 3.5;
        Settings.CompressorAttackMs = 16;
        Settings.CompressorReleaseMs = 190;
        Settings.CompressorKneeDb = 8;
        Settings.DeEsserFrequencyHz = 5600;
        Settings.DeEsserThresholdDb = -34;
        Settings.DeEsserRangeDb = 6;
        Settings.PresenceEnhancerAmountDb = 1.1;
        Settings.PresenceEnhancerFrequencyHz = 2600;
        Settings.PresenceEnhancerWidthHz = 1800;
        Settings.LimiterSoftClipDriveDb = 1;
        Settings.LimiterLookaheadMs = 3;
        Settings.LimiterReleaseMs = 85;

        SetProcessingSliderDefaults(description);
        StatusText.Text = "Deep Warm preset loaded";
    }

    private void LoadWarmRadioPreset()
    {
        ApplyPreset(
            "Warm Radio",
            "Adds body and warmth, gently tames harshness, and uses a slightly stronger compressor for a denser sound.",
            70, 3, -52, 3, 3, -20, 3.5, 3, 3, -1,
            [-5, -3, -1, 0.5, 2, 2.5, 2, 1, 0, -1, -1.5, -1, 0, 1, 1.5, 1, 0, -0.5, -1, -2]);
    }

    private void NoisyRoomPresetClicked(object sender, RoutedEventArgs e)
    {
        ApplyPreset(
            "Noisy Room",
            "Raises the high-pass filter and tightens the gate to fight room noise, with firmer compression.",
            110, 6, -42, 8, 4, -21, 4, 5, 1.5, -1,
            [-8, -6, -4, -3, -2, -1, -1, -1.5, -2, -2, -1, 0, 1.5, 2, 1, 0, -1, -2, -3, -4]);
    }

    private void BrightHeadsetPresetClicked(object sender, RoutedEventArgs e)
    {
        ApplyPreset(
            "Bright Headset",
            "Adds clarity and presence for darker headset mics while trimming low rumble and high fizz.",
            95, 4, -48, 5, 2, -16, 2.5, 6, 1.5, -1,
            [-5, -4, -3, -2, -1.5, -1, -1, -0.5, 0, 0.5, 1, 1.5, 2.5, 3, 2.5, 1.5, 0.5, -0.5, -1.5, -3]);
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
        double[] gains)
    {
        Settings.HighPassEnabled = true;
        Settings.InputTrimDb = 0;
        Settings.HighPassFrequencyHz = highPassFrequencyHz;
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
        Settings.PresenceEnhancerEnabled = true;
        Settings.PresenceEnhancerAmountDb = 2;
        Settings.PresenceEnhancerFrequencyHz = 3000;
        Settings.PresenceEnhancerWidthHz = 2600;
        Settings.MakeupGainDb = makeupGainDb;
        Settings.LimiterEnabled = true;
        Settings.LimiterCeilingDb = limiterCeilingDb;
        Settings.LimiterSoftClipEnabled = true;
        Settings.LimiterSoftClipDriveDb = 1.5;
        Settings.LimiterLookaheadEnabled = true;
        Settings.LimiterLookaheadMs = 3;
        Settings.LimiterReleaseMs = 60;

        for (var i = 0; i < Bands.Count && i < gains.Length; i++)
        {
            Bands[i].IsEnabled = true;
            Bands[i].GainDb = gains[i];
        }

        for (var i = gains.Length; i < Bands.Count; i++)
        {
            Bands[i].IsEnabled = true;
            Bands[i].GainDb = 0;
        }

        SyncEqualizerSettings();
        SetProcessingSliderDefaults(description);
        SetActivePresetButton(name);
        _lastLoadedPresetName = name;
        _lastLoadedPresetIsUserPreset = false;
        StatusText.Text = $"{name} preset loaded";
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
        var typedName = CameraProfileNameTextBox.Text.Trim();
        var selectedName = CameraProfileComboBox.SelectedItem as string ?? string.Empty;
        var name = !string.IsNullOrWhiteSpace(typedName)
            ? typedName
            : selectedName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CameraProfileStatusText.Text = "Type a new profile name or choose a camera profile to update.";
            return;
        }

        try
        {
            Directory.CreateDirectory(CameraProfileFolder);
            var profile = CaptureCameraProfile(name);
            var json = JsonSerializer.Serialize(profile, UserPresetJsonOptions);
            File.WriteAllText(GetCameraProfilePath(name), json);
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
            var path = GetCameraProfilePath(name);
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
            File.WriteAllText(GetUserPresetPath(name), json);
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
                IsSupportedAudioRecordingFile,
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

    private void RefreshAudioRecordingFiles(string? preferredPath = null)
    {
        if (RecordingFilesListBox is null)
        {
            return;
        }

        var selectedPath = preferredPath ?? GetSelectedAudioRecordingPath();
        var items = Directory.Exists(_audioRecordingFolder)
            ? Directory.EnumerateFiles(_audioRecordingFolder)
                .Where(IsSupportedAudioRecordingFile)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new AudioRecordingFileItem(
                    file.FullName,
                    file.Name,
                    $"{file.LastWriteTime:g}    {FormatFileSize(file.Length)}"))
                .ToList()
            : [];

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
                IsSupportedAudioRecordingFile,
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
        var items = Directory.Exists(_karaokeRecordingFolder)
            ? Directory.EnumerateFiles(_karaokeRecordingFolder)
                .Where(IsSupportedAudioRecordingFile)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new AudioRecordingFileItem(
                    file.FullName,
                    file.Name,
                    $"{file.LastWriteTime:g}    {FormatFileSize(file.Length)}"))
                .ToList()
            : [];

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
                IsSessionBrowserPath,
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
        var items = EnumerateSessionRecordings()
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

    private List<SessionRecordingItem> EnumerateSessionRecordings()
    {
        if (!Directory.Exists(_outputFolder))
        {
            return [];
        }

        IEnumerable<string> folders = IsPodcastSessionFolder(_outputFolder)
            ? [_outputFolder]
            : Directory.EnumerateDirectories(_outputFolder)
                .Where(IsPodcastSessionFolder);
        var items = new List<SessionRecordingItem>();
        foreach (var folder in folders)
        {
            foreach (var path in Directory.EnumerateFiles(folder, "video_*.mp4"))
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    continue;
                }

                var metadata = ReadSessionMetadata(folder, file.Name);
                var sessionName = System.IO.Path.GetFileName(folder);
                var displayName = metadata.SetNumber > 0
                    ? $"{sessionName}  set {FormatRecordingSetNumber(metadata.SetNumber)}"
                    : $"{sessionName}  {file.Name}";
                var cameraText = string.IsNullOrWhiteSpace(metadata.Camera) ? "Camera unknown" : metadata.Camera;
                var durationText = string.IsNullOrWhiteSpace(metadata.Duration) ? "Duration unknown" : metadata.Duration;
                var details = $"{file.LastWriteTime:g}    {FormatFileSize(file.Length)}    {durationText}    {cameraText}";
                items.Add(new SessionRecordingItem(
                    file.FullName,
                    folder,
                    displayName,
                    details,
                    file.LastWriteTimeUtc));
            }
        }

        return items;
    }

    private static SessionMetadataSummary ReadSessionMetadata(string folder, string videoFileName)
    {
        var metadataPath = System.IO.Path.Combine(folder, "session.json");
        if (!File.Exists(metadataPath))
        {
            return new SessionMetadataSummary(0, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            var video = TryGetJsonString(root, "video");
            if (!string.IsNullOrWhiteSpace(video)
                && !video.Equals(videoFileName, StringComparison.OrdinalIgnoreCase))
            {
                return new SessionMetadataSummary(0, null, null);
            }

            var setNumber = root.TryGetProperty("setNumber", out var setProperty)
                && setProperty.TryGetInt32(out var number)
                    ? number
                    : 0;
            return new SessionMetadataSummary(
                setNumber,
                TryGetJsonString(root, "camera"),
                TryGetJsonString(root, "duration"));
        }
        catch
        {
            return new SessionMetadataSummary(0, null, null);
        }
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool IsSessionBrowserPath(string path)
    {
        if (Directory.Exists(path))
        {
            return IsPodcastSessionFolder(path);
        }

        return System.IO.Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || System.IO.Path.GetFileName(path).Equals("session.json", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsSupportedAudioRecordingFile(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aiff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wma", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFileSize(long byteCount)
    {
        if (byteCount < 1024)
        {
            return $"{byteCount} B";
        }

        var kib = byteCount / 1024d;
        if (kib < 1024d)
        {
            return $"{kib:0.0} KB";
        }

        return $"{kib / 1024d:0.0} MB";
    }

    private CameraProfile CaptureCameraProfile(string name)
    {
        var camera = CameraComboBox.SelectedItem as CameraDevice;
        var mode = Dx12Camera.ResolveSelectedCameraMode(CameraModeComboBox.SelectedItem);
        return new CameraProfile
        {
            Name = name,
            CameraName = camera?.Name,
            CameraSource = camera?.Source,
            CameraDevicePath = camera?.DevicePath,
            CameraEnabled = _isCameraEnabled,
            ModeLabel = mode.Label,
            ModeWidth = mode.Width,
            ModeHeight = mode.Height,
            ModeFramesPerSecond = mode.FramesPerSecond,
            ModeInputFormat = mode.InputFormat,
            DenoiseEnabled = VideoDenoiseCheckBox?.IsChecked == true,
            DenoiseStrength = VideoDenoiseSlider?.Value ?? _videoDenoiseSliderStrength,
            ColorPolishEnabled = VideoColorPolishCheckBox?.IsChecked == true,
            Exposure = VideoExposureSlider?.Value ?? _pendingVideoColorSettings.Exposure,
            Contrast = VideoContrastSlider?.Value ?? _pendingVideoColorSettings.Contrast,
            Saturation = VideoSaturationSlider?.Value ?? _pendingVideoColorSettings.Saturation,
            Warmth = VideoWarmthSlider?.Value ?? _pendingVideoColorSettings.Warmth
        };
    }

    private CameraProfile? LoadCameraProfile(string name)
    {
        var path = GetCameraProfilePath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CameraProfile>(json, UserPresetJsonOptions);
    }

    private void ApplyCameraProfile(CameraProfile profile)
    {
        var name = string.IsNullOrWhiteSpace(profile.Name) ? "Camera profile" : profile.Name.Trim();
        var camera = FindCameraForProfile(profile);
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

    private CameraDevice? FindCameraForProfile(CameraProfile profile)
    {
        var cameras = CameraComboBox.Items.OfType<CameraDevice>().ToList();
        return Dx12Camera.FindCamera(
            cameras,
            profile.CameraDevicePath,
            profile.CameraSource,
            profile.CameraName);
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
                var name = ReadCameraProfileName(path);
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

    private static string ReadCameraProfileName(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<CameraProfile>(json, UserPresetJsonOptions);
            if (!string.IsNullOrWhiteSpace(profile?.Name))
            {
                return profile.Name.Trim();
            }
        }
        catch
        {
        }

        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static string GetCameraProfilePath(string name)
    {
        return System.IO.Path.Combine(CameraProfileFolder, $"{SanitizePresetFileName(name)}.json");
    }

    private UserVoicePreset CaptureUserPreset(string name)
    {
        var preset = new UserVoicePreset
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(PresetDescriptionText.Text)
                ? $"User preset: {name}"
                : PresetDescriptionText.Text,
            AnalyzerSmoothing = AnalyzerSmoothingSlider.Value,
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
            AnalyzerSmoothingSlider.Value = Math.Clamp(preset.AnalyzerSmoothing, AnalyzerSmoothingSlider.Minimum, AnalyzerSmoothingSlider.Maximum);
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
        StatusText.Text = $"User preset loaded: {name}";
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
        SetProcessingSliderDefault(PresenceEnhancerSlider, Settings.PresenceEnhancerAmountDb);
        SetProcessingSliderDefault(PresenceFrequencySlider, Settings.PresenceEnhancerFrequencyHz);
        SetProcessingSliderDefault(PresenceWidthSlider, Settings.PresenceEnhancerWidthHz);
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

    private sealed class CameraProfile
    {
        public int Version { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        public string? CameraName { get; set; }

        public string? CameraSource { get; set; }

        public string? CameraDevicePath { get; set; }

        public bool CameraEnabled { get; set; }

        public string ModeLabel { get; set; } = CameraVideoMode.Auto.Label;

        public int? ModeWidth { get; set; }

        public int? ModeHeight { get; set; }

        public double? ModeFramesPerSecond { get; set; }

        public string? ModeInputFormat { get; set; }

        public bool DenoiseEnabled { get; set; }

        public double DenoiseStrength { get; set; } = 2d;

        public bool ColorPolishEnabled { get; set; }

        public double Exposure { get; set; }

        public double Contrast { get; set; }

        public double Saturation { get; set; }

        public double Warmth { get; set; }

    }

    private sealed record VoiceZone(string Name, double StartFrequencyHz, double EndFrequencyHz, string Description);

    private sealed record MicZoneDifference(string ZoneName, double DifferenceDb);

    private sealed record InputChannelOption(InputChannelMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RecordingTarget(string SessionFolder, int SetNumber);

    private sealed record AudioRecordingFileItem(string Path, string Name, string Details);

    private sealed record KaraokeTrackItem(string Path, string Name, string Artist, TimeSpan Duration, string DurationText);

    private sealed record KaraokeBrowserFileItem(string Path, string Name, string Details);

    private sealed record KaraokeLyricLineItem(string Text, TimeSpan? Start, TimeSpan? End, List<KaraokeLyricToken> Tokens);

    private sealed record KaraokeTimedLyricLine(TimeSpan Start, string Text, IReadOnlyList<KaraokeLyricToken>? Tokens = null);

    private sealed record KaraokeLyricToken(string Text, TimeSpan? Start, TimeSpan? End, bool IsSingable);

    private sealed record KaraokeRawLyricToken(string Text, bool IsSingable);

    private sealed record KaraokeAudioEnergyWindow(double TimeSeconds, double Energy);

    private sealed record KaraokeAiToolchain(string? PythonPath, string? DemucsPath, string? WhisperXPath);

    private sealed record KaraokeDetectedLyrics(string LyricsText, int LineCount, int SyllableCount);

    private sealed record KaraokeDetectedWord(string Text, TimeSpan Start, TimeSpan End, IReadOnlyList<KaraokeDetectedCharacter>? Characters = null);

    private sealed record KaraokeDetectedCharacter(string Text, TimeSpan? Start, TimeSpan? End);

    private sealed class KaraokeRateSampleProvider : ISampleProvider
    {
        private readonly AudioFileReader _source;
        private readonly int _channels;
        private readonly float[] _currentFrame;
        private readonly float[] _nextFrame;
        private readonly float[] _readBuffer;
        private bool _hasCurrentFrame;
        private bool _hasNextFrame;
        private double _framePosition;
        private double _playbackRate;

        public KaraokeRateSampleProvider(AudioFileReader source, double playbackRate)
        {
            _source = source;
            _channels = Math.Max(1, source.WaveFormat.Channels);
            _currentFrame = new float[_channels];
            _nextFrame = new float[_channels];
            _readBuffer = new float[_channels];
            SetPlaybackRate(playbackRate);
            Reset();
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public void SetPlaybackRate(double playbackRate)
        {
            _playbackRate = Math.Clamp(playbackRate, 0.5d, 2d);
        }

        public void Reset()
        {
            _framePosition = 0d;
            _hasCurrentFrame = ReadFrame(_currentFrame);
            _hasNextFrame = ReadFrame(_nextFrame);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (!_hasCurrentFrame)
            {
                return 0;
            }

            var framesRequested = count / _channels;
            var framesWritten = 0;
            var writeIndex = offset;
            while (framesWritten < framesRequested && _hasCurrentFrame)
            {
                var fraction = _hasNextFrame ? _framePosition : 0d;
                for (var channel = 0; channel < _channels; channel++)
                {
                    var current = _currentFrame[channel];
                    var next = _hasNextFrame ? _nextFrame[channel] : current;
                    buffer[writeIndex++] = (float)(current + (next - current) * fraction);
                }

                framesWritten++;
                _framePosition += _playbackRate;
                while (_framePosition >= 1d && _hasCurrentFrame)
                {
                    _framePosition -= 1d;
                    AdvanceFrame();
                }
            }

            return framesWritten * _channels;
        }

        private void AdvanceFrame()
        {
            if (!_hasNextFrame)
            {
                _hasCurrentFrame = false;
                return;
            }

            Array.Copy(_nextFrame, _currentFrame, _channels);
            _hasCurrentFrame = true;
            _hasNextFrame = ReadFrame(_nextFrame);
        }

        private bool ReadFrame(float[] target)
        {
            var read = _source.Read(_readBuffer, 0, _channels);
            if (read <= 0)
            {
                Array.Clear(target);
                return false;
            }

            for (var i = 0; i < _channels; i++)
            {
                target[i] = i < read ? _readBuffer[i] : 0f;
            }

            return true;
        }
    }

    private sealed class KaraokeVocalReductionSampleProvider(ISampleProvider source, bool enabled) : ISampleProvider
    {
        private volatile bool _enabled = enabled;

        public WaveFormat WaveFormat => source.WaveFormat;

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            if (!_enabled || WaveFormat.Channels < 2)
            {
                return read;
            }

            var channels = WaveFormat.Channels;
            var end = offset + read - channels + 1;
            for (var sample = offset; sample < end; sample += channels)
            {
                var left = buffer[sample];
                var right = buffer[sample + 1];
                var sideLeft = (left - right) * 0.72f;
                var sideRight = (right - left) * 0.72f;

                buffer[sample] = Math.Clamp(sideLeft, -1f, 1f);
                buffer[sample + 1] = Math.Clamp(sideRight, -1f, 1f);
            }

            return read;
        }
    }

    private sealed record SessionRecordingItem(string Path, string SessionFolder, string Name, string Details, DateTime LastWriteTimeUtc)
    {
        public override string ToString() => Name;
    }

    private sealed record SessionMetadataSummary(int SetNumber, string? Camera, string? Duration);

}


