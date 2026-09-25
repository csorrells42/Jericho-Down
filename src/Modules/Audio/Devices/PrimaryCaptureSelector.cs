namespace JerichoDown.Modules.Audio.Devices;

public readonly record struct PrimaryCaptureCandidate(
    int ChannelNumber,
    int DeviceNumber,
    bool IsActive,
    bool IsMuted,
    string? EndpointId = null,
    AudioInputBackend Backend = AudioInputBackend.Windows);

public static class PrimaryCaptureSelector
{
    public static int? ResolveChannelNumber(
        IReadOnlyList<PrimaryCaptureCandidate> candidates,
        int? requestedDeviceNumber,
        string? requestedEndpointId = null,
        AudioInputBackend requestedBackend = AudioInputBackend.Windows)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var active = candidates.FirstOrDefault(candidate => candidate.IsActive);
        if (active.ChannelNumber > 0)
        {
            return active.ChannelNumber;
        }

        if (requestedDeviceNumber is not null || !string.IsNullOrWhiteSpace(requestedEndpointId))
        {
            var requested = candidates.FirstOrDefault(candidate => MatchesRequestedDevice(
                candidate,
                requestedDeviceNumber,
                requestedEndpointId,
                requestedBackend));
            if (requested.ChannelNumber > 0)
            {
                return requested.ChannelNumber;
            }
        }

        var unmuted = candidates.FirstOrDefault(candidate => !candidate.IsMuted);
        if (unmuted.ChannelNumber > 0)
        {
            return unmuted.ChannelNumber;
        }

        return candidates[0].ChannelNumber;
    }

    private static bool MatchesRequestedDevice(
        PrimaryCaptureCandidate candidate,
        int? requestedDeviceNumber,
        string? requestedEndpointId,
        AudioInputBackend requestedBackend)
    {
        if (!string.IsNullOrWhiteSpace(requestedEndpointId))
        {
            return candidate.Backend == requestedBackend
                && candidate.EndpointId?.Equals(requestedEndpointId, StringComparison.OrdinalIgnoreCase) == true;
        }

        return requestedDeviceNumber is not null
            && candidate.DeviceNumber == requestedDeviceNumber.Value
            && candidate.Backend == requestedBackend;
    }
}
