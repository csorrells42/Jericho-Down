using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PodcastWorkbench;

internal static class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PodcastWorkbench");
    private static readonly string SettingsPath = Path.Combine(AppFolder, "app-state.json");
    private static readonly string RunMarkerPath = Path.Combine(AppFolder, "run-state.json");
    private static readonly string DiagnosticsPath = Path.Combine(AppFolder, "diagnostics.log");
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
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
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
            Directory.CreateDirectory(AppFolder);
            File.AppendAllText(
                DiagnosticsPath,
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
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
            File.WriteAllText(RunMarkerPath, JsonSerializer.Serialize(state, JsonOptions));
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

    public string? MicrophoneName { get; set; }

    public string? InputChannelMode { get; set; }

    public string? OutputDeviceName { get; set; }

    public string? OutputEndpointId { get; set; }

    public bool ProcessedOutputEnabled { get; set; }

    public string? OutputFolder { get; set; }

    public string? AudioRecordingFolder { get; set; }

    public string? LastAudioRecordingPath { get; set; }

    public string? LastSessionRecordingPath { get; set; }

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

    public string? VideoDenoiseMode { get; set; }

    public double? VideoDenoiseStrength { get; set; }

    public bool ProcessedTextureRecordingEnabled { get; set; }

    public string? ActivePresetName { get; set; }

    public bool ActivePresetIsUserPreset { get; set; }
}
