using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace JerichoDown.Audio;

public sealed record CoreAudioSessionSnapshot(
    string DisplayName,
    string ProcessName,
    int ProcessId,
    bool IsSystemSoundsSession,
    bool IsMuted,
    float Volume,
    float PeakLevel,
    string State,
    string SessionIdentifier,
    string SessionInstanceIdentifier)
{
    public string DisplayTitle => CoreAudioSessionCatalog.CreateDisplayTitle(
        DisplayName,
        ProcessName,
        ProcessId,
        IsSystemSoundsSession);
}

public static class CoreAudioSessionCatalog
{
    public static IReadOnlyList<CoreAudioSessionSnapshot> GetRenderSessions(AudioOutputDevice? device)
    {
        if (device?.IsAsio == true)
        {
            return [];
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = string.IsNullOrWhiteSpace(device?.EndpointId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(device.EndpointId);
            var manager = endpoint.AudioSessionManager;
            manager.RefreshSessions();

            var sessions = new List<CoreAudioSessionSnapshot>();
            for (var i = 0; i < manager.Sessions.Count; i++)
            {
                var session = manager.Sessions[i];
                if (TryCreateSnapshot(session, out var snapshot))
                {
                    sessions.Add(snapshot);
                }
            }

            return sessions
                .OrderByDescending(session => session.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(session => session.PeakLevel)
                .ThenBy(session => session.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static bool TrySetRenderSessionControls(
        AudioOutputDevice? device,
        string sessionInstanceIdentifier,
        string sessionIdentifier,
        float? volume,
        bool? isMuted,
        out string status)
    {
        status = string.Empty;
        if (device?.IsAsio == true)
        {
            status = "ASIO outputs do not expose Windows CoreAudio app sessions.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sessionInstanceIdentifier) && string.IsNullOrWhiteSpace(sessionIdentifier))
        {
            status = "CoreAudio session is missing its Windows session identifier.";
            return false;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = string.IsNullOrWhiteSpace(device?.EndpointId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(device.EndpointId);
            var manager = endpoint.AudioSessionManager;
            manager.RefreshSessions();

            for (var i = 0; i < manager.Sessions.Count; i++)
            {
                var session = manager.Sessions[i];
                if (!IsMatchingSession(session, sessionInstanceIdentifier, sessionIdentifier))
                {
                    continue;
                }

                var simpleVolume = session.SimpleAudioVolume;
                if (volume.HasValue)
                {
                    simpleVolume.Volume = Math.Clamp(volume.Value, 0f, 1f);
                }

                if (isMuted.HasValue)
                {
                    simpleVolume.Mute = isMuted.Value;
                }

                status = $"CoreAudio session updated: {CreateDisplayTitle(session.DisplayName, TryGetProcessName(unchecked((int)session.GetProcessID)), unchecked((int)session.GetProcessID), session.IsSystemSoundsSession)}.";
                return true;
            }

            status = "CoreAudio session was no longer available.";
            return false;
        }
        catch (Exception ex)
        {
            status = $"CoreAudio session control failed: {ex.Message}";
            return false;
        }
    }

    public static string CreateDisplayTitle(
        string? displayName,
        string? processName,
        int processId,
        bool isSystemSoundsSession)
    {
        if (isSystemSoundsSession)
        {
            return "System Sounds";
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            return processName.Trim();
        }

        return processId > 0 ? $"PID {processId}" : "Unknown app";
    }

    public static string FormatSessionSummary(CoreAudioSessionSnapshot session)
    {
        var volumeText = session.IsMuted
            ? "muted"
            : $"{Math.Clamp(session.Volume, 0f, 1f):P0}";
        return $"{session.DisplayTitle} ({session.State}, {volumeText}, peak {FormatPeakDb(session.PeakLevel)})";
    }

    private static bool IsMatchingSession(
        AudioSessionControl session,
        string sessionInstanceIdentifier,
        string sessionIdentifier)
    {
        try
        {
            var liveInstanceIdentifier = session.GetSessionInstanceIdentifier ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sessionInstanceIdentifier)
                && liveInstanceIdentifier.Equals(sessionInstanceIdentifier, StringComparison.Ordinal))
            {
                return true;
            }

            var liveSessionIdentifier = session.GetSessionIdentifier ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sessionIdentifier)
                && liveSessionIdentifier.Equals(sessionIdentifier, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateSnapshot(AudioSessionControl session, out CoreAudioSessionSnapshot snapshot)
    {
        snapshot = default!;
        try
        {
            var processId = unchecked((int)session.GetProcessID);
            var processName = TryGetProcessName(processId);
            snapshot = new CoreAudioSessionSnapshot(
                session.DisplayName ?? string.Empty,
                processName,
                processId,
                session.IsSystemSoundsSession,
                session.SimpleAudioVolume.Mute,
                session.SimpleAudioVolume.Volume,
                session.AudioMeterInformation.MasterPeakValue,
                session.State.ToString(),
                session.GetSessionIdentifier ?? string.Empty,
                session.GetSessionInstanceIdentifier ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string TryGetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatPeakDb(float peakLevel)
    {
        if (peakLevel <= 0f)
        {
            return "-inf dB";
        }

        return $"{20d * Math.Log10(Math.Clamp(peakLevel, float.Epsilon, 1f)):0.0} dB";
    }
}
