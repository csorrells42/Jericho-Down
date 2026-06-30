using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;
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
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;
using VoiceWorkbench.Audio;
using VoiceWorkbench.Modbus;
using VoiceWorkbench.Mqtt;
using VoiceWorkbench.Network;
using VoiceWorkbench.Serial;
using VoiceWorkbench.Video;
using VoiceWorkbench.Wifi;

namespace VoiceWorkbench;

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
    private readonly WindowsWifiScanner _wifiScanner = new();
    private readonly ObservableCollection<WifiNetwork> _wifiNetworks = [];
    private readonly Dictionary<string, (WifiNetwork Network, DateTime LastSeen)> _wifiNetworkCache = [];
    private readonly ObservableCollection<IpScanDevice> _ipScanDevices = [];
    private readonly ObservableCollection<PortScanResult> _portScanResults = [];
    private readonly ObservableCollection<SerialLogEntry> _serialLogEntries = [];
    private readonly ObservableCollection<ModbusValueRow> _modbusValues = [];
    private readonly ObservableCollection<MqttLogEntry> _mqttLogEntries = [];
    private readonly ObservableCollection<SecurityCameraEvent> _securityWatchEvents = [];
    private readonly ModbusClientService _modbusClient = new();
    private readonly DispatcherTimer _wifiRefreshTimer = new();
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
    private double _displayGainReductionDb;
    private double _displayGateOpenness = 1d;
    private int _gateClosedHoldFrames;
    private int _silentFrameCount;
    private readonly Dictionary<Slider, double> _processingSliderDefaults = [];
    private readonly Dictionary<Slider, string> _processingSliderBaseToolTips = [];
    private bool _isSnappingProcessingSlider;
    private bool _isSnappingVideoDenoiseSlider;
    private AudioInputDevice? _selectedDevice;
    private AudioOutputDevice? _selectedOutputDevice;
    private InputChannelMode _selectedInputChannelMode = InputChannelMode.MonoSum;
    private ProcessingSnapshot? _snapshotA;
    private ProcessingSnapshot? _snapshotB;
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
    private byte[]? _securityPreviousSample;
    private DateTime _securityLastAnalysisAt = DateTime.MinValue;
    private DateTime _securityWatchLastEventAt = DateTime.MinValue;
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
    private bool _isWifiScanRunning;
    private CancellationTokenSource? _wifiScanCancellation;
    private bool _isIpScanRunning;
    private CancellationTokenSource? _ipScanCancellation;
    private bool _isPortScanRunning;
    private CancellationTokenSource? _portScanCancellation;
    private CancellationTokenSource? _modbusCancellation;
    private CancellationTokenSource? _sqlCancellation;
    private SerialPort? _serialPort;
    private SqlConnection? _sqlConnection;
    private IMqttClient? _mqttClient;
    private MqttServer? _mqttServer;

    public EqualizerWindow()
    {
        InitializeComponent();
        OrderMainTabs();
        DataContext = Settings;
        WifiNetworkGrid.ItemsSource = _wifiNetworks;
        IpScanResultsGrid.ItemsSource = _ipScanDevices;
        PortScanResultsGrid.ItemsSource = _portScanResults;
        SerialLogGrid.ItemsSource = _serialLogEntries;
        ModbusResultsGrid.ItemsSource = _modbusValues;
        MqttLogGrid.ItemsSource = _mqttLogEntries;
        SecurityWatchEventGrid.ItemsSource = _securityWatchEvents;
        _wifiRefreshTimer.Interval = TimeSpan.FromSeconds(15);
        _wifiRefreshTimer.Tick += WifiRefreshTimerTick;
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
        _spectrumService.TestClipStateChanged += TestClipStateChanged;
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
        InitializeSerialControls();
        InitializeModbusControls();
        InitializeMqttControls();

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

        SetProcessingSliderDefaults(
            "Choose a preset to load a starting point. Yellow ticks show that preset's home settings.",
            Settings.HighPassFrequencyHz,
            Settings.DePopperAmountDb,
            Settings.NoiseGateThresholdDb,
            Settings.NoiseSuppressionAmountDb,
            Settings.EchoReducerAmountDb,
            Settings.CompressorThresholdDb,
            Settings.CompressorRatio,
            Settings.DeEsserAmountDb,
            Settings.MakeupGainDb,
            Settings.LimiterCeilingDb);
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
        if (!_spectrumService.IsPlayingTestClip)
        {
            StartSelectedDevice();
        }
    }

    private void WindowDeactivated(object? sender, EventArgs e)
    {
        if (_spectrumService.IsProcessedOutputEnabled)
        {
            StatusText.Text = "Output routing active";
            return;
        }

        _spectrumService.StopTestClipRecording();
        _spectrumService.Stop();
        StatusText.Text = "Paused";
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        CompositionTarget.Rendering -= CompositionTargetRendering;
        _spectrumService.SpectrumAvailable -= SpectrumAvailable;
        _spectrumService.TestClipStateChanged -= TestClipStateChanged;
        _cameraPreviewService.FrameAvailable -= CameraPreviewFrameAvailable;
        _cameraPreviewService.StatusChanged -= CameraPreviewStatusChanged;
        _cameraModeLoadCancellation?.Cancel();
        _cameraModeLoadCancellation?.Dispose();
        _wifiRefreshTimer.Stop();
        _wifiRefreshTimer.Tick -= WifiRefreshTimerTick;
        _wifiScanCancellation?.Cancel();
        _wifiScanCancellation?.Dispose();
        _ipScanCancellation?.Cancel();
        _ipScanCancellation?.Dispose();
        _portScanCancellation?.Cancel();
        _portScanCancellation?.Dispose();
        _modbusCancellation?.Cancel();
        _modbusCancellation?.Dispose();
        _sqlCancellation?.Cancel();
        _sqlCancellation?.Dispose();
        _cameraPreviewService.Dispose();
        _spectrumService.Dispose();
        CloseSerialPort();
        DisconnectSql();
        DisconnectMqttForShutdown();
        StopMqttBrokerForShutdown();
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

    private void RefreshWifiClicked(object sender, RoutedEventArgs e)
    {
        _ = RefreshWifiAsync();
    }

    private void AutoRefreshWifiChanged(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshWifiCheckBox.IsChecked == true)
        {
            _wifiRefreshTimer.Start();
            _ = RefreshWifiAsync();
            return;
        }

        _wifiRefreshTimer.Stop();
    }

    private void WifiRefreshTimerTick(object? sender, EventArgs e)
    {
        _ = RefreshWifiAsync();
    }

    private async Task RefreshWifiAsync()
    {
        if (_isWifiScanRunning)
        {
            return;
        }

        _wifiScanCancellation?.Cancel();
        _wifiScanCancellation?.Dispose();
        _wifiScanCancellation = new CancellationTokenSource();
        var cancellationToken = _wifiScanCancellation.Token;

        _isWifiScanRunning = true;
        RefreshWifiButton.IsEnabled = false;
        WifiStatusText.Text = "Scanning Wi-Fi...";

        try
        {
            var result = await _wifiScanner.ScanAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            UpdateWifiNetworkCache(result.Networks, DateTime.Now);

            UpdateSelectedWifiNetworkDetails();
            WifiStatusText.Text = $"{FormatWifiScanStatus(result.Status)} Last scan: {DateTime.Now:h:mm:ss tt}";
            DrawWifiChannelGraph();
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshWifiButton.IsEnabled = true;
                _isWifiScanRunning = false;
            }
        }
    }

    private void UpdateWifiNetworkCache(IReadOnlyList<WifiNetwork> scannedNetworks, DateTime now)
    {
        foreach (var network in scannedNetworks)
        {
            var key = GetWifiCacheKey(network);
            _wifiNetworkCache[key] = (CopyWifiNetwork(network, now, isCached: false), now);
        }

        var staleCutoff = now - TimeSpan.FromMinutes(5);
        foreach (var key in _wifiNetworkCache.Where(item => item.Value.LastSeen < staleCutoff).Select(item => item.Key).ToList())
        {
            _wifiNetworkCache.Remove(key);
        }

        _wifiNetworks.Clear();
        foreach (var cached in _wifiNetworkCache.Values
            .Select(item => CopyWifiNetwork(item.Network, item.LastSeen, isCached: item.LastSeen < now - TimeSpan.FromSeconds(2)))
            .OrderBy(network => IsTwoFourGhz(network) ? 0 : 1)
            .ThenBy(network => network.Channel)
            .ThenByDescending(network => network.SignalPercent)
            .ThenBy(network => network.Ssid))
        {
            _wifiNetworks.Add(cached);
        }
    }

    private static string GetWifiCacheKey(WifiNetwork network)
    {
        if (!string.IsNullOrWhiteSpace(network.Bssid))
        {
            return network.Bssid;
        }

        return $"{network.Ssid}|{network.Band}|{network.Channel}";
    }

    private static WifiNetwork CopyWifiNetwork(WifiNetwork network, DateTime lastSeen, bool isCached)
    {
        return new WifiNetwork
        {
            Ssid = network.Ssid,
            Bssid = network.Bssid,
            SignalPercent = network.SignalPercent,
            Band = network.Band,
            Channel = network.Channel,
            RadioType = network.RadioType,
            Authentication = network.Authentication,
            Encryption = network.Encryption,
            LastSeen = lastSeen,
            IsCached = isCached
        };
    }

    private void WifiChannelCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawWifiChannelGraph();
    }

    private void StartIpScanClicked(object sender, RoutedEventArgs e)
    {
        _ = StartIpScanAsync();
    }

    private void CancelIpScanClicked(object sender, RoutedEventArgs e)
    {
        _ipScanCancellation?.Cancel();
        IpScanStatusText.Text = "Canceling IP scan...";
    }

    private void IpScanResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePortScanTarget();
    }

    private void ScanCommonPortsClicked(object sender, RoutedEventArgs e)
    {
        _ = StartPortScanAsync(GetCommonTcpPorts());
    }

    private void ScanPortRangeClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadPortRange(out var startPort, out var endPort, out var error))
        {
            PortScanTargetText.Text = error;
            return;
        }

        var count = endPort - startPort + 1;
        if (count > 2048)
        {
            PortScanTargetText.Text = $"That range contains {count:N0} ports. Please scan 2,048 or fewer at once.";
            return;
        }

        _ = StartPortScanAsync(Enumerable.Range(startPort, count).ToArray());
    }

    private void CancelPortScanClicked(object sender, RoutedEventArgs e)
    {
        _portScanCancellation?.Cancel();
        PortScanTargetText.Text = "Canceling port scan...";
    }

    private void RefreshSerialPortsClicked(object sender, RoutedEventArgs e)
    {
        RefreshSerialPorts();
    }

    private void RefreshModbusSerialPortsClicked(object sender, RoutedEventArgs e)
    {
        RefreshModbusSerialPorts();
    }

    private void ModbusModeChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateModbusModeControls();
    }

    private void ModbusOperationChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateModbusOperationControls();
    }

    private void ExecuteModbusClicked(object sender, RoutedEventArgs e)
    {
        _ = ExecuteModbusAsync();
    }

    private void CancelModbusClicked(object sender, RoutedEventArgs e)
    {
        _modbusCancellation?.Cancel();
        ModbusStatusText.Text = "Canceling Modbus request...";
    }

    private void SerialConnectClicked(object sender, RoutedEventArgs e)
    {
        ConnectSerialPort();
    }

    private void SerialDisconnectClicked(object sender, RoutedEventArgs e)
    {
        CloseSerialPort();
    }

    private void SerialSendClicked(object sender, RoutedEventArgs e)
    {
        SendSerialText();
    }

    private void ClearSerialLogClicked(object sender, RoutedEventArgs e)
    {
        _serialLogEntries.Clear();
    }

    private void MqttConnectClicked(object sender, RoutedEventArgs e)
    {
        _ = ConnectMqttAsync();
    }

    private void SqlConnectClicked(object sender, RoutedEventArgs e)
    {
        _ = ConnectSqlAsync();
    }

    private void SqlDisconnectClicked(object sender, RoutedEventArgs e)
    {
        DisconnectSql();
    }

    private void SqlExecuteClicked(object sender, RoutedEventArgs e)
    {
        _ = ExecuteSqlAsync();
    }

    private void SqlCancelClicked(object sender, RoutedEventArgs e)
    {
        _sqlCancellation?.Cancel();
        SqlStatusText.Text = "Canceling SQL query...";
    }

    private void SqlAuthModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateSqlAuthControls();
    }

    private void StartMqttBrokerClicked(object sender, RoutedEventArgs e)
    {
        _ = StartMqttBrokerAsync();
    }

    private void StopMqttBrokerClicked(object sender, RoutedEventArgs e)
    {
        _ = StopMqttBrokerAsync();
    }

    private void UseLocalMqttBrokerClicked(object sender, RoutedEventArgs e)
    {
        MqttBrokerTextBox.Text = "localhost";
        MqttPortTextBox.Text = MqttBrokerPortTextBox.Text.Trim();
        MqttTlsCheckBox.IsChecked = false;
        MqttStatusText.Text = "MQTT client settings pointed at the built-in broker.";
    }

    private void MqttDisconnectClicked(object sender, RoutedEventArgs e)
    {
        _ = DisconnectMqttAsync();
    }

    private void MqttSubscribeClicked(object sender, RoutedEventArgs e)
    {
        _ = SubscribeMqttAsync();
    }

    private void MqttPublishClicked(object sender, RoutedEventArgs e)
    {
        _ = PublishMqttAsync();
    }

    private void ClearMqttLogClicked(object sender, RoutedEventArgs e)
    {
        _mqttLogEntries.Clear();
    }

    private async Task StartIpScanAsync()
    {
        if (_isIpScanRunning)
        {
            return;
        }

        if (!TryReadIpRange(out var startAddress, out var endAddress, out var error))
        {
            IpScanStatusText.Text = error;
            return;
        }

        if (endAddress < startAddress)
        {
            IpScanStatusText.Text = "End address must be greater than or equal to the start address.";
            return;
        }

        var addressCount = endAddress - startAddress + 1UL;
        if (addressCount > 4096)
        {
            IpScanStatusText.Text = $"That range contains {addressCount:N0} addresses. Please scan 4,096 or fewer at once.";
            return;
        }

        _ipScanCancellation?.Cancel();
        _ipScanCancellation?.Dispose();
        _ipScanCancellation = new CancellationTokenSource();
        var cancellationToken = _ipScanCancellation.Token;
        _ipScanDevices.Clear();
        _isIpScanRunning = true;
        StartIpScanButton.IsEnabled = false;
        CancelIpScanButton.IsEnabled = true;
        IpScanStatusText.Text = $"Scanning {addressCount:N0} address{(addressCount == 1 ? "" : "es")}...";

        var scanned = 0;
        var alive = 0;
        using var gate = new SemaphoreSlim(64);
        var tasks = new List<Task>();
        for (var address = startAddress; address <= endAddress; address++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await gate.WaitAsync(cancellationToken);
            var capturedAddress = address;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await ProbeIpAddressAsync(capturedAddress, cancellationToken);
                    var scannedCount = Interlocked.Increment(ref scanned);
                    if (result is not null)
                    {
                        Interlocked.Increment(ref alive);
                        await Dispatcher.BeginInvoke(() => _ipScanDevices.Add(result));
                    }

                    if (scannedCount % 16 == 0 || scannedCount == (int)addressCount)
                    {
                        await Dispatcher.BeginInvoke(() =>
                        {
                            IpScanStatusText.Text = $"Scanned {scannedCount:N0}/{addressCount:N0}. Found {alive:N0} responding device{(alive == 1 ? "" : "s")}.";
                        });
                    }
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
            IpScanStatusText.Text = cancellationToken.IsCancellationRequested
                ? $"Scan canceled. Found {alive:N0} responding device{(alive == 1 ? "" : "s")}."
                : $"Scan complete. Found {alive:N0} responding device{(alive == 1 ? "" : "s")}.";
        }
        catch (OperationCanceledException)
        {
            IpScanStatusText.Text = $"Scan canceled. Found {alive:N0} responding device{(alive == 1 ? "" : "s")}.";
        }
        finally
        {
            StartIpScanButton.IsEnabled = true;
            CancelIpScanButton.IsEnabled = false;
            _isIpScanRunning = false;
        }
    }

    private async Task<IpScanDevice?> ProbeIpAddressAsync(uint address, CancellationToken cancellationToken)
    {
        var ipAddress = UInt32ToIpAddress(address);
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 450).WaitAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
            if (reply.Status != IPStatus.Success)
            {
                return null;
            }

            var hostName = await TryGetHostNameAsync(ipAddress, cancellationToken);
            var ttl = reply.Options?.Ttl;
            var osGuess = GuessOperatingSystemFromTtl(ttl);
            var macAddress = await TryGetMacAddressAsync(ipAddress, cancellationToken);
            return new IpScanDevice
            {
                Address = ipAddress.ToString(),
                HostName = hostName,
                Status = "Alive",
                LatencyMs = reply.RoundtripTime,
                Ttl = ttl,
                MacAddress = macAddress,
                OsGuess = osGuess,
                Notes = BuildIpScanNotes(ttl, macAddress)
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task StartPortScanAsync(IReadOnlyList<int> ports)
    {
        if (_isPortScanRunning)
        {
            return;
        }

        if (IpScanResultsGrid.SelectedItem is not IpScanDevice device || !IPAddress.TryParse(device.Address, out var ipAddress))
        {
            PortScanTargetText.Text = "Select a discovered device first.";
            return;
        }

        _portScanCancellation?.Cancel();
        _portScanCancellation?.Dispose();
        _portScanCancellation = new CancellationTokenSource();
        var cancellationToken = _portScanCancellation.Token;
        _portScanResults.Clear();
        _isPortScanRunning = true;
        SetPortScanButtonsEnabled(false);
        CancelPortScanButton.IsEnabled = true;
        PortScanTargetText.Text = $"Scanning {ports.Count:N0} TCP port{(ports.Count == 1 ? "" : "s")} on {device.Address}...";

        var scanned = 0;
        var open = 0;
        using var gate = new SemaphoreSlim(96);
        var tasks = ports
            .Distinct()
            .Where(port => port is >= 1 and <= 65535)
            .OrderBy(port => port)
            .Select(async port =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var result = await ProbeTcpPortAsync(ipAddress, port, cancellationToken);
                    var scannedCount = Interlocked.Increment(ref scanned);
                    if (result is not null)
                    {
                        Interlocked.Increment(ref open);
                        await Dispatcher.BeginInvoke(() => _portScanResults.Add(result));
                    }

                    if (scannedCount % 64 == 0 || scannedCount == ports.Count)
                    {
                        await Dispatcher.BeginInvoke(() =>
                        {
                            PortScanTargetText.Text = $"Scanned {scannedCount:N0}/{ports.Count:N0} TCP ports on {device.Address}. Open: {open:N0}.";
                        });
                    }
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToList();

        try
        {
            await Task.WhenAll(tasks);
            PortScanTargetText.Text = cancellationToken.IsCancellationRequested
                ? $"Port scan canceled for {device.Address}. Open: {open:N0}."
                : $"Port scan complete for {device.Address}. Open: {open:N0}.";
        }
        catch (OperationCanceledException)
        {
            PortScanTargetText.Text = $"Port scan canceled for {device.Address}. Open: {open:N0}.";
        }
        finally
        {
            _isPortScanRunning = false;
            CancelPortScanButton.IsEnabled = false;
            UpdatePortScanTarget();
        }
    }

    private static async Task<PortScanResult?> ProbeTcpPortAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ipAddress, port).WaitAsync(TimeSpan.FromMilliseconds(450), cancellationToken);
            if (!client.Connected)
            {
                return null;
            }

            return new PortScanResult
            {
                Port = port,
                State = "Open",
                Service = GetCommonServiceName(port),
                Notes = "TCP connection accepted"
            };
        }
        catch
        {
            return null;
        }
    }

    private void UpdatePortScanTarget()
    {
        if (_isPortScanRunning)
        {
            return;
        }

        if (IpScanResultsGrid?.SelectedItem is IpScanDevice device)
        {
            PortScanTargetText.Text = $"Selected: {device.Address}{(string.IsNullOrWhiteSpace(device.HostName) ? "" : $" ({device.HostName})")}";
            SetPortScanButtonsEnabled(true);
            return;
        }

        PortScanTargetText.Text = "Select a discovered device.";
        SetPortScanButtonsEnabled(false);
    }

    private void SetPortScanButtonsEnabled(bool isEnabled)
    {
        ScanCommonPortsButton.IsEnabled = isEnabled;
        ScanPortRangeButton.IsEnabled = isEnabled;
    }

    private bool TryReadPortRange(out int startPort, out int endPort, out string error)
    {
        startPort = 0;
        endPort = 0;
        error = "";

        if (!int.TryParse(PortStartTextBox.Text.Trim(), out startPort)
            || !int.TryParse(PortEndTextBox.Text.Trim(), out endPort)
            || startPort < 1
            || endPort > 65535)
        {
            error = "Ports must be numbers from 1 to 65535.";
            return false;
        }

        if (endPort < startPort)
        {
            error = "End port must be greater than or equal to start port.";
            return false;
        }

        return true;
    }

    private static int[] GetCommonTcpPorts()
    {
        return
        [
            20, 21, 22, 23, 25, 53, 80, 110, 123, 135, 137, 138, 139, 143, 161, 389, 443, 445,
            465, 500, 515, 548, 587, 631, 993, 995, 1433, 1521, 1723, 1883, 2049, 3306, 3389,
            5000, 5357, 5432, 5900, 5985, 5986, 8000, 8080, 8443, 9000, 9090, 9100, 10000
        ];
    }

    private static string GetCommonServiceName(int port)
    {
        return port switch
        {
            20 => "FTP data",
            21 => "FTP",
            22 => "SSH",
            23 => "Telnet",
            25 => "SMTP",
            53 => "DNS",
            80 => "HTTP",
            110 => "POP3",
            123 => "NTP",
            135 => "MS RPC",
            137 => "NetBIOS",
            138 => "NetBIOS",
            139 => "SMB/NetBIOS",
            143 => "IMAP",
            161 => "SNMP",
            389 => "LDAP",
            443 => "HTTPS",
            445 => "SMB",
            465 => "SMTPS",
            500 => "IPsec/IKE",
            515 => "LPD print",
            548 => "AFP",
            587 => "SMTP submit",
            631 => "IPP print",
            993 => "IMAPS",
            995 => "POP3S",
            1433 => "SQL Server",
            1521 => "Oracle",
            1723 => "PPTP",
            1883 => "MQTT",
            2049 => "NFS",
            3306 => "MySQL",
            3389 => "RDP",
            5000 => "UPnP/HTTP",
            5357 => "WSDAPI",
            5432 => "PostgreSQL",
            5900 => "VNC",
            5985 => "WinRM HTTP",
            5986 => "WinRM HTTPS",
            8000 => "Alt HTTP",
            8080 => "HTTP proxy",
            8443 => "Alt HTTPS",
            9000 => "App service",
            9090 => "Web admin",
            9100 => "Raw print",
            10000 => "Webmin",
            _ => "Unknown"
        };
    }

    private void InitializeSerialControls()
    {
        SerialBaudComboBox.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
        SerialBaudComboBox.SelectedItem = 9600;
        SerialDataBitsComboBox.ItemsSource = new[] { 7, 8 };
        SerialDataBitsComboBox.SelectedItem = 8;
        SerialParityComboBox.ItemsSource = Enum.GetNames<Parity>();
        SerialParityComboBox.SelectedItem = Parity.None.ToString();
        SerialStopBitsComboBox.ItemsSource = new[] { "One", "Two" };
        SerialStopBitsComboBox.SelectedItem = "One";
        SerialFlowControlComboBox.ItemsSource = Enum.GetNames<Handshake>();
        SerialFlowControlComboBox.SelectedItem = Handshake.None.ToString();
        SerialRtsModeComboBox.ItemsSource = new[] { "Default", "RTS high while TX", "RTS low while TX" };
        SerialRtsModeComboBox.SelectedIndex = 0;
        SerialLineEndingComboBox.ItemsSource = new[] { "None", "CR", "LF", "CRLF" };
        SerialLineEndingComboBox.SelectedItem = "CRLF";
        RefreshSerialPorts();
    }

    private void RefreshSerialPorts()
    {
        var selected = SerialPortComboBox.SelectedItem as string;
        var ports = SerialPort.GetPortNames().OrderBy(port => port).ToList();
        SerialPortComboBox.ItemsSource = ports;
        if (ports.Count == 0)
        {
            SerialStatusText.Text = "No COM ports found.";
            return;
        }

        SerialPortComboBox.SelectedItem = ports.Contains(selected) ? selected : ports[0];
    }

    private void ConnectSerialPort()
    {
        if (SerialPortComboBox.SelectedItem is not string portName)
        {
            SerialStatusText.Text = "Choose a COM port first.";
            return;
        }

        try
        {
            CloseSerialPort();
            var baud = SerialBaudComboBox.SelectedItem is int selectedBaud ? selectedBaud : 9600;
            var dataBits = SerialDataBitsComboBox.SelectedItem is int selectedDataBits ? selectedDataBits : 8;
            var parity = Enum.TryParse<Parity>(SerialParityComboBox.SelectedItem?.ToString(), out var selectedParity) ? selectedParity : Parity.None;
            var stopBits = SerialStopBitsComboBox.SelectedItem?.ToString() == "Two" ? StopBits.Two : StopBits.One;
            var handshake = Enum.TryParse<Handshake>(SerialFlowControlComboBox.SelectedItem?.ToString(), out var selectedHandshake) ? selectedHandshake : Handshake.None;

            _serialPort = new SerialPort(portName, baud, parity, dataBits, stopBits)
            {
                Handshake = handshake,
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = SerialRs485CheckBox.IsChecked != true || SerialRtsModeComboBox.SelectedIndex != 2
            };
            _serialPort.DataReceived += SerialPortDataReceived;
            _serialPort.Open();
            SerialConnectButton.IsEnabled = false;
            SerialDisconnectButton.IsEnabled = true;
            SerialStatusText.Text = $"Connected to {portName} at {baud} {dataBits}{parity.ToString()[0]}{(stopBits == StopBits.Two ? "2" : "1")}.";
            AddSerialLog("SYS", $"Connected {portName}", []);
        }
        catch (Exception ex)
        {
            SerialStatusText.Text = $"Serial connect failed: {ex.Message}";
            CloseSerialPort();
        }
    }

    private void CloseSerialPort()
    {
        if (_serialPort is null)
        {
            if (SerialConnectButton is not null)
            {
                SerialConnectButton.IsEnabled = true;
                SerialDisconnectButton.IsEnabled = false;
            }

            return;
        }

        try
        {
            _serialPort.DataReceived -= SerialPortDataReceived;
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        catch
        {
        }
        finally
        {
            _serialPort.Dispose();
            _serialPort = null;
            if (SerialConnectButton is not null)
            {
                SerialConnectButton.IsEnabled = true;
                SerialDisconnectButton.IsEnabled = false;
                SerialStatusText.Text = "Serial disconnected.";
            }
        }
    }

    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _serialPort;
        if (port is null || !port.IsOpen)
        {
            return;
        }

        try
        {
            var count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            var read = port.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return;
            }

            var received = buffer.Take(read).ToArray();
            Dispatcher.BeginInvoke(() => AddSerialLog("RX", DecodeSerialText(received), received));
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => SerialStatusText.Text = $"Serial read error: {ex.Message}");
        }
    }

    private void SendSerialText()
    {
        var text = SerialSendTextBox.Text;
        if (string.IsNullOrEmpty(text) && SerialSendHexCheckBox.IsChecked != true)
        {
            return;
        }

        var bytes = SerialSendHexCheckBox.IsChecked == true
            ? ParseHexBytes(text)
            : Encoding.ASCII.GetBytes(text + GetSelectedLineEnding());
        if (bytes is null)
        {
            SerialStatusText.Text = "HEX send expects bytes like: 01 03 00 00 00 01";
            return;
        }

        SendSerialBytes(bytes);
    }

    private void SendSerialBytes(byte[] bytes)
    {
        var port = _serialPort;
        if (port is null || !port.IsOpen)
        {
            SerialStatusText.Text = "Serial port is not connected.";
            return;
        }

        try
        {
            ApplyRs485TransmitState(transmitting: true);
            port.Write(bytes, 0, bytes.Length);
            ApplyRs485TransmitState(transmitting: false);
            AddSerialLog("TX", DecodeSerialText(bytes), bytes);
        }
        catch (Exception ex)
        {
            SerialStatusText.Text = $"Serial send failed: {ex.Message}";
        }
    }

    private void ApplyRs485TransmitState(bool transmitting)
    {
        if (_serialPort is null || SerialRs485CheckBox.IsChecked != true)
        {
            return;
        }

        if (SerialRtsModeComboBox.SelectedIndex == 1)
        {
            _serialPort.RtsEnable = transmitting;
        }
        else if (SerialRtsModeComboBox.SelectedIndex == 2)
        {
            _serialPort.RtsEnable = !transmitting;
        }
    }

    private void InitializeModbusControls()
    {
        ModbusModeComboBox.ItemsSource = new[] { "TCP", "RTU" };
        ModbusModeComboBox.SelectedItem = "TCP";
        ModbusRtuBaudComboBox.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
        ModbusRtuBaudComboBox.SelectedItem = 9600;
        ModbusRtuDataBitsComboBox.ItemsSource = new[] { 7, 8 };
        ModbusRtuDataBitsComboBox.SelectedItem = 8;
        ModbusRtuParityComboBox.ItemsSource = Enum.GetNames<Parity>();
        ModbusRtuParityComboBox.SelectedItem = Parity.None.ToString();
        ModbusRtuStopBitsComboBox.ItemsSource = new[] { "One", "Two" };
        ModbusRtuStopBitsComboBox.SelectedItem = "One";
        ModbusOperationComboBox.ItemsSource = new[]
        {
            new ModbusOperationOption(3, "03 Read Holding Registers"),
            new ModbusOperationOption(4, "04 Read Input Registers"),
            new ModbusOperationOption(1, "01 Read Coils"),
            new ModbusOperationOption(2, "02 Read Discrete Inputs"),
            new ModbusOperationOption(6, "06 Write Single Register"),
            new ModbusOperationOption(5, "05 Write Single Coil"),
            new ModbusOperationOption(16, "16 Write Multiple Registers"),
            new ModbusOperationOption(15, "15 Write Multiple Coils")
        };
        ModbusOperationComboBox.SelectedIndex = 0;
        RefreshModbusSerialPorts();
        UpdateModbusModeControls();
        UpdateModbusOperationControls();
    }

    private void RefreshModbusSerialPorts()
    {
        var selected = ModbusRtuPortComboBox.SelectedItem as string;
        var ports = SerialPort.GetPortNames().OrderBy(port => port).ToList();
        ModbusRtuPortComboBox.ItemsSource = ports;
        if (ports.Count == 0)
        {
            if (ModbusModeComboBox.SelectedItem?.ToString() == "RTU")
            {
                ModbusStatusText.Text = "No COM ports found for Modbus RTU.";
            }

            return;
        }

        ModbusRtuPortComboBox.SelectedItem = ports.Contains(selected) ? selected : ports[0];
    }

    private void UpdateModbusModeControls()
    {
        if (ModbusModeComboBox is null)
        {
            return;
        }

        var isTcp = ModbusModeComboBox.SelectedItem?.ToString() != "RTU";
        ModbusTcpHostLabel.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        ModbusTcpHostTextBox.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        ModbusTcpPortLabel.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        ModbusTcpPortTextBox.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;

        ModbusRtuPortLabel.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuPortComboBox.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        RefreshModbusSerialPortsButton.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuBaudLabel.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuBaudComboBox.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuDataLabel.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuDataBitsComboBox.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuParityLabel.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuParityComboBox.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuStopLabel.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        ModbusRtuStopBitsComboBox.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateModbusOperationControls()
    {
        if (ModbusOperationComboBox?.SelectedItem is not ModbusOperationOption operation)
        {
            return;
        }

        var isWrite = operation.IsWrite;
        var isMultipleWrite = operation.FunctionCode is 15 or 16;
        ModbusCountLabel.Text = isWrite ? "Count" : "Count";
        ModbusRequestCountTextBox.IsEnabled = !isWrite || isMultipleWrite;
        ModbusValuesLabel.Visibility = isWrite ? Visibility.Visible : Visibility.Collapsed;
        ModbusValuesTextBox.Visibility = isWrite ? Visibility.Visible : Visibility.Collapsed;
        if (operation.FunctionCode is 5 or 6)
        {
            ModbusRequestCountTextBox.Text = "1";
        }
    }

    private async Task ExecuteModbusAsync()
    {
        if (CancelModbusButton.IsEnabled)
        {
            return;
        }

        if (!TryReadModbusRequest(out var request, out var error))
        {
            ModbusStatusText.Text = error;
            return;
        }

        _modbusCancellation?.Cancel();
        _modbusCancellation?.Dispose();
        _modbusCancellation = new CancellationTokenSource();
        var cancellationToken = _modbusCancellation.Token;

        ExecuteModbusButton.IsEnabled = false;
        CancelModbusButton.IsEnabled = true;
        _modbusValues.Clear();
        ModbusRequestHexText.Text = "Sending...";
        ModbusResponseHexText.Text = "Waiting...";
        ModbusStatusText.Text = $"Sending Modbus {request.FunctionCode:00} over {request.Mode}...";

        try
        {
            var result = request.Mode == "RTU"
                ? await _modbusClient.SendRtuAsync(
                    request.RtuPort,
                    request.BaudRate,
                    request.DataBits,
                    request.Parity,
                    request.StopBits,
                    request.UnitId,
                    request.FunctionCode,
                    request.Address,
                    request.Count,
                    request.Values,
                    request.Timeout,
                    cancellationToken)
                : await _modbusClient.SendTcpAsync(
                    request.Host,
                    request.TcpPort,
                    request.UnitId,
                    request.FunctionCode,
                    request.Address,
                    request.Count,
                    request.Values,
                    request.Timeout,
                    cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (var row in result.Values)
            {
                _modbusValues.Add(row);
            }

            ModbusRequestHexText.Text = FormatHex(result.RequestBytes);
            ModbusResponseHexText.Text = FormatHex(result.ResponseBytes);
            ModbusStatusText.Text = result.Status;
        }
        catch (OperationCanceledException)
        {
            ModbusStatusText.Text = "Modbus request canceled.";
        }
        finally
        {
            ExecuteModbusButton.IsEnabled = true;
            CancelModbusButton.IsEnabled = false;
        }
    }

    private bool TryReadModbusRequest(out ModbusUiRequest request, out string error)
    {
        request = ModbusUiRequest.Empty;
        error = "";

        if (ModbusOperationComboBox.SelectedItem is not ModbusOperationOption operation)
        {
            error = "Choose a Modbus function.";
            return false;
        }

        if (!byte.TryParse(ModbusUnitTextBox.Text.Trim(), out var unitId) || unitId == 0)
        {
            error = "Unit/slave ID must be 1-247.";
            return false;
        }

        if (!ushort.TryParse(ModbusStartAddressTextBox.Text.Trim(), out var address))
        {
            error = "Address must be 0-65535.";
            return false;
        }

        if (!ushort.TryParse(ModbusRequestCountTextBox.Text.Trim(), out var count) || count == 0)
        {
            error = "Count must be a positive number.";
            return false;
        }

        if (!int.TryParse(ModbusTimeoutTextBox.Text.Trim(), out var timeoutMs) || timeoutMs is < 100 or > 60000)
        {
            error = "Timeout must be 100-60000 milliseconds.";
            return false;
        }

        var values = operation.IsWrite ? ParseModbusValues(ModbusValuesTextBox.Text) : [];
        if (operation.FunctionCode is 5 or 6 && values.Count == 0)
        {
            error = "Single writes need one value.";
            return false;
        }

        if (operation.FunctionCode is 15 or 16)
        {
            if (values.Count == 0)
            {
                error = "Multiple writes need one or more values.";
                return false;
            }

            count = (ushort)values.Count;
            ModbusRequestCountTextBox.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (operation.FunctionCode is 1 or 2 && count > 2000)
        {
            error = "Coil/discrete input reads are limited to 2,000 points per request.";
            return false;
        }

        if (operation.FunctionCode is 3 or 4 && count > 125)
        {
            error = "Register reads are limited to 125 registers per request.";
            return false;
        }

        if (operation.FunctionCode == 15 && values.Count > 1968)
        {
            error = "Multiple coil writes are limited to 1,968 coils per request.";
            return false;
        }

        if (operation.FunctionCode == 16 && values.Count > 123)
        {
            error = "Multiple register writes are limited to 123 registers per request.";
            return false;
        }

        var mode = ModbusModeComboBox.SelectedItem?.ToString() == "RTU" ? "RTU" : "TCP";
        var host = ModbusTcpHostTextBox.Text.Trim();
        var tcpPort = 502;
        var rtuPort = ModbusRtuPortComboBox.SelectedItem as string ?? "";
        var baud = ModbusRtuBaudComboBox.SelectedItem is int selectedBaud ? selectedBaud : 9600;
        var dataBits = ModbusRtuDataBitsComboBox.SelectedItem is int selectedDataBits ? selectedDataBits : 8;
        var parity = Enum.TryParse<Parity>(ModbusRtuParityComboBox.SelectedItem?.ToString(), out var selectedParity) ? selectedParity : Parity.None;
        var stopBits = ModbusRtuStopBitsComboBox.SelectedItem?.ToString() == "Two" ? StopBits.Two : StopBits.One;

        if (mode == "TCP")
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                error = "Enter a Modbus TCP host.";
                return false;
            }

            if (!int.TryParse(ModbusTcpPortTextBox.Text.Trim(), out tcpPort) || tcpPort is < 1 or > 65535)
            {
                error = "TCP port must be 1-65535.";
                return false;
            }
        }
        else if (string.IsNullOrWhiteSpace(rtuPort))
        {
            error = "Choose a Modbus RTU COM port.";
            return false;
        }

        request = new ModbusUiRequest(
            mode,
            host,
            tcpPort,
            rtuPort,
            baud,
            dataBits,
            parity,
            stopBits,
            unitId,
            operation.FunctionCode,
            address,
            count,
            values,
            TimeSpan.FromMilliseconds(timeoutMs));
        return true;
    }

    private static IReadOnlyList<ushort> ParseModbusValues(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var values = new List<ushort>();
        foreach (var part in text.Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!ushort.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
                {
                    return [];
                }

                values.Add(hexValue);
                continue;
            }

            if (!ushort.TryParse(trimmed, out var value))
            {
                return [];
            }

            values.Add(value);
        }

        return values;
    }

    private void AddSerialLog(string direction, string text, byte[] bytes)
    {
        _serialLogEntries.Add(new SerialLogEntry
        {
            Direction = direction,
            Text = text,
            Hex = FormatHex(bytes)
        });

        if (_serialLogEntries.Count > 5000)
        {
            _serialLogEntries.RemoveAt(0);
        }

        SerialLogGrid.ScrollIntoView(_serialLogEntries[^1]);
    }

    private string GetSelectedLineEnding()
    {
        return SerialLineEndingComboBox.SelectedItem?.ToString() switch
        {
            "CR" => "\r",
            "LF" => "\n",
            "CRLF" => "\r\n",
            _ => ""
        };
    }

    private static string DecodeSerialText(byte[] bytes)
    {
        return Encoding.ASCII.GetString(bytes)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string FormatHex(byte[] bytes)
    {
        return bytes.Length == 0 ? "" : BitConverter.ToString(bytes).Replace("-", " ", StringComparison.Ordinal);
    }

    private static byte[]? ParseHexBytes(string text)
    {
        var parts = text.Split([' ', ',', '-', ':', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
            {
                return null;
            }
        }

        return bytes;
    }

    private void InitializeMqttControls()
    {
        MqttClientIdTextBox.Text = $"PodcastWorkbench-{Environment.MachineName}-{Guid.NewGuid():N}"[..Math.Min(42, $"PodcastWorkbench-{Environment.MachineName}-{Guid.NewGuid():N}".Length)];
        MqttSubscribeQosComboBox.ItemsSource = new[] { 0, 1, 2 };
        MqttSubscribeQosComboBox.SelectedItem = 0;
        MqttPublishQosComboBox.ItemsSource = new[] { 0, 1, 2 };
        MqttPublishQosComboBox.SelectedItem = 0;
        UpdateSqlAuthControls();
    }

    private async Task ConnectSqlAsync()
    {
        if (!TryBuildSqlConnectionString(out var connectionString, out var error))
        {
            SqlStatusText.Text = error;
            return;
        }

        try
        {
            DisconnectSql();
            _sqlCancellation?.Cancel();
            _sqlCancellation?.Dispose();
            _sqlCancellation = new CancellationTokenSource();
            SqlStatusText.Text = "Connecting to SQL Server...";
            SqlConnectButton.IsEnabled = false;

            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(_sqlCancellation.Token);
            _sqlConnection = connection;
            SqlDisconnectButton.IsEnabled = true;
            SqlStatusText.Text = $"SQL connected to {SqlServerTextBox.Text.Trim()} / {SqlDatabaseTextBox.Text.Trim()}.";
        }
        catch (Exception ex)
        {
            DisconnectSql();
            SqlStatusText.Text = $"SQL connect failed: {ex.Message}";
        }
        finally
        {
            if (_sqlConnection is null)
            {
                SqlConnectButton.IsEnabled = true;
            }
        }
    }

    private void DisconnectSql()
    {
        _sqlCancellation?.Cancel();
        if (_sqlConnection is not null)
        {
            try
            {
                _sqlConnection.Close();
            }
            catch
            {
            }
            finally
            {
                _sqlConnection.Dispose();
                _sqlConnection = null;
            }
        }

        if (SqlConnectButton is not null)
        {
            SqlConnectButton.IsEnabled = true;
            SqlDisconnectButton.IsEnabled = false;
            SqlExecuteButton.IsEnabled = true;
            SqlCancelButton.IsEnabled = false;
        }
    }

    private async Task ExecuteSqlAsync()
    {
        if (_sqlConnection is null)
        {
            await ConnectSqlAsync();
        }

        if (_sqlConnection is null)
        {
            return;
        }

        var sql = SqlQueryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sql))
        {
            SqlStatusText.Text = "Enter a SQL query first.";
            return;
        }

        if (SqlAllowWritesCheckBox.IsChecked != true && !LooksReadOnlySql(sql))
        {
            SqlStatusText.Text = "Read-only guard is on. Check Allow writes before running non-SELECT SQL.";
            return;
        }

        if (!int.TryParse(SqlTimeoutTextBox.Text.Trim(), out var timeoutSeconds) || timeoutSeconds is < 1 or > 600)
        {
            SqlStatusText.Text = "SQL timeout must be 1-600 seconds.";
            return;
        }

        _sqlCancellation?.Cancel();
        _sqlCancellation?.Dispose();
        _sqlCancellation = new CancellationTokenSource();
        var cancellationToken = _sqlCancellation.Token;

        SqlExecuteButton.IsEnabled = false;
        SqlCancelButton.IsEnabled = true;
        SqlResultsGrid.ItemsSource = null;
        SqlStatusText.Text = "Executing SQL...";

        try
        {
            await using var command = _sqlConnection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = timeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var table = new DataTable();
            table.Load(reader);
            SqlResultsGrid.ItemsSource = table.DefaultView;

            var affected = reader.RecordsAffected;
            var message = affected >= 0
                ? $"{table.Rows.Count:N0} row{(table.Rows.Count == 1 ? "" : "s")} returned. {affected:N0} affected."
                : $"{table.Rows.Count:N0} row{(table.Rows.Count == 1 ? "" : "s")} returned.";
            SqlStatusText.Text = message;
        }
        catch (OperationCanceledException)
        {
            SqlStatusText.Text = "SQL query canceled.";
        }
        catch (Exception ex)
        {
            SqlStatusText.Text = $"SQL query failed: {ex.Message}";
        }
        finally
        {
            SqlExecuteButton.IsEnabled = true;
            SqlCancelButton.IsEnabled = false;
        }
    }

    private bool TryBuildSqlConnectionString(out string connectionString, out string error)
    {
        connectionString = "";
        error = "";

        var server = SqlServerTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(server))
        {
            error = "Enter a SQL Server host or instance.";
            return false;
        }

        if (!int.TryParse(SqlPortTextBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            error = "SQL port must be 1-65535.";
            return false;
        }

        var database = SqlDatabaseTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(database))
        {
            error = "Enter a database name.";
            return false;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server.Contains('\\', StringComparison.Ordinal) ? server : $"{server},{port}",
            InitialCatalog = database,
            ConnectTimeout = 8,
            Encrypt = SqlEncryptCheckBox.IsChecked == true,
            TrustServerCertificate = SqlTrustCertificateCheckBox.IsChecked == true,
                ApplicationName = "Podcast Workbench"
        };

        if (SqlWindowsAuthCheckBox.IsChecked == true)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(SqlUserTextBox.Text))
            {
                error = "Enter a SQL username or use Windows Auth.";
                return false;
            }

            builder.UserID = SqlUserTextBox.Text.Trim();
            builder.Password = SqlPasswordBox.Password;
        }

        connectionString = builder.ConnectionString;
        return true;
    }

    private void UpdateSqlAuthControls()
    {
        if (SqlUserTextBox is null)
        {
            return;
        }

        var sqlLogin = SqlWindowsAuthCheckBox.IsChecked != true;
        SqlUserLabel.Visibility = sqlLogin ? Visibility.Visible : Visibility.Collapsed;
        SqlUserTextBox.Visibility = sqlLogin ? Visibility.Visible : Visibility.Collapsed;
        SqlPasswordLabel.Visibility = sqlLogin ? Visibility.Visible : Visibility.Collapsed;
        SqlPasswordBox.Visibility = sqlLogin ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool LooksReadOnlySql(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private async Task StartMqttBrokerAsync()
    {
        if (_mqttServer is not null)
        {
            MqttBrokerStatusText.Text = "Built-in broker is already running.";
            return;
        }

        if (!int.TryParse(MqttBrokerPortTextBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            MqttBrokerStatusText.Text = "Broker port must be 1-65535.";
            return;
        }

        try
        {
            var builder = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port);

            if (MqttBrokerLocalOnlyCheckBox.IsChecked == true)
            {
                builder = builder.WithDefaultEndpointBoundIPAddress(IPAddress.Loopback);
            }

            var options = builder.Build();
            var factory = new MqttFactory();
            _mqttServer = factory.CreateMqttServer(options);
            _mqttServer.ClientConnectedAsync += MqttBrokerClientConnectedAsync;
            _mqttServer.ClientDisconnectedAsync += MqttBrokerClientDisconnectedAsync;
            _mqttServer.InterceptingPublishAsync += MqttBrokerPublishInterceptedAsync;
            await _mqttServer.StartAsync();

            StartMqttBrokerButton.IsEnabled = false;
            StopMqttBrokerButton.IsEnabled = true;
            MqttBrokerPortTextBox.IsEnabled = false;
            MqttBrokerLocalOnlyCheckBox.IsEnabled = false;
            var host = MqttBrokerLocalOnlyCheckBox.IsChecked == true ? "127.0.0.1" : "all interfaces";
            MqttBrokerStatusText.Text = $"Built-in broker running on {host}:{port}.";
            AddMqttLog("BROKER", "$SYS/voiceworkbench/broker", $"Started on {host}:{port}", 0, false);
        }
        catch (Exception ex)
        {
            MqttBrokerStatusText.Text = $"Broker start failed: {ex.Message}";
            await StopMqttBrokerAsync();
        }
    }

    private async Task StopMqttBrokerAsync()
    {
        var server = _mqttServer;
        if (server is null)
        {
            StartMqttBrokerButton.IsEnabled = true;
            StopMqttBrokerButton.IsEnabled = false;
            MqttBrokerPortTextBox.IsEnabled = true;
            MqttBrokerLocalOnlyCheckBox.IsEnabled = true;
            return;
        }

        try
        {
            server.ClientConnectedAsync -= MqttBrokerClientConnectedAsync;
            server.ClientDisconnectedAsync -= MqttBrokerClientDisconnectedAsync;
            server.InterceptingPublishAsync -= MqttBrokerPublishInterceptedAsync;
            await server.StopAsync();
            AddMqttLog("BROKER", "$SYS/voiceworkbench/broker", "Stopped", 0, false);
        }
        catch (Exception ex)
        {
            MqttBrokerStatusText.Text = $"Broker stop failed: {ex.Message}";
        }
        finally
        {
            _mqttServer = null;
            StartMqttBrokerButton.IsEnabled = true;
            StopMqttBrokerButton.IsEnabled = false;
            MqttBrokerPortTextBox.IsEnabled = true;
            MqttBrokerLocalOnlyCheckBox.IsEnabled = true;
            MqttBrokerStatusText.Text = "Built-in broker stopped.";
        }
    }

    private void StopMqttBrokerForShutdown()
    {
        try
        {
            StopMqttBrokerAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private Task MqttBrokerClientConnectedAsync(ClientConnectedEventArgs args)
    {
        Dispatcher.BeginInvoke(() => AddMqttLog("BROKER", "$SYS/voiceworkbench/client", $"Connected: {args.ClientId} from {args.Endpoint}", 0, false));
        return Task.CompletedTask;
    }

    private Task MqttBrokerClientDisconnectedAsync(ClientDisconnectedEventArgs args)
    {
        Dispatcher.BeginInvoke(() => AddMqttLog("BROKER", "$SYS/voiceworkbench/client", $"Disconnected: {args.ClientId} ({args.ReasonCode})", 0, false));
        return Task.CompletedTask;
    }

    private Task MqttBrokerPublishInterceptedAsync(InterceptingPublishEventArgs args)
    {
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);
        Dispatcher.BeginInvoke(() => AddMqttLog(
            "BROKER",
            args.ApplicationMessage.Topic,
            $"{args.ClientId}: {payload}",
            (int)args.ApplicationMessage.QualityOfServiceLevel,
            args.ApplicationMessage.Retain));
        return Task.CompletedTask;
    }

    private async Task ConnectMqttAsync()
    {
        if (!int.TryParse(MqttPortTextBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            MqttStatusText.Text = "MQTT port must be 1-65535.";
            return;
        }

        var host = MqttBrokerTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            MqttStatusText.Text = "Enter an MQTT broker host.";
            return;
        }

        try
        {
            await DisconnectMqttAsync();
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            _mqttClient.ApplicationMessageReceivedAsync += MqttMessageReceivedAsync;
            _mqttClient.DisconnectedAsync += args =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    MqttConnectButton.IsEnabled = true;
                    MqttDisconnectButton.IsEnabled = false;
                    MqttStatusText.Text = args.Exception is null
                        ? "MQTT disconnected."
                        : $"MQTT disconnected: {args.Exception.Message}";
                });
                return Task.CompletedTask;
            };

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId(string.IsNullOrWhiteSpace(MqttClientIdTextBox.Text) ? $"PodcastWorkbench-{Guid.NewGuid():N}" : MqttClientIdTextBox.Text.Trim())
                .WithCleanSession();

            if (!string.IsNullOrWhiteSpace(MqttUsernameTextBox.Text))
            {
                builder = builder.WithCredentials(MqttUsernameTextBox.Text, MqttPasswordBox.Password);
            }

            if (MqttTlsCheckBox.IsChecked == true)
            {
                builder = builder.WithTls();
            }

            await _mqttClient.ConnectAsync(builder.Build(), CancellationToken.None);
            MqttConnectButton.IsEnabled = false;
            MqttDisconnectButton.IsEnabled = true;
            MqttStatusText.Text = $"MQTT connected to {host}:{port}.";
        }
        catch (Exception ex)
        {
            MqttStatusText.Text = $"MQTT connect failed: {ex.Message}";
            await DisconnectMqttAsync();
        }
    }

    private async Task DisconnectMqttAsync()
    {
        var client = _mqttClient;
        _mqttClient = null;
        if (client is null)
        {
            return;
        }

        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync();
            }
        }
        catch
        {
        }
        finally
        {
            client.Dispose();
            if (MqttConnectButton is not null)
            {
                MqttConnectButton.IsEnabled = true;
                MqttDisconnectButton.IsEnabled = false;
            }
        }
    }

    private void DisconnectMqttForShutdown()
    {
        try
        {
            DisconnectMqttAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task SubscribeMqttAsync()
    {
        if (_mqttClient is not { IsConnected: true } client)
        {
            MqttStatusText.Text = "Connect to an MQTT broker first.";
            return;
        }

        var topic = MqttSubscribeTopicTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            MqttStatusText.Text = "Enter a topic filter to subscribe.";
            return;
        }

        var qos = ToMqttQos(MqttSubscribeQosComboBox.SelectedItem);
        try
        {
            var filter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(qos)
                .Build();
            await client.SubscribeAsync(filter);
            MqttStatusText.Text = $"Subscribed to {topic} at QoS {(int)qos}.";
        }
        catch (Exception ex)
        {
            MqttStatusText.Text = $"Subscribe failed: {ex.Message}";
        }
    }

    private async Task PublishMqttAsync()
    {
        if (_mqttClient is not { IsConnected: true } client)
        {
            MqttStatusText.Text = "Connect to an MQTT broker first.";
            return;
        }

        var topic = MqttPublishTopicTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            MqttStatusText.Text = "Enter a publish topic.";
            return;
        }

        var qos = ToMqttQos(MqttPublishQosComboBox.SelectedItem);
        var payload = MqttPayloadTextBox.Text ?? "";
        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(MqttRetainCheckBox.IsChecked == true)
                .Build();
            await client.PublishAsync(message);
            AddMqttLog("TX", topic, payload, (int)qos, MqttRetainCheckBox.IsChecked == true);
            MqttStatusText.Text = $"Published {payload.Length:N0} character{(payload.Length == 1 ? "" : "s")} to {topic}.";
        }
        catch (Exception ex)
        {
            MqttStatusText.Text = $"Publish failed: {ex.Message}";
        }
    }

    private Task MqttMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var payload = args.ApplicationMessage.PayloadSegment.Count > 0
            ? Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment)
            : "";
        Dispatcher.BeginInvoke(() => AddMqttLog(
            "RX",
            args.ApplicationMessage.Topic,
            payload,
            (int)args.ApplicationMessage.QualityOfServiceLevel,
            args.ApplicationMessage.Retain));
        return Task.CompletedTask;
    }

    private void AddMqttLog(string direction, string topic, string payload, int qos, bool retained)
    {
        _mqttLogEntries.Add(new MqttLogEntry
        {
            Direction = direction,
            Topic = topic,
            Payload = payload,
            QualityOfService = qos,
            Retained = retained
        });

        if (_mqttLogEntries.Count > 5000)
        {
            _mqttLogEntries.RemoveAt(0);
        }

        MqttLogGrid.ScrollIntoView(_mqttLogEntries[^1]);
    }

    private static MqttQualityOfServiceLevel ToMqttQos(object? selected)
    {
        return selected switch
        {
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };
    }

    private static async Task<string> TryGetHostNameAsync(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ipAddress.ToString()).WaitAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
            return entry.HostName;
        }
        catch
        {
            return "";
        }
    }

    private static async Task<string> TryGetMacAddressAsync(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "arp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(ipAddress.ToString());

            process = Process.Start(startInfo);
            if (process is null)
            {
                return "";
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(700), cancellationToken);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains(ipAddress.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var mac = parts.FirstOrDefault(part => part.Count(ch => ch == '-') == 5 || part.Count(ch => ch == ':') == 5);
                if (!string.IsNullOrWhiteSpace(mac))
                {
                    return mac.ToUpperInvariant().Replace('-', ':');
                }
            }
        }
        catch
        {
        }
        finally
        {
            KillProcessIfRunning(process);
            process?.Dispose();
        }

        return "";
    }

    private static void KillProcessIfRunning(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }
        }
        catch
        {
        }
    }

    private static string GuessOperatingSystemFromTtl(int? ttl)
    {
        if (ttl is null)
        {
            return "Unknown";
        }

        return ttl switch
        {
            <= 64 => "Linux/macOS/Unix-ish",
            <= 128 => "Windows-ish",
            <= 255 => "Network device-ish",
            _ => "Unknown"
        };
    }

    private static string BuildIpScanNotes(int? ttl, string macAddress)
    {
        var notes = new List<string>();
        if (ttl is not null)
        {
            notes.Add("OS inferred from ping TTL");
        }

        if (string.IsNullOrWhiteSpace(macAddress))
        {
            notes.Add("MAC unavailable unless device is on local subnet/ARP cache");
        }

        return string.Join("; ", notes);
    }

    private bool TryReadIpRange(out uint startAddress, out uint endAddress, out string error)
    {
        startAddress = 0;
        endAddress = 0;
        error = "";

        if (!TryReadIpAddress(
                [StartIpOctet1TextBox, StartIpOctet2TextBox, StartIpOctet3TextBox, StartIpOctet4TextBox],
                out startAddress,
                out error))
        {
            error = $"Start address: {error}";
            return false;
        }

        if (!TryReadIpAddress(
                [EndIpOctet1TextBox, EndIpOctet2TextBox, EndIpOctet3TextBox, EndIpOctet4TextBox],
                out endAddress,
                out error))
        {
            error = $"End address: {error}";
            return false;
        }

        return true;
    }

    private static bool TryReadIpAddress(TextBox[] octetBoxes, out uint address, out string error)
    {
        address = 0;
        error = "";
        var octets = new byte[4];
        for (var i = 0; i < octetBoxes.Length; i++)
        {
            if (!int.TryParse(octetBoxes[i].Text.Trim(), out var value) || value < 0 || value > 255)
            {
                error = "Each octet must be a number from 0 to 255.";
                return false;
            }

            octets[i] = (byte)value;
        }

        address = ((uint)octets[0] << 24)
            | ((uint)octets[1] << 16)
            | ((uint)octets[2] << 8)
            | octets[3];
        return true;
    }

    private static IPAddress UInt32ToIpAddress(uint address)
    {
        return new IPAddress([
            (byte)(address >> 24),
            (byte)(address >> 16),
            (byte)(address >> 8),
            (byte)address
        ]);
    }

    private void WifiNetworkSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedWifiNetworkDetails();
        DrawWifiChannelGraph();
    }

    private void UpdateSelectedWifiNetworkDetails()
    {
        if (WifiSelectedNetworkText is null)
        {
            return;
        }

        if (WifiNetworkGrid?.SelectedItem is not WifiNetwork network)
        {
            WifiSelectedNetworkText.Text = "Click a network to inspect it.";
            return;
        }

        WifiSelectedNetworkText.Text =
            $"SSID: {network.Ssid}{Environment.NewLine}" +
            $"BSSID: {network.Bssid}{Environment.NewLine}" +
            $"Signal: {network.SignalPercent}%{Environment.NewLine}" +
            $"Band: {FormatWifiDetail(network.Band)}{Environment.NewLine}" +
            $"Channel: {(network.Channel > 0 ? network.Channel.ToString() : "Unknown")}{Environment.NewLine}" +
            $"Seen: {network.SeenLabel}{Environment.NewLine}" +
            $"Radio: {FormatWifiDetail(network.RadioType)}{Environment.NewLine}" +
            $"Authentication: {FormatWifiDetail(network.Authentication)}{Environment.NewLine}" +
            $"Encryption: {FormatWifiDetail(network.Encryption)}";
    }

    private void DrawWifiChannelGraph()
    {
        if (WifiChannelCanvas is null)
        {
            return;
        }

        WifiChannelCanvas.Children.Clear();
        var width = Math.Max(1d, WifiChannelCanvas.ActualWidth);
        var height = Math.Max(1d, WifiChannelCanvas.ActualHeight);
        var marginLeft = 48d;
        var marginRight = 24d;
        var marginTop = 26d;
        var marginBottom = 34d;
        var laneGap = 22d;
        var laneHeight = Math.Max(90d, (height - marginTop - marginBottom - laneGap) / 2d);
        var graphWidth = Math.Max(100d, width - marginLeft - marginRight);
        var twoFourTop = marginTop;
        var fiveTop = marginTop + laneHeight + laneGap;
        var twoFourRange = (Min: 1d, Max: 14d);
        var fiveRange = (Min: 32d, Max: 177d);

        AddWifiText("2.4 GHz", 8, twoFourTop + 8, 13, FontWeights.SemiBold);
        AddWifiText("5 GHz", 8, fiveTop + 8, 13, FontWeights.SemiBold);
        DrawWifiLane(twoFourTop, laneHeight, marginLeft, graphWidth, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], twoFourRange.Min, twoFourRange.Max);
        DrawWifiLane(fiveTop, laneHeight, marginLeft, graphWidth, [36, 40, 44, 48, 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144, 149, 153, 157, 161, 165, 169, 173, 177], fiveRange.Min, fiveRange.Max);

        if (_wifiNetworks.Count == 0)
        {
            AddWifiText(WifiStatusText?.Text ?? "No Wi-Fi scan data yet.", marginLeft, marginTop + 24, 14, FontWeights.Normal);
            return;
        }

        if (!_wifiNetworks.Any(IsTwoFourGhz))
        {
            AddWifiText("No 2.4 GHz signals returned by Windows in this scan.", marginLeft + 12d, twoFourTop + laneHeight * 0.42d, 13, FontWeights.Normal);
        }

        if (!_wifiNetworks.Any(network => !IsTwoFourGhz(network)))
        {
            AddWifiText("No 5 GHz signals returned by Windows in this scan.", marginLeft + 12d, fiveTop + laneHeight * 0.42d, 13, FontWeights.Normal);
        }

        DrawWifiNetworks(twoFourTop, fiveTop, laneHeight, marginLeft, graphWidth, twoFourRange, fiveRange);
    }

    private int DrawWifiNetworks(
        double twoFourTop,
        double fiveTop,
        double laneHeight,
        double marginLeft,
        double graphWidth,
        (double Min, double Max) twoFourRange,
        (double Min, double Max) fiveRange)
    {
        var drawnNetworks = 0;
        foreach (var network in _wifiNetworks.OrderBy(network => network.SignalPercent))
        {
            var isTwoFour = IsTwoFourGhz(network);
            var laneTop = isTwoFour ? twoFourTop : fiveTop;
            var visibleRange = isTwoFour ? twoFourRange : fiveRange;
            var channelMin = visibleRange.Min;
            var channelMax = visibleRange.Max;
            if (channelMax <= channelMin)
            {
                continue;
            }

            var channelWidth = isTwoFour ? 4.5d : 18d;
            var graphChannel = GetGraphChannel(network, isTwoFour ? 1 : 32, isTwoFour ? 14 : 177);
            var x = ChannelToX(graphChannel, channelMin, channelMax, marginLeft, graphWidth);
            var baseY = laneTop + laneHeight - 20d;
            var signalHeight = Math.Max(12d, (laneHeight - 42d) * Math.Clamp(network.SignalPercent, 0, 100) / 100d);
            var topY = baseY - signalHeight;
            var halfWidth = Math.Max(18d, graphWidth * channelWidth / Math.Max(1d, channelMax - channelMin + 1d));
            var color = GetWifiSignalColor(network.SignalPercent, isTwoFour);
            var minX = marginLeft;
            var maxX = marginLeft + graphWidth;

            var opacity = network.IsCached ? 0.45d : 1d;
            var polygon = new Polygon
            {
                Points =
                [
                    new Point(ClampWifiX(x - halfWidth, minX, maxX), baseY),
                    new Point(ClampWifiX(x - halfWidth * 0.45d, minX, maxX), baseY - signalHeight * 0.45d),
                    new Point(ClampWifiX(x, minX, maxX), topY),
                    new Point(ClampWifiX(x + halfWidth * 0.45d, minX, maxX), baseY - signalHeight * 0.45d),
                    new Point(ClampWifiX(x + halfWidth, minX, maxX), baseY)
                ],
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B)),
                Opacity = opacity
            };
            WifiChannelCanvas.Children.Add(polygon);

            WifiChannelCanvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = baseY,
                X2 = x,
                Y2 = topY - 6d,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2.5,
                Opacity = opacity
            });
            var marker = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                StrokeThickness = 1,
                Opacity = opacity
            };
            WifiChannelCanvas.Children.Add(marker);
            Canvas.SetLeft(marker, x - 4.5d);
            Canvas.SetTop(marker, topY - 10.5d);

            var shortSsid = network.Ssid.Length > 16 ? $"{network.Ssid[..16]}..." : network.Ssid;
            var label = $"{shortSsid} ch {FormatWifiChannel(network)}{(network.IsCached ? " cached" : "")}";
            AddWifiText(label, Math.Clamp(x - 58d, marginLeft + 4d, marginLeft + graphWidth - 128d), Math.Max(laneTop + 2d, topY - 28d), 11, FontWeights.SemiBold);
            drawnNetworks++;
        }

        return drawnNetworks;
    }

    private void DrawWifiLane(double top, double laneHeight, double left, double width, int[] channels, double minChannel, double maxChannel)
    {
        var bottom = top + laneHeight - 20d;
        var gridBrush = new SolidColorBrush(Color.FromArgb(65, 79, 93, 107));
        var baseline = new Line
        {
            X1 = left,
            Y1 = bottom,
            X2 = left + width,
            Y2 = bottom,
            Stroke = gridBrush,
            StrokeThickness = 1
        };
        WifiChannelCanvas.Children.Add(baseline);

        foreach (var channel in channels.Where(channel => channel >= minChannel && channel <= maxChannel))
        {
            var x = ChannelToX(channel, minChannel, maxChannel, left, width);
            WifiChannelCanvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = top + 10d,
                X2 = x,
                Y2 = bottom + 4d,
                Stroke = gridBrush,
                StrokeThickness = 1
            });
            AddWifiText(channel.ToString(), x - 9d, bottom + 8d, 10, FontWeights.Normal);
        }

        if (!channels.Any(channel => Math.Abs(channel - minChannel) < 0.01d))
        {
            AddWifiText($"{minChannel:0.#}", left - 10d, bottom + 8d, 10, FontWeights.Normal);
        }

        if (!channels.Any(channel => Math.Abs(channel - maxChannel) < 0.01d))
        {
            AddWifiText($"{maxChannel:0.#}", left + width - 26d, bottom + 8d, 10, FontWeights.Normal);
        }
    }

    private void AddWifiText(string text, double left, double top, double fontSize, FontWeight fontWeight)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(184, 199, 217)),
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };
        WifiChannelCanvas.Children.Add(textBlock);
        Canvas.SetLeft(textBlock, left);
        Canvas.SetTop(textBlock, top);
    }

    private static double ChannelToX(double channel, double minChannel, double maxChannel, double left, double width)
    {
        var clamped = Math.Clamp(channel <= 0 ? minChannel : channel, minChannel, maxChannel);
        return left + (clamped - minChannel) / (double)(maxChannel - minChannel) * width;
    }

    private string FormatWifiScanStatus(string status)
    {
        if (_wifiNetworks.Count == 0)
        {
            return status;
        }

        var twoFourCount = _wifiNetworks.Count(IsTwoFourGhz);
        var fiveCount = _wifiNetworks.Count(network => !IsTwoFourGhz(network));
        var cachedCount = _wifiNetworks.Count(network => network.IsCached);
        var cacheText = cachedCount > 0 ? $" Cached last-seen: {cachedCount}." : "";
        return $"{status} Showing 2.4 GHz: {twoFourCount}, 5/6 GHz: {fiveCount}.{cacheText}";
    }

    private static int GetGraphChannel(WifiNetwork network, int minChannel, int maxChannel)
    {
        return network.Channel > 0 ? network.Channel : (minChannel + maxChannel) / 2;
    }

    private static string FormatWifiChannel(WifiNetwork network)
    {
        return network.Channel > 0 ? network.Channel.ToString() : "?";
    }

    private static double ClampWifiX(double x, double minX, double maxX)
    {
        return Math.Clamp(x, minX, maxX);
    }

    private static string FormatWifiDetail(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    private static bool IsTwoFourGhz(WifiNetwork network)
    {
        return network.Band.Contains('2') || (network.Channel > 0 && network.Channel <= 14);
    }

    private static Color GetWifiSignalColor(int signalPercent, bool isTwoFour)
    {
        if (signalPercent >= 75)
        {
            return isTwoFour ? Color.FromRgb(0, 190, 230) : Color.FromRgb(66, 215, 125);
        }

        if (signalPercent >= 45)
        {
            return Color.FromRgb(215, 178, 32);
        }

        return Color.FromRgb(255, 116, 82);
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

    private void SecurityWatchEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (SecurityWatchEnabledCheckBox.IsChecked == true)
        {
            _securityPreviousSample = null;
            _securityWatchLastEventAt = DateTime.MinValue;
            AddSecurityWatchEvent("Security watch started", 0d, "");
            SecurityWatchStatusText.Text = "Security cam watching for motion.";

            if (_cameraAvailable && !_isCameraEnabled)
            {
                _isCameraEnabled = true;
                UpdateCameraEnabledState();
            }
        }
        else
        {
            _securityPreviousSample = null;
            AddSecurityWatchEvent("Security watch stopped", 0d, "");
            SecurityWatchStatusText.Text = "Security cam idle.";
        }
    }

    private void SecurityWatchSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SecurityWatchThresholdValueText is not null)
        {
            SecurityWatchThresholdValueText.Text = $"{GetSecurityWatchMotionThreshold():0.0}%";
        }

        if (SecurityWatchCooldownValueText is not null)
        {
            SecurityWatchCooldownValueText.Text = $"{GetSecurityWatchCooldownSeconds():0}s";
        }
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
                SecurityWatchPreviewImage.Source = null;
                SecurityWatchPreviewImage.Visibility = Visibility.Collapsed;
                CameraPlaceholder.Visibility = Visibility.Visible;
                SecurityWatchPlaceholder.Visibility = Visibility.Visible;
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
        SecurityWatchPreviewImage.Source = null;
        SecurityWatchPreviewImage.Visibility = Visibility.Collapsed;
        SecurityWatchPlaceholder.Visibility = Visibility.Visible;
        SecurityWatchStateText.Text = status;
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
            SecurityWatchPreviewImage.Source = latestFrame;
            SecurityWatchPreviewImage.Visibility = Visibility.Visible;
            SecurityWatchPlaceholder.Visibility = Visibility.Collapsed;
            ProcessSecurityCameraFrame(latestFrame);

            if (CameraComboBox.SelectedItem is CameraDevice camera)
            {
                var status = FormatCameraStatus("Live", camera, GetSelectedCameraMode());
                CameraPreviewStatusText.Text = status;
                SecurityWatchStateText.Text = status;
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

    private void ProcessSecurityCameraFrame(ImageSource frame)
    {
        var securityEnabled = SecurityWatchEnabledCheckBox?.IsChecked == true;
        if (!securityEnabled || frame is not BitmapSource bitmap)
        {
            return;
        }

        var now = DateTime.Now;
        if ((now - _securityLastAnalysisAt).TotalMilliseconds < 250d)
        {
            return;
        }

        _securityLastAnalysisAt = now;
        var sample = CreateSecurityFrameSample(bitmap);
        if (sample.Length == 0)
        {
            return;
        }

        if (_securityPreviousSample is null || _securityPreviousSample.Length != sample.Length)
        {
            _securityPreviousSample = sample;
            SecurityWatchStatusText.Text = "Security cam armed. Waiting for baseline motion.";
            return;
        }

        var motion = CalculateFrameMotionPercent(_securityPreviousSample, sample);
        _securityPreviousSample = sample;
        ProcessSecurityWatchMotion(bitmap, now, motion);
    }

    private void ProcessSecurityWatchMotion(BitmapSource bitmap, DateTime now, double motion)
    {
        var threshold = GetSecurityWatchMotionThreshold();
        SecurityWatchStatusText.Text = motion >= threshold
            ? $"Motion detected: {motion:0.0}%"
            : $"Watching. Motion: {motion:0.0}%";

        if (motion < threshold || (now - _securityWatchLastEventAt).TotalSeconds < GetSecurityWatchCooldownSeconds())
        {
            return;
        }

        _securityWatchLastEventAt = now;
        var snapshotPath = SecurityWatchSnapshotCheckBox.IsChecked == true
            ? SaveSecurityWatchSnapshot(bitmap, now)
            : "";
        AddSecurityWatchEvent("Motion", motion, snapshotPath);
    }

    private byte[] CreateSecurityFrameSample(BitmapSource bitmap)
    {
        const int sampleWidth = 80;
        const int sampleHeight = 45;
        try
        {
            var source = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var stride = source.PixelWidth * 4;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);
            var sample = new byte[sampleWidth * sampleHeight];

            for (var y = 0; y < sampleHeight; y++)
            {
                var sourceY = Math.Clamp((int)(y / (double)(sampleHeight - 1) * (source.PixelHeight - 1)), 0, source.PixelHeight - 1);
                for (var x = 0; x < sampleWidth; x++)
                {
                    var sourceX = Math.Clamp((int)(x / (double)(sampleWidth - 1) * (source.PixelWidth - 1)), 0, source.PixelWidth - 1);
                    var offset = sourceY * stride + sourceX * 4;
                    var blue = pixels[offset];
                    var green = pixels[offset + 1];
                    var red = pixels[offset + 2];
                    sample[y * sampleWidth + x] = (byte)((red * 30 + green * 59 + blue * 11) / 100);
                }
            }

            return sample;
        }
        catch
        {
            return [];
        }
    }

    private static double CalculateFrameMotionPercent(byte[] previous, byte[] current)
    {
        if (previous.Length == 0 || previous.Length != current.Length)
        {
            return 0d;
        }

        long totalDifference = 0;
        for (var i = 0; i < previous.Length; i++)
        {
            totalDifference += Math.Abs(previous[i] - current[i]);
        }

        return totalDifference / (previous.Length * 255d) * 100d;
    }

    private string SaveSecurityWatchSnapshot(BitmapSource bitmap, DateTime timestamp)
    {
        try
        {
            var folder = System.IO.Path.Combine(_outputFolder, $"SecurityCam_{timestamp:yyyy-MM-dd}");
            Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder, $"motion_{timestamp:HH-mm-ss-fff}.jpg");
            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex)
        {
            SecurityWatchStatusText.Text = $"Motion detected, but snapshot failed: {ex.Message}";
            return "";
        }
    }

    private void AddSecurityWatchEvent(string eventName, double motion, string file)
    {
        _securityWatchEvents.Insert(0, new SecurityCameraEvent
        {
            Event = eventName,
            Motion = motion <= 0d ? "" : $"{motion:0.0}%",
            File = file
        });

        while (_securityWatchEvents.Count > 200)
        {
            _securityWatchEvents.RemoveAt(_securityWatchEvents.Count - 1);
        }
    }

    private double GetSecurityWatchMotionThreshold()
    {
        return SecurityWatchThresholdSlider is null ? 8d : Math.Clamp(SecurityWatchThresholdSlider.Value, 1d, 30d);
    }

    private double GetSecurityWatchCooldownSeconds()
    {
        return SecurityWatchCooldownSlider is null ? 5d : Math.Clamp(SecurityWatchCooldownSlider.Value, 1d, 60d);
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
        _spectrumService.StopTestClipPlayback();
        _spectrumService.Stop();
        StartSelectedDevice();
    }

    private void InputChannelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputChannelComboBox.SelectedItem is InputChannelOption option)
        {
            _selectedInputChannelMode = option.Mode;
        }

        _spectrumService.StopTestClipPlayback();
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

            if (enabled && !_spectrumService.IsRunning && !_spectrumService.IsPlayingTestClip)
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
        if (_selectedDevice is null || !IsActive || _spectrumService.IsPlayingTestClip)
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

    private void RecordClipClicked(object sender, RoutedEventArgs e)
    {
        if (_spectrumService.IsRecordingTestClip)
        {
            _spectrumService.StopTestClipRecording();
            StatusText.Text = "Test clip saved";
            return;
        }

        _spectrumService.StopTestClipPlayback();
        StartSelectedDevice();
        if (!_spectrumService.IsRunning)
        {
            ClipStatusText.Text = "Choose a working mic before recording.";
            return;
        }

        _spectrumService.BeginTestClipRecording();
        StatusText.Text = "Recording test clip";
    }

    private void PlayClipClicked(object sender, RoutedEventArgs e)
    {
        if (_spectrumService.IsPlayingTestClip)
        {
            _spectrumService.StopTestClipPlayback();
            StartSelectedDevice();
            return;
        }

        _spectrumService.StartTestClipPlayback(Settings);
    }

    private void ReturnToLiveMicClicked(object sender, RoutedEventArgs e)
    {
        _spectrumService.StopTestClipPlayback();
        _spectrumService.StopTestClipRecording();
        StartSelectedDevice();
        UpdateTestClipUi();
    }

    private void StoreAClicked(object sender, RoutedEventArgs e)
    {
        _snapshotA = CaptureSnapshot("A");
        UpdateCompareUi("Stored current settings as A.");
    }

    private void StoreBClicked(object sender, RoutedEventArgs e)
    {
        _snapshotB = CaptureSnapshot("B");
        UpdateCompareUi("Stored current settings as B.");
    }

    private void LoadAClicked(object sender, RoutedEventArgs e)
    {
        if (_snapshotA is null)
        {
            return;
        }

        ApplySnapshot(_snapshotA);
        UpdateCompareUi("Listening to A.");
    }

    private void LoadBClicked(object sender, RoutedEventArgs e)
    {
        if (_snapshotB is null)
        {
            return;
        }

        ApplySnapshot(_snapshotB);
        UpdateCompareUi("Listening to B.");
    }

    private void TestClipStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateTestClipUi);
    }

    private void UpdateTestClipUi()
    {
        var duration = _spectrumService.TestClipDurationSeconds;
        var progressWidth = Math.Clamp(duration / 10d, 0d, 1d) * 260d;
        ClipProgressMeter.Width = progressWidth;
        PlayClipButton.IsEnabled = _spectrumService.HasTestClip;

        if (_spectrumService.IsRecordingTestClip)
        {
            RecordClipButton.Content = "Stop recording";
            PlayClipButton.Content = "Loop test clip";
            ClipStatusText.Text = $"Recording raw mic: {duration:0.0}/10.0 seconds";
            ClipProgressMeter.Fill = new SolidColorBrush(Color.FromRgb(218, 80, 72));
            return;
        }

        RecordClipButton.Content = "Record 10 seconds";
        ClipProgressMeter.Fill = new SolidColorBrush(Color.FromRgb(0, 190, 230));

        if (_spectrumService.IsPlayingTestClip)
        {
            PlayClipButton.Content = "Stop loop";
            ClipStatusText.Text = $"Looping {duration:0.0}s test clip through current settings";
            StatusText.Text = "Looping test clip";
            return;
        }

        PlayClipButton.Content = "Loop test clip";
        ClipStatusText.Text = duration > 0d
            ? $"Saved {duration:0.0}s raw test clip"
            : "No test clip recorded";
    }

    private ProcessingSnapshot CaptureSnapshot(string name)
    {
        return new ProcessingSnapshot(
            name,
            Settings.HighPassEnabled,
            Settings.HighPassFrequencyHz,
            Settings.DePopperEnabled,
            Settings.DePopperAmountDb,
            Settings.NoiseGateEnabled,
            Settings.NoiseGateThresholdDb,
            Settings.NoiseSuppressionEnabled,
            Settings.NoiseSuppressionAmountDb,
            Settings.EchoReducerEnabled,
            Settings.EchoReducerAmountDb,
            Settings.CompressorEnabled,
            Settings.CompressorThresholdDb,
            Settings.CompressorRatio,
            Settings.DeEsserEnabled,
            Settings.DeEsserAmountDb,
            Settings.MakeupGainDb,
            Settings.LimiterEnabled,
            Settings.LimiterCeilingDb,
            [.. Bands.Select(band => band.GainDb)]);
    }

    private void ApplySnapshot(ProcessingSnapshot snapshot)
    {
        Settings.HighPassEnabled = snapshot.HighPassEnabled;
        Settings.HighPassFrequencyHz = snapshot.HighPassFrequencyHz;
        Settings.DePopperEnabled = snapshot.DePopperEnabled;
        Settings.DePopperAmountDb = snapshot.DePopperAmountDb;
        Settings.NoiseGateEnabled = snapshot.NoiseGateEnabled;
        Settings.NoiseGateThresholdDb = snapshot.NoiseGateThresholdDb;
        Settings.NoiseSuppressionEnabled = snapshot.NoiseSuppressionEnabled;
        Settings.NoiseSuppressionAmountDb = snapshot.NoiseSuppressionAmountDb;
        Settings.EchoReducerEnabled = snapshot.EchoReducerEnabled;
        Settings.EchoReducerAmountDb = snapshot.EchoReducerAmountDb;
        Settings.CompressorEnabled = snapshot.CompressorEnabled;
        Settings.CompressorThresholdDb = snapshot.CompressorThresholdDb;
        Settings.CompressorRatio = snapshot.CompressorRatio;
        Settings.DeEsserEnabled = snapshot.DeEsserEnabled;
        Settings.DeEsserAmountDb = snapshot.DeEsserAmountDb;
        Settings.MakeupGainDb = snapshot.MakeupGainDb;
        Settings.LimiterEnabled = snapshot.LimiterEnabled;
        Settings.LimiterCeilingDb = snapshot.LimiterCeilingDb;

        for (var i = 0; i < Bands.Count && i < snapshot.BandGains.Length; i++)
        {
            Bands[i].GainDb = snapshot.BandGains[i];
        }

        SetProcessingSliderDefaults(
            $"A/B slot {snapshot.Name} loaded. Yellow ticks now mark this slot's saved processing values.",
            snapshot.HighPassFrequencyHz,
            snapshot.DePopperAmountDb,
            snapshot.NoiseGateThresholdDb,
            snapshot.NoiseSuppressionAmountDb,
            snapshot.EchoReducerAmountDb,
            snapshot.CompressorThresholdDb,
            snapshot.CompressorRatio,
            snapshot.DeEsserAmountDb,
            snapshot.MakeupGainDb,
            snapshot.LimiterCeilingDb);
        StatusText.Text = $"Listening to {snapshot.Name}";
    }

    private void UpdateCompareUi(string message)
    {
        LoadAButton.IsEnabled = _snapshotA is not null;
        LoadBButton.IsEnabled = _snapshotB is not null;

        var aState = _snapshotA is null ? "A empty" : "A saved";
        var bState = _snapshotB is null ? "B empty" : "B saved";
        CompareStatusText.Text = $"{message} {aState}, {bState}.";
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
        UpdateProcessingMeters(frame.Telemetry);
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

    private void UpdateProcessingMeters(VoiceProcessingTelemetry telemetry)
    {
        var targetGainReduction = Math.Clamp(telemetry.CompressorGainReductionDb, 0d, 24d);
        _displayGainReductionDb = targetGainReduction > _displayGainReductionDb
            ? Ease(_displayGainReductionDb, targetGainReduction, 0.45d)
            : Ease(_displayGainReductionDb, targetGainReduction, 0.08d);

        var gainReduction = _displayGainReductionDb;
        GainReductionText.Text = $"Compressor: -{gainReduction:0.0} dB";
        GainReductionMeter.Width = Math.Max(0d, (gainReduction / 12d) * 260d);

        var compressorActive = _displayGainReductionDb > 0.25d;
        CompressorStateText.Text = compressorActive
            ? $"Compressing: voice {telemetry.CompressorInputLevelDb:0.0} dB, threshold {telemetry.CompressorThresholdDb:0.0} dB"
            : $"Below threshold: voice {telemetry.CompressorInputLevelDb:0.0} dB, threshold {telemetry.CompressorThresholdDb:0.0} dB";
        CompressorStateText.Foreground = compressorActive
            ? new SolidColorBrush(Color.FromRgb(215, 178, 32))
            : new SolidColorBrush(Color.FromRgb(184, 199, 217));

        _displayGateOpenness = Ease(_displayGateOpenness, telemetry.GateOpenness, telemetry.GateOpenness > _displayGateOpenness ? 0.35d : 0.08d);
        if (_displayGateOpenness < 0.45d)
        {
            _gateClosedHoldFrames = 10;
        }
        else if (_displayGateOpenness > 0.7d && _gateClosedHoldFrames > 0)
        {
            _gateClosedHoldFrames--;
        }

        var gateOpen = _gateClosedHoldFrames == 0 && _displayGateOpenness > 0.6d;
        if (gateOpen)
        {
            GateIndicator.Fill = new SolidColorBrush(Color.FromRgb(66, 215, 125));
            GateStateText.Text = "Gate open";
        }
        else
        {
            GateIndicator.Fill = new SolidColorBrush(Color.FromRgb(218, 80, 72));
            GateStateText.Text = "Gate reducing noise";
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
        if (_spectrumService.IsPlayingTestClip)
        {
            StatusText.Text = "Looping test clip";
            return;
        }

        if (_spectrumService.IsRecordingTestClip)
        {
            StatusText.Text = "Recording test clip";
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
        Settings.HighPassFrequencyHz = highPassFrequencyHz;
        Settings.DePopperEnabled = dePopperAmountDb > 0;
        Settings.DePopperAmountDb = dePopperAmountDb;
        Settings.NoiseGateEnabled = true;
        Settings.NoiseGateThresholdDb = gateThresholdDb;
        Settings.NoiseSuppressionEnabled = noiseSuppressionAmountDb > 0;
        Settings.NoiseSuppressionAmountDb = noiseSuppressionAmountDb;
        Settings.EchoReducerEnabled = echoReducerAmountDb > 0;
        Settings.EchoReducerAmountDb = echoReducerAmountDb;
        Settings.CompressorEnabled = true;
        Settings.CompressorThresholdDb = compressorThresholdDb;
        Settings.CompressorRatio = compressorRatio;
        Settings.DeEsserEnabled = deEsserAmountDb > 0;
        Settings.DeEsserAmountDb = deEsserAmountDb;
        Settings.MakeupGainDb = makeupGainDb;
        Settings.LimiterEnabled = true;
        Settings.LimiterCeilingDb = limiterCeilingDb;

        for (var i = 0; i < Bands.Count && i < gains.Length; i++)
        {
            Bands[i].GainDb = gains[i];
        }

        SetProcessingSliderDefaults(description, highPassFrequencyHz, dePopperAmountDb, gateThresholdDb, noiseSuppressionAmountDb, echoReducerAmountDb, compressorThresholdDb, compressorRatio, deEsserAmountDb, makeupGainDb, limiterCeilingDb);
        StatusText.Text = $"{name} preset loaded";
    }

    private void SetProcessingSliderDefaults(
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
        double limiterCeilingDb)
    {
        PresetDescriptionText.Text = description;
        SetProcessingSliderDefault(HighPassSlider, highPassFrequencyHz);
        SetProcessingSliderDefault(DePopperSlider, dePopperAmountDb);
        SetProcessingSliderDefault(GateThresholdSlider, gateThresholdDb);
        SetProcessingSliderDefault(NoiseSuppressionSlider, noiseSuppressionAmountDb);
        SetProcessingSliderDefault(EchoReducerSlider, echoReducerAmountDb);
        SetProcessingSliderDefault(CompressorThresholdSlider, compressorThresholdDb);
        SetProcessingSliderDefault(CompressorRatioSlider, compressorRatio);
        SetProcessingSliderDefault(DeEsserSlider, deEsserAmountDb);
        SetProcessingSliderDefault(MakeupGainSlider, makeupGainDb);
        SetProcessingSliderDefault(LimiterCeilingSlider, limiterCeilingDb);
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

    private sealed record ModbusUiRequest(
        string Mode,
        string Host,
        int TcpPort,
        string RtuPort,
        int BaudRate,
        int DataBits,
        Parity Parity,
        StopBits StopBits,
        byte UnitId,
        byte FunctionCode,
        ushort Address,
        ushort Count,
        IReadOnlyList<ushort> Values,
        TimeSpan Timeout)
    {
        public static ModbusUiRequest Empty { get; } = new(
            "TCP",
            "",
            502,
            "",
            9600,
            8,
            Parity.None,
            StopBits.One,
            1,
            3,
            0,
            1,
            [],
            TimeSpan.FromSeconds(1));
    }

    private sealed record ProcessingSnapshot(
        string Name,
        bool HighPassEnabled,
        double HighPassFrequencyHz,
        bool DePopperEnabled,
        double DePopperAmountDb,
        bool NoiseGateEnabled,
        double NoiseGateThresholdDb,
        bool NoiseSuppressionEnabled,
        double NoiseSuppressionAmountDb,
        bool EchoReducerEnabled,
        double EchoReducerAmountDb,
        bool CompressorEnabled,
        double CompressorThresholdDb,
        double CompressorRatio,
        bool DeEsserEnabled,
        double DeEsserAmountDb,
        double MakeupGainDb,
        bool LimiterEnabled,
        double LimiterCeilingDb,
        double[] BandGains);
}

