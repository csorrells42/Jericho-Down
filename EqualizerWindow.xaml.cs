using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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
using PodcastWorkbench.Audio;
using PodcastWorkbench.Video;

namespace PodcastWorkbench;

public partial class EqualizerWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const double MinimumDisplayFrequency = 40d;
    private const double MaximumDisplayFrequency = 20000d;
    private const int SampleRate = 44100;
    private const int MaximumWaveformHistorySamples = SampleRate * 3;
    private const double MicAnalysisDurationSeconds = 8d;
    private const double SequentialMicAnalysisPhaseSeconds = 5d;
    private static readonly Regex PodcastSessionFolderRegex = new(@"^Podcast_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex NumberedRecordingFileRegex = new(@"^(?:video|mix|raw_backup)_(?<number>\d{3,})\.(?:mp4|wav)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly MicrophoneSpectrumService _spectrumService = new();
    private readonly FfmpegCameraPreviewService _cameraPreviewService = new();
    private readonly FfmpegCameraModeService _cameraModeService = new();
    private readonly DirectShowCameraControlService _cameraControlService = new();
    private readonly List<Line> _gridLines = [];
    private readonly List<Line> _waveformGridLines = [];
    private readonly object _waveformLock = new();
    private readonly object _micAnalysisLock = new();
    private readonly Queue<float> _rawWaveformHistory = new();
    private readonly Queue<float> _processedWaveformHistory = new();
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
    private readonly Polyline _rawTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(167, 176, 188)),
        StrokeThickness = 1.5,
        Opacity = 0.62,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _liveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 3,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _averageTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(215, 178, 32)),
        StrokeThickness = 2,
        Opacity = 0.9,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _input1Trace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 2,
        Opacity = 0.95,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _input2Trace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
        StrokeThickness = 2,
        Opacity = 0.95,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _rawWaveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(167, 176, 188)),
        StrokeThickness = 1.5,
        Opacity = 0.68,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _processedWaveTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 2.5,
        StrokeLineJoin = PenLineJoin.Round
    };
    private readonly Polyline _recordingSignalTrace = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0, 190, 230)),
        StrokeThickness = 2,
        StrokeLineJoin = PenLineJoin.Round
    };
    private SpectrumFrame? _latestFrame;
    private double[] _renderedMagnitudes = [];
    private double[] _renderedAverageMagnitudes = [];
    private double[] _renderedRecordingMagnitudes = [];
    private double[] _renderedInput1Magnitudes = [];
    private double[] _renderedInput2Magnitudes = [];
    private double _visualCeiling = 0.25d;
    private double _recordingVisualCeiling = 0.25d;
    private double _displayInputPeak;
    private int _silentFrameCount;
    private readonly Dictionary<Slider, double> _processingSliderDefaults = [];
    private readonly Dictionary<Slider, string> _processingSliderBaseToolTips = [];
    private bool _isSnappingProcessingSlider;
    private bool _isSnappingVideoDenoiseSlider;
    private AudioInputDevice? _selectedDevice;
    private AudioOutputDevice? _selectedOutputDevice;
    private InputChannelMode _selectedInputChannelMode = InputChannelMode.MonoSum;
    private string _outputFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Podcast Workbench Sessions");
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
    private ImageSource? _pendingCameraFrame;
    private CancellationTokenSource? _cameraModeLoadCancellation;
    private bool _showWaveform;
    private bool _showMicCompare;
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
        SpectrumCanvas.Children.Add(_rawTrace);
        SpectrumCanvas.Children.Add(_input1Trace);
        SpectrumCanvas.Children.Add(_input2Trace);
        SpectrumCanvas.Children.Add(_averageTrace);
        SpectrumCanvas.Children.Add(_liveTrace);
        WaveformCanvas.Children.Add(_rawWaveTrace);
        WaveformCanvas.Children.Add(_processedWaveTrace);
        RecordingSignalCanvas.Children.Add(_recordingSignalTrace);
        _spectrumService.SpectrumAvailable += SpectrumAvailable;
        _cameraPreviewService.FrameAvailable += CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged += CameraPreviewStatusChanged;
        CompositionTarget.Rendering += CompositionTargetRendering;
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

    public VoiceProcessorSettings Settings { get; } = new();

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
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

        var cameras = DirectShowCameraEnumerator.GetVideoInputDevices();
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
        UpdateRecordingTransportControls();

        LoadWarmRadioPreset();
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

    private void WindowActivated(object? sender, EventArgs e)
    {
        StartSelectedDevice();
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
        CompositionTarget.Rendering -= CompositionTargetRendering;
        _spectrumService.SpectrumAvailable -= SpectrumAvailable;
        _cameraPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged -= CameraPreviewStatusChanged;
        _cameraModeLoadCancellation?.Cancel();
        _cameraModeLoadCancellation?.Dispose();
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

        RecordingStatusText.Text = $"Recording set {FormatRecordingSetNumber(_activeRecordingSetNumber)} started: {_activeRecordingSessionFolder}";
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
            RecordingStatusText.Text = "Recording resumed.";
        }
        else
        {
            _recordingPausedAt = DateTime.UtcNow;
            _isRecordingPaused = true;
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
        _isRecordingSession = false;
        _isRecordingPaused = false;
        _recordingPausedAt = DateTime.MinValue;
        _recordingPausedDuration = TimeSpan.Zero;
        _activeRecordingSessionFolder = null;
        _activeRecordingSetNumber = 0;
        RecordingTimerText.Text = "00:00:00";
        RecordingStatusText.Text = sessionFolder is null
            ? $"Recording stopped at {FormatDuration(elapsed)}."
            : $"Recording stopped at {FormatDuration(elapsed)}. Session folder: {sessionFolder}";

        UpdateOutputFolderText();
        UpdateRecordingTransportControls();
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

        if (!_cameraPreviewService.IsAvailable)
        {
            _isCameraEnabled = false;
            StopCameraPreview("FFmpeg was not found for camera preview");
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

    private void StopCameraPreview(string status)
    {
        _cameraPreviewService.Stop();
        _isCameraFrameUpdateQueued = false;
        _pendingCameraFrame = null;
        CameraPreviewImage.Source = null;
        CameraPreviewImage.Visibility = Visibility.Collapsed;
        CameraPlaceholder.Visibility = Visibility.Visible;
        CameraPreviewStatusText.Text = status;
    }

    private void CameraPreviewFrameAvailable(object? sender, ImageSource frame)
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

            CameraPreviewImage.Source = latestFrame;
            CameraPreviewImage.Visibility = Visibility.Visible;
            CameraPlaceholder.Visibility = Visibility.Collapsed;

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                var status = FormatCameraStatus("Live", camera, GetSelectedCameraMode());
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
            CameraControlStatusText.Text = _pendingVideoDenoiseEnabled
                ? "Video grain reduction is queued for the next camera start. Live preview was left untouched."
                : "Video grain reduction is off. Live preview was left untouched.";
        }
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

    private void SpectrumViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = false;
        _showMicCompare = false;
        SpectrumCanvas.Visibility = Visibility.Visible;
        WaveformCanvas.Visibility = Visibility.Collapsed;
        SpectrumViewButton.FontWeight = FontWeights.SemiBold;
        WaveformViewButton.FontWeight = FontWeights.Normal;
        MicCompareViewButton.FontWeight = FontWeights.Normal;
        NormalSpectrumLegendPanel.Visibility = Visibility.Visible;
        MicCompareLegendPanel.Visibility = Visibility.Collapsed;
        UpdateMicCompareUiState();
    }

    private void WaveformViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = true;
        _showMicCompare = false;
        SpectrumCanvas.Visibility = Visibility.Collapsed;
        WaveformCanvas.Visibility = Visibility.Visible;
        SpectrumViewButton.FontWeight = FontWeights.Normal;
        WaveformViewButton.FontWeight = FontWeights.SemiBold;
        MicCompareViewButton.FontWeight = FontWeights.Normal;
        NormalSpectrumLegendPanel.Visibility = Visibility.Visible;
        MicCompareLegendPanel.Visibility = Visibility.Collapsed;
        UpdateMicCompareUiState();
    }

    private void MicCompareViewClicked(object sender, RoutedEventArgs e)
    {
        _showWaveform = false;
        _showMicCompare = true;
        SpectrumCanvas.Visibility = Visibility.Visible;
        WaveformCanvas.Visibility = Visibility.Collapsed;
        SpectrumViewButton.FontWeight = FontWeights.Normal;
        WaveformViewButton.FontWeight = FontWeights.Normal;
        MicCompareViewButton.FontWeight = FontWeights.SemiBold;
        NormalSpectrumLegendPanel.Visibility = Visibility.Collapsed;
        MicCompareLegendPanel.Visibility = Visibility.Visible;
        UpdateMicCompareUiState();
    }

    private void UpdateMicCompareUiState()
    {
        MicAnalysisPanel.Visibility = _showMicCompare ? Visibility.Visible : Visibility.Collapsed;
        EqBandPanel.IsEnabled = !_showMicCompare;
        EqBandPanel.Opacity = _showMicCompare ? 0.35d : 1d;
        WaveformTimeSlider.IsEnabled = !_showMicCompare;
        WaveformTriggerCheckBox.IsEnabled = !_showMicCompare;
    }

    private void EqSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.ToolTip = "0 dB is the yellow center mark. Drag near it to snap back to flat.";
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

    private void MicrophoneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = MicrophoneComboBox.SelectedItem as AudioInputDevice;
        _spectrumService.Stop();
        StartSelectedDevice();
    }

    private void InputChannelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputChannelComboBox.SelectedItem is InputChannelOption option)
        {
            _selectedInputChannelMode = option.Mode;
        }

        _spectrumService.Stop();
        StartSelectedDevice();
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
            return;
        }

        var enabled = ProcessedOutputCheckBox.IsChecked == true;
        try
        {
            _spectrumService.ConfigureProcessedOutput(enabled, _selectedOutputDevice.DeviceNumber);
            OutputStatusText.Text = enabled
                ? $"Sending processed mic to {_selectedOutputDevice.Name}. In your podcast app, select the matching virtual cable output as the mic."
                : "Output off. Pick a virtual cable input when you want podcast routing.";

            if (enabled && !_spectrumService.IsRunning)
            {
                StartSelectedDevice();
            }
        }
        catch (Exception ex)
        {
            ProcessedOutputCheckBox.IsChecked = false;
            OutputStatusText.Text = $"Output unavailable: {ex.Message}";
        }
    }

    private void StartSelectedDevice()
    {
        if (_selectedDevice is null)
        {
            return;
        }

        try
        {
            _spectrumService.Start(_selectedDevice.DeviceNumber, Settings, _selectedInputChannelMode);
            StatusText.Text = "Listening";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Mic unavailable: {ex.Message}";
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
        _latestFrame = frame;
        CollectMicAnalysisFrame(frame);
        AppendWaveformHistory(frame.RawSamples, frame.ProcessedSamples);
    }

    private void CompositionTargetRendering(object? sender, EventArgs e)
    {
        UpdateRecordingTimer();
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
        var bottomInset = 270d;
        var usableHeight = Math.Max(1d, height - topInset - bottomInset);
        var graphTop = topInset;
        var graphBottom = graphTop + usableHeight;

        EnsureVoiceZones(width, graphTop, graphBottom);
        EnsureGraphGrid(width, graphTop, graphBottom);

        var livePoints = new PointCollection();
        var rawPoints = new PointCollection();
        var averagePoints = new PointCollection();
        var input1Points = new PointCollection();
        var input2Points = new PointCollection();
        var frameCeilingSource = _showMicCompare && frame.HasStereoInput
            ? frame.Input1Magnitudes.Select(ShapeMagnitude).Concat(frame.Input2Magnitudes.Select(ShapeMagnitude))
            : frame.Magnitudes
                .Select((magnitude, index) => ShapeMagnitude(magnitude) * GetVisualGainForBar(index, frame.Magnitudes.Length))
                .Concat(frame.RawMagnitudes.Select(ShapeMagnitude));
        var frameCeiling = Math.Max(0.08d, frameCeilingSource.DefaultIfEmpty(0.08d).Max());
        _visualCeiling = frameCeiling > _visualCeiling
            ? Ease(_visualCeiling, frameCeiling, 0.08d)
            : Ease(_visualCeiling, frameCeiling, 0.015d);
        EnsureRenderBuffers(frame.Magnitudes.Length);
        EnsureInputRenderBuffers(frame.Input1Magnitudes.Length, frame.Input2Magnitudes.Length);

        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            var eqGain = GetVisualGainForBar(i, frame.Magnitudes.Length);
            var shaped = NormalizeForDisplay(ShapeMagnitude(frame.Magnitudes[i]) * eqGain);
            var rawShaped = NormalizeForDisplay(ShapeMagnitude(frame.RawMagnitudes[Math.Min(i, frame.RawMagnitudes.Length - 1)]));
            _renderedMagnitudes[i] = Ease(_renderedMagnitudes[i], shaped, 0.105d);
            _renderedAverageMagnitudes[i] = Ease(_renderedAverageMagnitudes[i], _renderedMagnitudes[i], 0.022d);

            var x = frame.Magnitudes.Length == 1
                ? 0d
                : i / (double)(frame.Magnitudes.Length - 1) * width;
            rawPoints.Add(new Point(x, graphBottom - usableHeight * rawShaped));
            livePoints.Add(new Point(x, graphBottom - usableHeight * _renderedMagnitudes[i]));
            averagePoints.Add(new Point(x, graphBottom - usableHeight * _renderedAverageMagnitudes[i]));
        }

        if (_showMicCompare && frame.HasStereoInput)
        {
            for (var i = 0; i < frame.Input1Magnitudes.Length; i++)
            {
                var input1Shaped = NormalizeForDisplay(Math.Clamp(ShapeMagnitude(frame.Input1Magnitudes[i]) + _micAnalysisInput1DisplayOffset, 0d, 1d));
                var input2Shaped = NormalizeForDisplay(Math.Clamp(ShapeMagnitude(frame.Input2Magnitudes[Math.Min(i, frame.Input2Magnitudes.Length - 1)]) + _micAnalysisInput2DisplayOffset, 0d, 1d));
                _renderedInput1Magnitudes[i] = Ease(_renderedInput1Magnitudes[i], input1Shaped, 0.09d);
                _renderedInput2Magnitudes[i] = Ease(_renderedInput2Magnitudes[i], input2Shaped, 0.09d);

                var x = frame.Input1Magnitudes.Length == 1
                    ? 0d
                    : i / (double)(frame.Input1Magnitudes.Length - 1) * width;
                input1Points.Add(new Point(x, graphBottom - usableHeight * _renderedInput1Magnitudes[i]));
                input2Points.Add(new Point(x, graphBottom - usableHeight * _renderedInput2Magnitudes[i]));
            }
        }

        _rawTrace.Points = SmoothPoints(rawPoints);
        _input1Trace.Points = SmoothPoints(input1Points);
        _input2Trace.Points = SmoothPoints(input2Points);
        _rawTrace.Visibility = _showMicCompare ? Visibility.Collapsed : Visibility.Visible;
        _averageTrace.Visibility = _showMicCompare ? Visibility.Collapsed : Visibility.Visible;
        _liveTrace.Visibility = _showMicCompare ? Visibility.Collapsed : Visibility.Visible;
        _input1Trace.Visibility = _showMicCompare && frame.HasStereoInput ? Visibility.Visible : Visibility.Collapsed;
        _input2Trace.Visibility = _showMicCompare && frame.HasStereoInput ? Visibility.Visible : Visibility.Collapsed;
        _liveTrace.Points = SmoothPoints(livePoints);
        _averageTrace.Points = SmoothPoints(averagePoints);
        PeakText.Text = _showMicCompare && frame.HasStereoInput
            ? $"Mic 1 {frame.Input1PeakLevel:P0}  Mic 2 {frame.Input2PeakLevel:P0}"
            : $"Peak {frame.PeakLevel:P0}";
        UpdateInputCoach(frame.RawPeakLevel);
        UpdateSignalStatus(frame.PeakLevel);
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
        var bottomInset = 270d;
        var usableHeight = Math.Max(1d, height - topInset - bottomInset);
        var graphTop = topInset;
        var graphBottom = graphTop + usableHeight;
        var centerY = graphTop + usableHeight / 2d;
        var halfHeight = usableHeight * 0.42d;

        EnsureWaveformGrid(width, graphTop, graphBottom, centerY);

        float[] rawSamples;
        float[] processedSamples;
        lock (_waveformLock)
        {
            rawSamples = _rawWaveformHistory.ToArray();
            processedSamples = _processedWaveformHistory.ToArray();
        }

        var requestedSamples = Math.Clamp((int)(SampleRate * WaveformTimeSlider.Value / 1000d), 512, SampleRate);
        var startIndex = WaveformTriggerCheckBox.IsChecked == true
            ? FindTriggeredStart(processedSamples, requestedSamples)
            : Math.Max(0, processedSamples.Length - requestedSamples);

        _rawWaveTrace.Points = CreateWaveformPoints(rawSamples, startIndex, requestedSamples, width, centerY, halfHeight);
        _processedWaveTrace.Points = CreateWaveformPoints(processedSamples, startIndex, requestedSamples, width, centerY, halfHeight);
    }

    private void RenderRecordingSignalStrip(SpectrumFrame frame)
    {
        RecordingDspStatusText.Text = RecordProcessedAudioCheckBox.IsChecked == true
            ? "DSP active"
            : "Recording natural audio";
        RecordingDspIndicator.Fill = RecordProcessedAudioCheckBox.IsChecked == true
            ? new SolidColorBrush(Color.FromRgb(66, 215, 125))
            : new SolidColorBrush(Color.FromRgb(215, 178, 32));

        var width = Math.Max(1d, RecordingSignalCanvas.ActualWidth);
        var height = Math.Max(1d, RecordingSignalCanvas.ActualHeight);
        var graphTop = 12d;
        var graphBottom = Math.Max(graphTop + 1d, height - 12d);
        var usableHeight = graphBottom - graphTop;

        if (_renderedRecordingMagnitudes.Length != frame.Magnitudes.Length)
        {
            _renderedRecordingMagnitudes = new double[frame.Magnitudes.Length];
        }

        var frameCeiling = Math.Max(0.08d, frame.Magnitudes
            .Select((magnitude, index) => ShapeMagnitude(magnitude) * GetVisualGainForBar(index, frame.Magnitudes.Length))
            .DefaultIfEmpty(0.08d)
            .Max());
        _recordingVisualCeiling = frameCeiling > _recordingVisualCeiling
            ? Ease(_recordingVisualCeiling, frameCeiling, 0.10d)
            : Ease(_recordingVisualCeiling, frameCeiling, 0.025d);

        var points = new PointCollection();
        for (var i = 0; i < frame.Magnitudes.Length; i++)
        {
            var eqGain = GetVisualGainForBar(i, frame.Magnitudes.Length);
            var shaped = ShapeMagnitude(frame.Magnitudes[i]) * eqGain;
            var normalized = Math.Clamp(shaped / Math.Max(0.08d, _recordingVisualCeiling) * 0.78d, 0d, 0.92d);
            _renderedRecordingMagnitudes[i] = Ease(_renderedRecordingMagnitudes[i], normalized, 0.14d);

            var x = frame.Magnitudes.Length == 1
                ? 0d
                : i / (double)(frame.Magnitudes.Length - 1) * width;
            points.Add(new Point(x, graphBottom - usableHeight * _renderedRecordingMagnitudes[i]));
        }

        _recordingSignalTrace.Points = SmoothPoints(points);
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

            TrimWaveformHistory(_rawWaveformHistory);
            TrimWaveformHistory(_processedWaveformHistory);
        }
    }

    private static void TrimWaveformHistory(Queue<float> samples)
    {
        while (samples.Count > MaximumWaveformHistorySamples)
        {
            samples.Dequeue();
        }
    }

    private void EnsureWaveformGrid(double width, double graphTop, double graphBottom, double centerY)
    {
        var neededLines = 14;
        while (_waveformGridLines.Count < neededLines)
        {
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(36, 45, 54)),
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
                ? new SolidColorBrush(Color.FromRgb(83, 101, 117))
                : new SolidColorBrush(Color.FromRgb(36, 45, 54));
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
            line.Stroke = new SolidColorBrush(Color.FromRgb(36, 45, 54));
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
        var points = new PointCollection();
        if (samples.Length == 0)
        {
            return points;
        }

        var availableSamples = Math.Clamp(samples.Length - startIndex, 0, requestedSamples);
        if (availableSamples <= 0)
        {
            return points;
        }

        var stride = Math.Max(1, availableSamples / 1000);
        var pointCount = (availableSamples + stride - 1) / stride;
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
        InputLevelMeter.Width = Math.Clamp((_displayInputPeak / 0.95d) * 260d, 0d, 260d);

        if (peakDb < -36d)
        {
            InputCoachText.Text = $"Input: too quiet ({peakDb:0} dB)";
            InputCoachText.Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 217));
            InputLevelMeter.Fill = new SolidColorBrush(Color.FromRgb(105, 132, 156));
        }
        else if (peakDb < -12d)
        {
            InputCoachText.Text = $"Input: good ({peakDb:0} dB)";
            InputCoachText.Foreground = new SolidColorBrush(Color.FromRgb(66, 215, 125));
            InputLevelMeter.Fill = new SolidColorBrush(Color.FromRgb(66, 215, 125));
        }
        else if (peakDb < -3d)
        {
            InputCoachText.Text = $"Input: hot ({peakDb:0} dB)";
            InputCoachText.Foreground = new SolidColorBrush(Color.FromRgb(215, 178, 32));
            InputLevelMeter.Fill = new SolidColorBrush(Color.FromRgb(215, 178, 32));
        }
        else
        {
            InputCoachText.Text = $"Input: clipping risk ({peakDb:0} dB)";
            InputCoachText.Foreground = new SolidColorBrush(Color.FromRgb(218, 80, 72));
            InputLevelMeter.Fill = new SolidColorBrush(Color.FromRgb(218, 80, 72));
        }
    }

    private double GetVisualGainForBar(int barIndex, int barCount)
    {
        var frequency = FrequencyForBar(barIndex, barCount);
        var bandIndex = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < Bands.Count; i++)
        {
            var distance = Math.Abs(Math.Log(frequency) - Math.Log(Bands[i].CenterFrequencyHz));
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bandIndex = i;
        }

        return Math.Pow(10d, Bands[bandIndex].GainDb / 20d);
    }

    private static double FrequencyForBar(int barIndex, int barCount)
    {
        var position = barIndex / Math.Max(1d, barCount - 1d);
        return MinimumDisplayFrequency * Math.Pow(MaximumDisplayFrequency / MinimumDisplayFrequency, position);
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

    private void EnsureRenderBuffers(int length)
    {
        if (_renderedMagnitudes.Length == length)
        {
            return;
        }

        _renderedMagnitudes = new double[length];
        _renderedAverageMagnitudes = new double[length];
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

    private static double Ease(double current, double target, double amount)
    {
        return current + (target - current) * amount;
    }

    private static PointCollection SmoothPoints(PointCollection source)
    {
        if (source.Count < 4)
        {
            return source;
        }

        var smoothed = new PointCollection(source.Count * 2);
        smoothed.Add(source[0]);

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
        return smoothed;
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

    private void PodMicSm7bPresetClicked(object sender, RoutedEventArgs e)
    {
        const string description = "Shapes a Rode PodMic toward a smoother SM7B-style broadcast tone: fuller lows, less boxiness, controlled bite, and polished limiting.";

        ApplyPreset(
            "PodMic to SM7B",
            description,
            75, 4, -50, 3.5, 2, -21, 3.8, 3.5, 3, -1,
            [-4, -3, -1.5, 1, 2.5, 2, 0.5, -1.5, -2.5, -2, -1, 0.5, 2, 2.5, 1.5, 0, -1, -1.5, -1, -1]);

        Settings.InputTrimDb = 0;
        Settings.DePopperFrequencyHz = 165;
        Settings.DePopperThresholdDb = -30;
        Settings.ExpanderThresholdDb = -56;
        Settings.ExpanderRatio = 1.6;
        Settings.ExpanderRangeDb = 10;
        Settings.NoiseGateThresholdDb = -50;
        Settings.NoiseGateAttackMs = 6;
        Settings.NoiseGateHoldMs = 110;
        Settings.NoiseGateReleaseMs = 180;
        Settings.NoiseGateRangeDb = 24;
        Settings.NoiseSuppressionSensitivity = 3.5;
        Settings.EchoReducerSensitivity = 4;
        Settings.CompressorAttackMs = 10;
        Settings.CompressorReleaseMs = 160;
        Settings.CompressorKneeDb = 7;
        Settings.DeEsserFrequencyHz = 6100;
        Settings.DeEsserThresholdDb = -36;
        Settings.DeEsserRangeDb = 8;
        Settings.PresenceEnhancerAmountDb = 1.7;
        Settings.PresenceEnhancerFrequencyHz = 3400;
        Settings.PresenceEnhancerWidthHz = 2200;
        Settings.LimiterSoftClipDriveDb = 1.2;
        Settings.LimiterLookaheadMs = 3;
        Settings.LimiterReleaseMs = 75;

        SetProcessingSliderDefaults(description);
        StatusText.Text = "PodMic to SM7B preset loaded";
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
            Bands[i].GainDb = gains[i];
        }

        SetProcessingSliderDefaults(description);
        StatusText.Text = $"{name} preset loaded";
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
        slider.Ticks.Clear();
        slider.Ticks.Add(value);
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

    private sealed record VoiceZone(string Name, double StartFrequencyHz, double EndFrequencyHz, string Description);

    private sealed record MicZoneDifference(string ZoneName, double DifferenceDb);

    private sealed record InputChannelOption(InputChannelMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RecordingTarget(string SessionFolder, int SetNumber);

}


