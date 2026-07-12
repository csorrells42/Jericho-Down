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
    public IReadOnlyList<CoreAudioSessionControlTarget> ControlTargets { get; init; } =
    [
        new(SessionInstanceIdentifier, SessionIdentifier)
    ];

    public int SessionCount { get; init; } = 1;

    public string DisplayTitle => CoreAudioSessionCatalog.CreateDisplayTitle(
        DisplayName,
        ProcessName,
        ProcessId,
        IsSystemSoundsSession);
}

public sealed record CoreAudioSessionControlTarget(
    string SessionInstanceIdentifier,
    string SessionIdentifier);

public static class CoreAudioSessionCatalog
{
    public static IReadOnlyList<AudioInputDevice> GetProcessLoopbackInputDevices(AudioOutputDevice? device = null)
    {
        var currentProcessId = Environment.ProcessId;
        return GetRenderSessions(device)
            .Where(session => !session.IsSystemSoundsSession
                && session.ProcessId > 0
                && session.ProcessId != currentProcessId)
            .GroupBy(session => session.ProcessId)
            .Select(group => group.First())
            .OrderBy(session => session.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Select(session => AudioInputDevice.CreateProcessLoopback(session.ProcessId, session.DisplayTitle))
            .ToArray();
    }

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

            return CollapseDuplicateSessions(sessions)
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
        return TrySetRenderSessionControls(
            device,
            [new CoreAudioSessionControlTarget(sessionInstanceIdentifier, sessionIdentifier)],
            volume,
            isMuted,
            out status);
    }

    public static bool TrySetRenderSessionControls(
        AudioOutputDevice? device,
        IReadOnlyList<CoreAudioSessionControlTarget> targets,
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

        var validTargets = (targets ?? [])
            .Where(target => !string.IsNullOrWhiteSpace(target.SessionInstanceIdentifier)
                || !string.IsNullOrWhiteSpace(target.SessionIdentifier))
            .ToArray();
        if (validTargets.Length == 0)
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

            var updated = 0;
            string? displayTitle = null;
            for (var i = 0; i < manager.Sessions.Count; i++)
            {
                var session = manager.Sessions[i];
                if (!validTargets.Any(target => IsMatchingSession(session, target)))
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

                updated++;
                displayTitle ??= CreateDisplayTitle(
                    session.DisplayName,
                    TryGetProcessName(unchecked((int)session.GetProcessID)),
                    unchecked((int)session.GetProcessID),
                    session.IsSystemSoundsSession);
            }

            if (updated == 0)
            {
                status = "CoreAudio session was no longer available.";
                return false;
            }

            status = updated == 1
                ? $"CoreAudio session updated: {displayTitle}."
                : $"CoreAudio app group updated: {displayTitle} ({updated} sessions).";
            return true;
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
        var sessionCountText = session.SessionCount > 1
            ? $", {session.SessionCount} sessions"
            : string.Empty;
        return $"{session.DisplayTitle} ({session.State}, {volumeText}, peak {FormatPeakDb(session.PeakLevel)}{sessionCountText})";
    }

    private static bool IsMatchingSession(
        AudioSessionControl session,
        string sessionInstanceIdentifier,
        string sessionIdentifier)
    {
        return IsMatchingSession(
            session,
            new CoreAudioSessionControlTarget(sessionInstanceIdentifier, sessionIdentifier));
    }

    private static bool IsMatchingSession(
        AudioSessionControl session,
        CoreAudioSessionControlTarget target)
    {
        try
        {
            var liveInstanceIdentifier = session.GetSessionInstanceIdentifier ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(target.SessionInstanceIdentifier)
                && liveInstanceIdentifier.Equals(target.SessionInstanceIdentifier, StringComparison.Ordinal))
            {
                return true;
            }

            var liveSessionIdentifier = session.GetSessionIdentifier ?? string.Empty;
            return !string.IsNullOrWhiteSpace(target.SessionIdentifier)
                && liveSessionIdentifier.Equals(target.SessionIdentifier, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<CoreAudioSessionSnapshot> CollapseDuplicateSessions(
        IReadOnlyList<CoreAudioSessionSnapshot> sessions)
    {
        return sessions
            .GroupBy(CreateSessionGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(CreateCollapsedSession)
            .ToArray();
    }

    private static CoreAudioSessionSnapshot CreateCollapsedSession(IGrouping<string, CoreAudioSessionSnapshot> group)
    {
        var sessions = group.ToArray();
        var representative = sessions
            .OrderByDescending(session => session.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(session => session.PeakLevel)
            .ThenBy(session => session.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .First();
        var targets = sessions
            .SelectMany(session => session.ControlTargets)
            .Distinct()
            .ToArray();
        var state = sessions.Any(session => session.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
            ? "Active"
            : representative.State;
        var volume = sessions
            .Where(session => !session.IsMuted)
            .Select(session => session.Volume)
            .DefaultIfEmpty(representative.Volume)
            .Average();

        return representative with
        {
            IsMuted = sessions.All(session => session.IsMuted),
            Volume = Math.Clamp(volume, 0f, 1f),
            PeakLevel = sessions.Max(session => session.PeakLevel),
            State = state,
            ControlTargets = targets,
            SessionCount = sessions.Length
        };
    }

    private static string CreateSessionGroupKey(CoreAudioSessionSnapshot session)
    {
        if (session.IsSystemSoundsSession)
        {
            return "system-sounds";
        }

        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return $"display:{NormalizeGroupKey(session.DisplayName)}";
        }

        if (!string.IsNullOrWhiteSpace(session.ProcessName))
        {
            return $"process:{NormalizeGroupKey(session.ProcessName)}";
        }

        return session.ProcessId > 0
            ? $"pid:{session.ProcessId}"
            : $"session:{NormalizeGroupKey(session.SessionIdentifier)}";
    }

    private static string NormalizeGroupKey(string value)
    {
        return value.Trim().ToUpperInvariant();
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
