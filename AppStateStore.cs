using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace JerichoDown;

internal static class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string AppFolder = AppStoragePaths.SettingsFolder;
    private static readonly string SettingsPath = Path.Combine(AppFolder, "app-state.json");
    private static readonly string RunMarkerPath = Path.Combine(AppFolder, "run-state.json");
    private const long MaximumDiagnosticsLogBytes = 1024L * 1024L;
    private const int RetainedDiagnosticsLogCount = 5;
    private static readonly string DiagnosticsPath = Path.Combine(AppFolder, "diagnostics.log");
    private static readonly object DiagnosticsLogLock = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static DateTimeOffset _currentRunStartedAt;
    private static bool _runStarted;

    public static AppStartupRecovery StartupRecovery { get; private set; } = AppStartupRecovery.Clean;

    public static AppSettingsState LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettingsState();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettingsState>(json, JsonOptions) ?? new AppSettingsState();
        }
        catch (Exception ex)
        {
            LogDiagnostic("settings-load-failed", ex);
            return new AppSettingsState();
        }
    }

    public static void SaveSettings(AppSettingsState settings)
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            settings.UpdatedAt = DateTimeOffset.Now;
            AtomicFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            LogDiagnostic("settings-save-failed", ex);
        }
    }

    public static AppStartupRecovery BeginRun()
    {
        Directory.CreateDirectory(AppFolder);
        var previousRun = ReadRunState();
        var previousRunWasUnclean = previousRun is { CleanShutdownAt: null };
        StartupRecovery = previousRunWasUnclean
            ? new AppStartupRecovery(
                true,
                previousRun?.StartedAt,
                previousRun?.ProcessId,
                DiagnosticsPath)
            : AppStartupRecovery.Clean;

        _currentRunStartedAt = DateTimeOffset.Now;
        WriteRunState(new AppRunState
        {
            SessionId = SessionId,
            ProcessId = Environment.ProcessId,
            StartedAt = _currentRunStartedAt,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
        });
        _runStarted = true;
        if (previousRunWasUnclean)
        {
            LogDiagnostic(
                "safe-start",
                $"Previous run did not close cleanly. StartedAt={previousRun?.StartedAt:o}; ProcessId={previousRun?.ProcessId}.");
        }

        return StartupRecovery;
    }

    public static void MarkCleanShutdown()
    {
        if (!_runStarted)
        {
            return;
        }

        WriteRunState(new AppRunState
        {
            SessionId = SessionId,
            ProcessId = Environment.ProcessId,
            StartedAt = _currentRunStartedAt == default ? DateTimeOffset.Now : _currentRunStartedAt,
            CleanShutdownAt = DateTimeOffset.Now,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
        });
        _runStarted = false;
    }

    public static void LogUnhandledException(string source, Exception exception)
    {
        LogDiagnostic(source, exception);
    }

    public static void LogDiagnostic(string source, Exception exception)
    {
        LogDiagnostic(source, exception.ToString());
    }

    public static void LogDiagnostic(string source, string message)
    {
        try
        {
            lock (DiagnosticsLogLock)
            {
                Directory.CreateDirectory(AppFolder);
                RotateDiagnosticsLogIfNeeded();
                File.AppendAllText(
                    DiagnosticsPath,
                    $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private static void RotateDiagnosticsLogIfNeeded()
    {
        var currentLog = new FileInfo(DiagnosticsPath);
        if (!currentLog.Exists || currentLog.Length <= MaximumDiagnosticsLogBytes)
        {
            return;
        }

        var oldestLog = GetRotatedDiagnosticsPath(RetainedDiagnosticsLogCount);
        if (File.Exists(oldestLog))
        {
            File.Delete(oldestLog);
        }

        for (var index = RetainedDiagnosticsLogCount - 1; index >= 1; index--)
        {
            var source = GetRotatedDiagnosticsPath(index);
            if (File.Exists(source))
            {
                File.Move(source, GetRotatedDiagnosticsPath(index + 1));
            }
        }

        File.Move(DiagnosticsPath, GetRotatedDiagnosticsPath(1));
    }

    private static string GetRotatedDiagnosticsPath(int index)
    {
        return $"{DiagnosticsPath}.{index}";
    }

    private static AppRunState? ReadRunState()
    {
        try
        {
            if (!File.Exists(RunMarkerPath))
            {
                return null;
            }

            var json = File.ReadAllText(RunMarkerPath);
            return JsonSerializer.Deserialize<AppRunState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            LogDiagnostic("run-state-load-failed", ex);
            return null;
        }
    }

    private static void WriteRunState(AppRunState state)
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            AtomicFile.WriteAllText(RunMarkerPath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            LogDiagnostic("run-state-save-failed", ex);
        }
    }
}

internal sealed record AppStartupRecovery(
    bool PreviousRunDidNotCloseCleanly,
    DateTimeOffset? PreviousRunStartedAt,
    int? PreviousProcessId,
    string DiagnosticsPath)
{
    public static AppStartupRecovery Clean { get; } = new(false, null, null, string.Empty);
}

internal sealed class AppRunState
{
    public string? SessionId { get; set; }

    public int ProcessId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CleanShutdownAt { get; set; }

    public string? Version { get; set; }
}

internal sealed class AppSettingsState
{
    public DateTimeOffset UpdatedAt { get; set; }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public bool LeftControlRailCollapsed { get; set; }

    public int SelectedMicChannelNumber { get; set; } = 1;

    public List<MicChannelSettingsState> MicChannels { get; set; } = [];

    public List<MidiControlMappingSettingsState> MidiControlMappings { get; set; } = [];

    public bool MidiEnabled { get; set; }

    public string? MidiInputDeviceName { get; set; }

    public int? MidiInputDeviceProductId { get; set; }

    public string? MidiOutputDeviceName { get; set; }

    public int? MidiOutputDeviceProductId { get; set; }

    public double? MidiSequenceSpeedPercent { get; set; }

    public string? MicrophoneName { get; set; }

    public string? MicrophoneEndpointId { get; set; }

    public string? InputChannelMode { get; set; }

    public double? MixerMasterVolumePercent { get; set; }

    public bool MixerAutoNormalizeEnabled { get; set; } = true;

    public bool MixerLimiterEnabled { get; set; } = true;

    public double? MixerLimiterCeilingDb { get; set; }

    public string? MixerOutputMode { get; set; }

    public bool SystemAudioLoopbackDefaultMuteApplied { get; set; }

    public string? OutputDeviceName { get; set; }

    public string? OutputEndpointId { get; set; }

    public bool ProcessedOutputEnabled { get; set; }

    public string? WasapiOutputProfile { get; set; }

    public bool WasapiOutputExclusiveMode { get; set; }

    public int? WasapiOutputCustomLatencyMilliseconds { get; set; }

    public string? OutputFolder { get; set; }

    public string? AudioRecordingFolder { get; set; }

    public string? AudioRecordingSource { get; set; }

    public string? LastAudioRecordingPath { get; set; }

    public string? LastSessionRecordingPath { get; set; }

    public string? KaraokeTrackPath { get; set; }

    public List<string> KaraokeTrackPaths { get; set; } = [];

    public string? KaraokeBrowserFolder { get; set; }

    public string? KaraokeRecordingFolder { get; set; }

    public string? LastKaraokeRecordingPath { get; set; }

    public bool KaraokeRecordVideoEnabled { get; set; }

    public string? KaraokeLyrics { get; set; }

    public double? KaraokeKeySemitones { get; set; }

    public double? KaraokeTempoPercent { get; set; }

    public bool KaraokeVocalReductionEnabled { get; set; }

    public string? CameraName { get; set; }

    public string? CameraSource { get; set; }

    public string? CameraDevicePath { get; set; }

    public bool CameraEnabled { get; set; }

    public string? CameraModeLabel { get; set; }

    public int? CameraModeWidth { get; set; }

    public int? CameraModeHeight { get; set; }

    public double? CameraModeFramesPerSecond { get; set; }

    public string? CameraModeInputFormat { get; set; }

    public bool VideoDenoiseEnabled { get; set; }

    public double? VideoDenoiseStrength { get; set; }

    public string? ActivePresetName { get; set; }

    public bool ActivePresetIsUserPreset { get; set; }
}

internal sealed class MicChannelSettingsState
{
    public int ChannelNumber { get; set; }

    public string? DisplayName { get; set; }

    public string? MicrophoneName { get; set; }

    public string? MicrophoneEndpointId { get; set; }

    public string? InputChannelMode { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsMuted { get; set; }

    public double? VolumePercent { get; set; }

    public double? InputGainDb { get; set; }

    public double? Pan { get; set; }

    public bool PolarityInverted { get; set; }

    public bool IsSoloed { get; set; }

    public double? DelayMilliseconds { get; set; }

    public string? ActivePresetName { get; set; }

    public bool ActivePresetIsUserPreset { get; set; }

    public string? PresetDescription { get; set; }

    public double? AnalyzerSmoothing { get; set; }

    public List<EqualizerBandSettingsState> EqualizerBands { get; set; } = [];

    public Dictionary<string, double> NumberSettings { get; set; } = [];

    public Dictionary<string, bool> BooleanSettings { get; set; } = [];
}

internal sealed class MidiControlMappingSettingsState
{
    public string? ActionName { get; set; }

    public string? MessageType { get; set; }

    public int? Channel { get; set; }

    public int? Data1 { get; set; }
}

internal sealed class EqualizerBandSettingsState
{
    public string? Label { get; set; }

    public double FrequencyHz { get; set; }

    public double GainDb { get; set; }

    public bool IsEnabled { get; set; } = true;
}
