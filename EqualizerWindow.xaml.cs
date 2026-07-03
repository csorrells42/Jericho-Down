using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
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
    private static readonly TimeSpan AudioRecordingFolderRefreshDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AudioDeviceFormatPollInterval = TimeSpan.FromSeconds(2);
    private static readonly Regex PodcastSessionFolderRegex = new(@"^Podcast_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex NumberedRecordingFileRegex = new(@"^(?:video|mix|raw_backup)_(?<number>\d{3,})\.(?:mp4|wav)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string UserPresetFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PodcastWorkbench",
        "Presets");
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
    private readonly MediaFoundationCameraModeService _cameraModeService = new();
    private readonly DirectShowCameraControlService _cameraControlService = new();
    private readonly DispatcherTimer _audioDeviceFormatTimer = new();
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
    private FileSystemWatcher? _audioRecordingFolderWatcher;
    private int _audioRecordingFolderRefreshQueued;
    private WaveOutEvent? _audioPlaybackOutput;
    private AudioFileReader? _audioPlaybackReader;
    private string? _audioPlaybackPath;
    private bool _isStoppingAudioPlayback;
    private string? _activeRecordingSessionFolder;
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
    private bool _pendingVideoDenoiseEnabled;
    private double _pendingVideoDenoiseStrength = 2d;
    private bool _processedTextureRecordingEnabled;
    private CameraFrame? _pendingCameraFrame;
    private WriteableBitmap? _cameraPreviewBitmap;
    private TextureNativeCameraStream? _textureNativeCameraStream;
    private TextureNativeFrameInfo? _pendingTextureNativeFrameInfo;
    private Direct3D12PreviewHost? _direct3D12PreviewHost;
    private TextureNativeCameraRecordingSession? _textureNativeRecordingSession;
    private TextureNativeRecordingResult? _lastTextureNativeRecordingResult;
    private int _textureNativeFrameUpdateQueued;
    private int _textureNativePreviewRenderQueued;
    private bool _textureNativeFrameLeaseActive;
    private bool _textureNativeBgraPreviewAvailable;
    private TextureNativeFrameLease? _pendingTextureNativePreviewFrame;
    private CancellationTokenSource? _cameraModeLoadCancellation;
    private SpectrumFrame? _pendingSpectrumFrame;
    private int _spectrumFrameUpdateQueued;
    private Waveform3DWindow? _waveform3DWindow;
    private EqualizerBand? _hoveredEqualizerBand;
    private bool _showWaveform;
    private bool _showWaveform3D;
    private bool _showMicCompare;
    private bool _isLeftControlRailCollapsed;
    private bool _isRecordingSession;
    private bool _isRecordingPaused;
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
        _audioDeviceFormatTimer.Interval = AudioDeviceFormatPollInterval;
        _audioDeviceFormatTimer.Tick += AudioDeviceFormatTimerTick;
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
            "Video / Recording",
            "Mic / DSP"
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
        _processedTextureRecordingEnabled = GetDefaultProcessedTextureNativeRecording();
        if (ProcessedTextureRecordingCheckBox is not null)
        {
            ProcessedTextureRecordingCheckBox.IsChecked = _processedTextureRecordingEnabled;
        }

        ApplyDarkTitleBar();
        InputChannelComboBox.ItemsSource = new[]
        {
            new InputChannelOption(InputChannelMode.MonoSum, "Sum L+R"),
            new InputChannelOption(InputChannelMode.Input1Left, "Input 1 L"),
            new InputChannelOption(InputChannelMode.Input2Right, "Input 2 R")
        };
        InputChannelComboBox.SelectedIndex = 0;

        var devices = MicrophoneSpectrumService.GetInputDevices();
        MicrophoneComboBox.ItemsSource = devices;
        if (devices.Count > 0)
        {
            MicrophoneComboBox.SelectedIndex = 0;
        }
        else
        {
            StatusText.Text = "No microphones found";
        }

        var outputDevices = MicrophoneSpectrumService.GetOutputDevices();
        OutputDeviceComboBox.ItemsSource = outputDevices;
        OutputDeviceComboBox.SelectedIndex = 0;
        UserPresetComboBox.ItemsSource = UserPresetNames;
        LoadUserPresetList();

        var cameras = MediaFoundationCameraEnumerator.GetVideoInputDevices();
        if (cameras.Count == 0)
        {
            cameras = DirectShowCameraEnumerator.GetVideoInputDevices();
        }

        CameraComboBox.ItemsSource = cameras;
        if (cameras.Count > 0)
        {
            CameraComboBox.SelectedIndex = 0;
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

        _ = LoadCameraModesAsync();
        ResetCameraControlPanel("Camera controls are available after mode loading. Use Refresh if you want to query the selected camera.");
        UpdateCameraEnabledState();
        UpdateOutputFolderText();
        RecordingFilesListBox.ItemsSource = _audioRecordingFiles;
        UpdateAudioRecordingFolderText();
        UpdateRecordingTransportControls();
        UpdateStandaloneAudioRecordingTransportControls();

        LoadWarmRadioPreset();
        _audioDeviceFormatTimer.Start();
        UpdateAudioFormatRouteText();
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
        _isClosing = true;
        _audioStreamOperationVersion++;
        CompositionTarget.Rendering -= CompositionTargetRendering;
        _audioDeviceFormatTimer.Stop();
        _audioDeviceFormatTimer.Tick -= AudioDeviceFormatTimerTick;
        _spectrumService.SpectrumAvailable -= SpectrumAvailable;
        _spectrumService.StreamStatusChanged -= SpectrumServiceStreamStatusChanged;
        DisposeAudioRecordingFolderWatcher();
        StopAudioPlayback();
        _cameraPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged -= CameraPreviewStatusChanged;
        StopTextureNativeCameraStream();
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
    }

    private void StartAudioRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (_isStandaloneAudioRecording)
        {
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
        UpdateStandaloneAudioRecordingTransportControls();
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

    private void StartRecordingClicked(object sender, RoutedEventArgs e)
    {
        if (_isRecordingSession)
        {
            return;
        }

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
                && _cameraPreviewService.StartRecording(videoPath, GetSelectedCameraMode()));

        RecordingStatusText.Text = textureVideoStarted
            ? $"Recording GPU video set {FormatRecordingSetNumber(_activeRecordingSetNumber)}: {videoPath}"
            : videoStarted
                ? $"Recording video set {FormatRecordingSetNumber(_activeRecordingSetNumber)}: {videoPath}"
            : $"Recording set {FormatRecordingSetNumber(_activeRecordingSetNumber)} started: {_activeRecordingSessionFolder}";
        UpdateRecordingTransportControls();
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
            if (_textureNativeCameraStream?.IsRecording == true)
            {
                _textureNativeCameraStream.ResumeRecording();
            }
            else if (_textureNativeRecordingSession is not null)
            {
                _textureNativeRecordingSession.Resume();
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
            if (_textureNativeCameraStream?.IsRecording == true)
            {
                _textureNativeCameraStream.PauseRecording();
            }
            else if (_textureNativeRecordingSession is not null)
            {
                _textureNativeRecordingSession.Pause();
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
        var textureResult = StopTextureNativeRecording();
        var videoPath = textureResult?.Path ?? _cameraPreviewService.StopRecording();
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

        RecordingStatusText.Text = textureResult is { Success: true }
            ? textureResult.RecordingPipeline.Contains("processed", StringComparison.OrdinalIgnoreCase)
                ? $"Recording stopped at {FormatDuration(elapsed)}. GPU stream saved processed texture bridge video {System.IO.Path.GetFileName(textureResult.Path)} ({textureResult.SamplesWritten} frames)."
                : $"Recording stopped at {FormatDuration(elapsed)}. GPU saved raw texture-native video {System.IO.Path.GetFileName(textureResult.Path)} ({textureResult.SamplesWritten} samples)."
            : textureResult is not null
                ? $"Recording stopped at {FormatDuration(elapsed)}. GPU recording issue: {textureResult.Status}"
                : !string.IsNullOrWhiteSpace(videoPath)
            ? $"Recording stopped at {FormatDuration(elapsed)}. Saved {System.IO.Path.GetFileName(videoPath)}."
            : sessionFolder is null
                ? $"Recording stopped at {FormatDuration(elapsed)}."
                : $"Recording stopped at {FormatDuration(elapsed)}. Session folder: {sessionFolder}";

        UpdateOutputFolderText();
        UpdateRecordingTransportControls();
    }

    private bool TryStartTextureNativeRecording(string? videoPath)
    {
        _lastTextureNativeRecordingResult = null;
        if (_textureNativeCameraStream is not null
            && _isCameraEnabled
            && !string.IsNullOrWhiteSpace(videoPath))
        {
            try
            {
                return _textureNativeCameraStream.StartRecording(
                    videoPath,
                    new TextureNativeRecordingOptions(
                        _processedTextureRecordingEnabled,
                        _pendingVideoDenoiseEnabled,
                        _pendingVideoDenoiseStrength));
            }
            catch (Exception ex)
            {
                RecordingStatusText.Text = $"Shared GPU recording path unavailable: {ex.Message}";
                return false;
            }
        }

        if (!ShouldUseTextureNativeRecording()
            || !_isCameraEnabled
            || string.IsNullOrWhiteSpace(videoPath)
            || CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            return false;
        }

        try
        {
            _textureNativeRecordingSession = TextureNativeCameraRecorder.StartSession(
                camera.Name,
                GetSelectedCameraMode(),
                videoPath,
                new TextureNativeRecordingOptions(
                    _processedTextureRecordingEnabled,
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

    private static bool ShouldUseTextureNativeRecording()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_TEXTURE_NATIVE_RECORDING");
        return IsEnabledEnvironmentValue(value);
    }

    private static bool ShouldUseSharedTextureCameraStream()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_SHARED_TEXTURE_CAMERA");
        return IsEnabledEnvironmentValue(value);
    }

    private static bool GetDefaultProcessedTextureNativeRecording()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_PROCESSED_TEXTURE_RECORDING");
        return IsEnabledEnvironmentValue(value);
    }

    private static bool IsEnabledEnvironmentValue(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private TextureNativeRecordingResult? StopTextureNativeRecording()
    {
        if (_textureNativeCameraStream?.IsRecording == true)
        {
            try
            {
                _lastTextureNativeRecordingResult = _textureNativeCameraStream.StopRecording();
                return _lastTextureNativeRecordingResult;
            }
            catch (Exception ex)
            {
                _lastTextureNativeRecordingResult = new TextureNativeRecordingResult(
                    false,
                    GetActiveRecordingVideoPath() ?? string.Empty,
                    0,
                    0,
                    _textureNativeCameraStream.DeviceMode,
                    _textureNativeCameraStream.MediaSubtype,
                    _textureNativeCameraStream.Width,
                    _textureNativeCameraStream.Height,
                    _textureNativeCameraStream.FramesPerSecond,
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
        UpdateCameraEnabledState();
    }

    private void CameraSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = LoadCameraModesAsync();
        ResetCameraControlPanel("Camera changed. Use Refresh after modes load if you want to query Windows camera controls.");
        UpdateCameraEnabledState();
    }

    private void RefreshCameraControlsClicked(object sender, RoutedEventArgs e)
    {
        LoadCameraControls();
    }

    private void VideoDenoiseChanged(object sender, RoutedEventArgs e)
    {
        UpdateVideoDenoiseSettings(restartPreview: false);
    }

    private void VideoDenoiseChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateVideoDenoiseSettings(restartPreview: false);
    }

    private void ProcessedTextureRecordingChanged(object sender, RoutedEventArgs e)
    {
        _processedTextureRecordingEnabled = ProcessedTextureRecordingCheckBox?.IsChecked == true;
        UpdateProcessedTextureRecordingStatus();
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
            return;
        }

        UpdateCameraIdleStatus();
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
            CameraEnabledToggle.IsEnabled = _cameraAvailable;
            CameraComboBox.IsEnabled = _cameraAvailable;
            CameraModeComboBox.IsEnabled = _cameraAvailable && CameraModeComboBox.Items.Count > 0;

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
                return;
            }

            CameraEnabledToggle.IsChecked = _isCameraEnabled;
            CameraEnabledToggle.Content = _isCameraEnabled ? "Camera On" : "Camera Off";
        }
        finally
        {
            _isUpdatingCameraUi = false;
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
            StopCameraPreview(FormatCameraDisabledStatus());
        }
    }

    private void StartCameraPreview()
    {
        if (CameraComboBox.SelectedItem is not CameraDevice camera)
        {
            StopCameraPreview("Choose a camera source");
            return;
        }

        CameraPlaceholder.Visibility = Visibility.Collapsed;
        CameraPreviewImage.Visibility = Visibility.Visible;

        if (ShouldUseSharedTextureCameraStream())
        {
            if (StartTextureNativeCameraStream(camera, CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto))
            {
                return;
            }

            _isCameraEnabled = false;
            StopCameraPreview($"Could not open shared GPU camera stream for {camera.Name}");
            UpdateCameraEnabledState();
            return;
        }

        StopTextureNativeCameraStream();

        if (!_cameraPreviewService.IsAvailable)
        {
            _isCameraEnabled = false;
            StopCameraPreview("Windows Media Foundation camera preview is unavailable");
            UpdateCameraEnabledState();
            return;
        }

        var mode = CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
        _cameraPreviewService.DenoiseEnabled = _pendingVideoDenoiseEnabled;
        _cameraPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;
        if (_cameraPreviewService.Start(camera.Name, mode))
        {
            CameraPreviewStatusText.Text = FormatCameraStatus("Starting", camera, mode);
            return;
        }

        if (_pendingVideoDenoiseEnabled)
        {
            _cameraPreviewService.DenoiseEnabled = false;
            if (_cameraPreviewService.Start(camera.Name, mode))
            {
                CameraPreviewStatusText.Text = $"{FormatCameraStatus("Starting", camera, mode)} - denoise bypassed because the camera rejected it";
                return;
            }
        }

        _isCameraEnabled = false;
        StopCameraPreview($"Could not open preview for {camera.Name}");
        UpdateCameraEnabledState();
    }

    private bool StartTextureNativeCameraStream(CameraDevice camera, CameraVideoMode mode)
    {
        StopTextureNativeCameraStream();
        _cameraPreviewService.Stop();
        _isCameraFrameUpdateQueued = false;
        _pendingCameraFrame = null;
        _cameraPreviewBitmap = null;
        CameraPreviewImage.Source = null;
        CameraPreviewImage.Visibility = Visibility.Collapsed;
        CameraPlaceholder.Visibility = Visibility.Visible;
        CameraPreviewStatusText.Text = $"{FormatCameraStatus("GPU stream starting", camera, mode)} - waiting for texture frames";

        try
        {
            var stream = new TextureNativeCameraStream(camera.Name, mode);
            _textureNativeCameraStream = stream;
            ShowDirect3D12PreviewHost(stream.DuplicateNativeD3D12Device());
            _textureNativeCameraStream.FrameAvailable += TextureNativeCameraFrameAvailable;
            _textureNativeCameraStream.TextureFrameAvailable += TextureNativeCameraTextureFrameAvailable;
            _textureNativeCameraStream.StatusChanged += TextureNativeCameraStatusChanged;
            return true;
        }
        catch (Exception ex)
        {
            StopTextureNativeCameraStream();
            CameraPreviewStatusText.Text = $"Shared GPU stream unavailable: {ex.Message}";
            return false;
        }
    }

    private void StopCameraPreview(string status)
    {
        _cameraPreviewService.Stop();
        StopTextureNativeCameraStream();
        _isCameraFrameUpdateQueued = false;
        _pendingCameraFrame = null;
        _cameraPreviewBitmap = null;
        CameraPreviewImage.Source = null;
        CameraPreviewImage.Visibility = Visibility.Collapsed;
        CameraPlaceholder.Visibility = Visibility.Visible;
        CameraPreviewStatusText.Text = status;
    }

    private void StopTextureNativeCameraStream()
    {
        var stream = _textureNativeCameraStream;
        if (stream is null)
        {
            HideDirect3D12PreviewHost();
            return;
        }

        _textureNativeCameraStream = null;
        stream.FrameAvailable -= TextureNativeCameraFrameAvailable;
        stream.TextureFrameAvailable -= TextureNativeCameraTextureFrameAvailable;
        stream.StatusChanged -= TextureNativeCameraStatusChanged;
        try
        {
            if (stream.IsRecording)
            {
                _lastTextureNativeRecordingResult = stream.StopRecording();
            }
        }
        finally
        {
            stream.Dispose();
            _pendingTextureNativeFrameInfo = null;
            System.Threading.Interlocked.Exchange(ref _pendingTextureNativePreviewFrame, null)?.Dispose();
            _textureNativeFrameLeaseActive = false;
            _textureNativeBgraPreviewAvailable = false;
            System.Threading.Volatile.Write(ref _textureNativeFrameUpdateQueued, 0);
            System.Threading.Volatile.Write(ref _textureNativePreviewRenderQueued, 0);
            HideDirect3D12PreviewHost();
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
            if (_isCameraEnabled && _textureNativeCameraStream is not null)
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

            UpdateCameraPreviewBitmap(latestFrame);
            CameraPreviewImage.Visibility = Visibility.Visible;
            CameraPlaceholder.Visibility = Visibility.Collapsed;

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                var status = FormatCameraStatus("Live", camera, GetSelectedCameraMode());
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
        if (frame is not null && _isCameraEnabled && _textureNativeCameraStream is not null)
        {
            CameraPreviewImage.Visibility = Visibility.Collapsed;
            CameraPlaceholder.Visibility = Visibility.Visible;
            if (!_textureNativeBgraPreviewAvailable
                && _direct3D12PreviewHost is not null
                && frame.FrameNumber % 4 == 0)
            {
                _direct3D12PreviewHost.RenderProofFrame(frame);
            }

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                CameraPreviewStatusText.Text = FormatTextureNativeCameraStatus("GPU stream live", camera, frame);
            }
        }

        System.Threading.Volatile.Write(ref _textureNativeFrameUpdateQueued, 0);
        if (System.Threading.Volatile.Read(ref _pendingTextureNativeFrameInfo) is not null
            && System.Threading.Interlocked.Exchange(ref _textureNativeFrameUpdateQueued, 1) == 0)
        {
            Dispatcher.BeginInvoke((Action)ProcessPendingTextureNativeFrame, DispatcherPriority.Background);
        }
    }

    private void TextureNativeCameraTextureFrameAvailable(object? sender, TextureNativeFrameLease frame)
    {
        _textureNativeFrameLeaseActive = frame.IsValid;
        var pendingFrame = frame.Duplicate();
        if (pendingFrame is null)
        {
            return;
        }

        _textureNativeBgraPreviewAvailable = true;
        System.Threading.Interlocked.Exchange(
            ref _pendingTextureNativePreviewFrame,
            pendingFrame)?.Dispose();
        if (System.Threading.Interlocked.Exchange(ref _textureNativePreviewRenderQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke((Action)ProcessPendingTextureNativePreviewFrame, DispatcherPriority.Background);
    }

    private void ProcessPendingTextureNativePreviewFrame()
    {
        var frame = System.Threading.Interlocked.Exchange(ref _pendingTextureNativePreviewFrame, null);
        if (frame is not null)
        {
            try
            {
                if (_isCameraEnabled && _textureNativeCameraStream is not null)
                {
                    _direct3D12PreviewHost?.RenderTextureFrame(frame, _pendingVideoDenoiseEnabled, _pendingVideoDenoiseStrength);
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        System.Threading.Volatile.Write(ref _textureNativePreviewRenderQueued, 0);
        if (System.Threading.Volatile.Read(ref _pendingTextureNativePreviewFrame) is not null
            && System.Threading.Interlocked.Exchange(ref _textureNativePreviewRenderQueued, 1) == 0)
        {
            Dispatcher.BeginInvoke((Action)ProcessPendingTextureNativePreviewFrame, DispatcherPriority.Background);
        }
    }

    private void TextureNativeCameraStatusChanged(object? sender, string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isCameraEnabled && _textureNativeCameraStream is not null)
            {
                CameraPreviewStatusText.Text = status;
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

        try
        {
            CameraPreviewStatusText.Text = $"Loading modes: {camera.Name} - selected mode: {FormatCameraMode(GetSelectedCameraMode())}";
            var modes = await _cameraModeService.GetModesAsync(camera.Name, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CameraModeComboBox.ItemsSource = modes;
            CameraModeComboBox.SelectedIndex = 0;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _isLoadingCameraModes = false;
                UpdateCameraEnabledState();
                if (_isCameraEnabled)
                {
                    StartCameraPreview();
                }
                else
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
                    ? $"{FormatCameraStatus("Preview", camera, GetSelectedCameraMode())} - {status}"
                    : status;
            }
        });
    }

    private CameraVideoMode GetSelectedCameraMode()
    {
        return CameraModeComboBox.SelectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
    }

    private void UpdateCameraIdleStatus()
    {
        if (!_cameraAvailable)
        {
            CameraPreviewStatusText.Text = "No camera source found";
            return;
        }

        CameraPreviewStatusText.Text = FormatCameraDisabledStatus();
    }

    private string FormatCameraDisabledStatus()
    {
        return $"Camera disabled - selected mode: {FormatCameraMode(GetSelectedCameraMode())}";
    }

    private static string FormatCameraStatus(string state, CameraDevice camera, CameraVideoMode mode)
    {
        return $"{state}: {camera.Name} at {FormatCameraMode(mode)}";
    }

    private string FormatTextureNativeCameraStatus(string state, CameraDevice camera, TextureNativeFrameInfo frame)
    {
        var textureStatus = _textureNativeFrameLeaseActive ? "texture lease active" : "waiting for texture lease";
        var presenterStatus = _direct3D12PreviewHost?.IsReady == true ? "DX12 presenter active" : "DX12 presenter pending";
        var previewPathStatus = _direct3D12PreviewHost?.PreviewPathDescription ?? "DX12 preview path pending";
        var denoiseStatus = _pendingVideoDenoiseEnabled ? $"DX12 denoise {_pendingVideoDenoiseStrength:0.0}" : "DX12 denoise off";
        var recordingStatus = _processedTextureRecordingEnabled ? "processed recording armed" : "raw recording armed";
        return $"{state}: {camera.Name} at {frame.Width}x{frame.Height} {frame.FramesPerSecond:0.#} fps {frame.MediaSubtype} ({frame.DeviceMode}, {textureStatus}, {presenterStatus}, {previewPathStatus}, {denoiseStatus}, {recordingStatus}, frame {frame.FrameNumber})";
    }

    private static string FormatCameraMode(CameraVideoMode mode)
    {
        return mode.IsAuto ? "Auto mode" : mode.Label;
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
            Text = FormatCameraControlValue(control.Value),
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
            SmallChange = GetCameraNudgeStep(control),
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

        if (UsesCameraNudgeButtons(control))
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

            var rounded = ApplyCameraDefaultMagnet(RoundToCameraStep(e.NewValue, control), control);
            valueText.Text = FormatCameraControlValue(rounded);
            if (SetCameraControl(camera, control, rounded, isAuto: false))
            {
                control.Value = rounded;
                control.IsAuto = false;
                autoCheckBox.IsChecked = false;
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
                SetCameraControl(camera, control, control.Value, isAuto: true);
            }
        };

        autoCheckBox.Unchecked += (_, _) =>
        {
            if (!_isUpdatingCameraControls)
            {
                SetCameraControl(camera, control, control.Value, isAuto: false);
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
                valueText.Text = FormatCameraControlValue(control.DefaultValue);
                _isUpdatingCameraControls = false;
            }
        };

        container.Child = layout;
        return container;
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
        var nextValue = Math.Clamp(control.Value + direction * GetCameraNudgeStep(control), control.Minimum, control.Maximum);
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
        valueText.Text = FormatCameraControlValue(nextValue);
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
        CameraControlStatusText.Text = $"{control.Name}: {(isAuto ? "Auto" : FormatCameraControlValue(value))}";
        return true;
    }

    private void UpdateVideoDenoiseSettings(bool restartPreview)
    {
        if (VideoDenoiseCheckBox is null || VideoDenoiseSlider is null)
        {
            return;
        }

        var strength = Math.Clamp(VideoDenoiseSlider.Value, VideoDenoiseSlider.Minimum, VideoDenoiseSlider.Maximum);
        const double defaultDenoiseStrength = 2d;
        if (!_isSnappingVideoDenoiseSlider
            && Math.Abs(strength - defaultDenoiseStrength) <= 0.25d
            && Math.Abs(strength - defaultDenoiseStrength) > 0.001d)
        {
            _isSnappingVideoDenoiseSlider = true;
            VideoDenoiseSlider.Value = defaultDenoiseStrength;
            _isSnappingVideoDenoiseSlider = false;
            strength = defaultDenoiseStrength;
        }

        VideoDenoiseSlider.Ticks.Clear();
        VideoDenoiseSlider.Ticks.Add(defaultDenoiseStrength);
        _pendingVideoDenoiseEnabled = VideoDenoiseCheckBox.IsChecked == true;
        _pendingVideoDenoiseStrength = strength;
        _cameraPreviewService.DenoiseEnabled = _pendingVideoDenoiseEnabled;
        _cameraPreviewService.DenoiseStrength = _pendingVideoDenoiseStrength;

        if (VideoDenoiseValueText is not null)
        {
            VideoDenoiseValueText.Text = strength.ToString("0.0");
        }

        if (restartPreview && _isCameraEnabled)
        {
            StartCameraPreview();
            return;
        }

        if (_isCameraEnabled)
        {
            if (_textureNativeCameraStream is not null)
            {
                CameraControlStatusText.Text = _pendingVideoDenoiseEnabled
                    ? "DX12 video grain reduction is live on the preview. Texture-native recording still uses the raw camera stream."
                    : "DX12 video grain reduction is off on the preview.";
                return;
            }

            CameraControlStatusText.Text = _pendingVideoDenoiseEnabled
                ? "Video grain reduction is live on the preview and CPU recording path."
                : "Video grain reduction is off on the preview and CPU recording path.";
        }
    }

    private void UpdateProcessedTextureRecordingStatus()
    {
        if (CameraControlStatusText is null)
        {
            return;
        }

        if (_isRecordingSession)
        {
            CameraControlStatusText.Text = _processedTextureRecordingEnabled
                ? "Processed DX12 video recording will apply to the next recording."
                : "Raw texture-native video recording will apply to the next recording.";
            return;
        }

        CameraControlStatusText.Text = _processedTextureRecordingEnabled
            ? "Processed DX12 bridge recording is enabled for shared GPU camera recordings."
            : "Raw texture-native recording is enabled for shared GPU camera recordings.";
    }

    private static int RoundToCameraStep(double value, CameraControlItem control)
    {
        var step = Math.Max(1, control.Step);
        var rounded = control.Minimum + (int)Math.Round((value - control.Minimum) / step) * step;
        return Math.Clamp(rounded, control.Minimum, control.Maximum);
    }

    private static int ApplyCameraDefaultMagnet(int value, CameraControlItem control)
    {
        var snapDistance = Math.Max(control.Step * 1.25d, (control.Maximum - control.Minimum) * 0.025d);
        return Math.Abs(value - control.DefaultValue) <= snapDistance
            ? control.DefaultValue
            : value;
    }

    private static int GetCameraNudgeStep(CameraControlItem control)
    {
        return Math.Max(1, control.Step);
    }

    private static bool UsesCameraNudgeButtons(CameraControlItem control)
    {
        return control.Kind == CameraControlKind.Camera
            && (control.PropertyId == 0 || control.PropertyId == 1 || control.PropertyId == 2);
    }

    private static string FormatCameraControlValue(int value)
    {
        return value.ToString("0");
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
                mode = GetSelectedCameraMode().Label,
                denoiseEnabled = _pendingVideoDenoiseEnabled,
                denoiseStrength = _pendingVideoDenoiseStrength,
                video = string.IsNullOrWhiteSpace(videoPath) ? null : System.IO.Path.GetFileName(videoPath),
                engine = textureResult is not null
                    ? _textureNativeCameraStream is not null
                        ? "Windows Media Foundation shared texture-native GPU stream"
                        : "Windows Media Foundation texture-native GPU samples"
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
                previewDenoiseApplied = _pendingVideoDenoiseEnabled,
                previewDenoiseStrength = _pendingVideoDenoiseStrength,
                recordingPipeline = textureResult.RecordingPipeline,
                recordingDenoiseApplied = textureResult.RecordingDenoiseApplied,
                recordingMatchesPreviewDenoise = textureResult.RecordingMatchesPreviewDenoise,
                note = textureResult.RecordingPipeline.Contains("processed", StringComparison.OrdinalIgnoreCase)
                    ? "Texture-native recording matched the preview denoise setting through the processed bridge."
                    : _pendingVideoDenoiseEnabled
                        ? "DX12 denoise was visible in preview only; the saved texture-native video is raw camera output."
                        : "DX12 preview denoise was off; the saved texture-native video is raw camera output."
            };
        }

        return new
        {
            previewPipeline = "Media Foundation CPU preview",
            previewDenoiseApplied = _pendingVideoDenoiseEnabled,
            previewDenoiseStrength = _pendingVideoDenoiseStrength,
            recordingPipeline = "Media Foundation CPU frame writer",
            recordingDenoiseApplied = _pendingVideoDenoiseEnabled,
            recordingMatchesPreviewDenoise = true,
            note = _pendingVideoDenoiseEnabled
                ? "CPU preview denoise was applied before recording frames were written."
                : "CPU preview denoise was off."
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
    }

    private async void InputChannelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputChannelComboBox.SelectedItem is InputChannelOption option)
        {
            _selectedInputChannelMode = option.Mode;
        }

        await RestartSelectedAudioStreamAsync("Listening");
    }

    private void OutputDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedOutputDevice = OutputDeviceComboBox.SelectedItem as AudioOutputDevice;
        UpdateOutputRouting();
    }

    private void ProcessedOutputChanged(object sender, RoutedEventArgs e)
    {
        UpdateOutputRouting();
    }

    private void UpdateOutputRouting()
    {
        if (_selectedOutputDevice is null || ProcessedOutputCheckBox is null || OutputStatusText is null)
        {
            UpdateAudioFormatRouteText();
            return;
        }

        var enabled = ProcessedOutputCheckBox.IsChecked == true;
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
            UpdateAudioFormatRouteText();
        }
        catch (Exception ex)
        {
            ProcessedOutputCheckBox.IsChecked = false;
            OutputStatusText.Text = $"Output unavailable: {ex.Message}";
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
        var routed = ProcessedOutputCheckBox?.IsChecked == true && _spectrumService.IsProcessedOutputEnabled;
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

        AudioRecordButton.IsEnabled = !_isStandaloneAudioRecording;
        AudioPauseButton.IsEnabled = _isStandaloneAudioRecording;
        AudioPauseButton.Content = _isStandaloneAudioRecordingPaused ? "Resume" : "Pause";
        AudioStopButton.IsEnabled = _isStandaloneAudioRecording;
        AudioPlayFileButton.IsEnabled = !_isStandaloneAudioRecording;
        AudioPlayFileButton.Content = _audioPlaybackOutput is null ? "Play File" : "Stop Play";
        AudioOpenLocationButton.IsEnabled = !_isStandaloneAudioRecording;
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
        UpdateStandaloneAudioRecordingTransportControls();
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
        AudioStabilityText.Text = $"{label}: frame {_displayedAudioFrameIntervalMs:0.0} ms ({1000d / Math.Max(0.001d, _displayedAudioFrameIntervalMs):0}/s), DSP avg {_displayedAudioProcessingMs:0.0} ms, load {_audioStabilityScore:P0}";
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
        StatusText.Text = $"{name} preset loaded";
    }

    private void UserPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
            Directory.CreateDirectory(_audioRecordingFolder);
            var watcher = new FileSystemWatcher(_audioRecordingFolder)
            {
                Filter = "*.*",
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.CreationTime
            };
            watcher.Created += AudioRecordingFolderChanged;
            watcher.Deleted += AudioRecordingFolderChanged;
            watcher.Renamed += AudioRecordingFolderRenamed;
            watcher.EnableRaisingEvents = true;
            _audioRecordingFolderWatcher = watcher;
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
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= AudioRecordingFolderChanged;
            watcher.Deleted -= AudioRecordingFolderChanged;
            watcher.Renamed -= AudioRecordingFolderRenamed;
            watcher.Dispose();
        }
        catch
        {
        }
    }

    private void AudioRecordingFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (IsSupportedAudioRecordingFile(e.FullPath))
        {
            QueueAudioRecordingFilesRefresh();
        }
    }

    private void AudioRecordingFolderRenamed(object sender, RenamedEventArgs e)
    {
        if (IsSupportedAudioRecordingFile(e.FullPath) || IsSupportedAudioRecordingFile(e.OldFullPath))
        {
            QueueAudioRecordingFilesRefresh();
        }
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

    private string? GetSelectedAudioRecordingPath()
    {
        return RecordingFilesListBox?.SelectedItem is AudioRecordingFileItem item
            ? item.Path
            : null;
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
        StatusText.Text = $"User preset loaded: {name}";
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

    private sealed record VoiceZone(string Name, double StartFrequencyHz, double EndFrequencyHz, string Description);

    private sealed record MicZoneDifference(string ZoneName, double DifferenceDb);

    private sealed record InputChannelOption(InputChannelMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RecordingTarget(string SessionFolder, int SetNumber);

    private sealed record AudioRecordingFileItem(string Path, string Name, string Details);

}


