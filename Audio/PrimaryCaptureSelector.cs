namespace JerichoDown.Audio;

public readonly record struct PrimaryCaptureCandidate(
    int ChannelNumber,
    int DeviceNumber,
    bool IsActive,
    bool IsMuted);

public static class PrimaryCaptureSelector
{
    public static int? ResolveChannelNumber(
        IReadOnlyList<PrimaryCaptureCandidate> candidates,
        int? requestedDeviceNumber)
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

        if (requestedDeviceNumber is not null)
        {
            var requested = candidates.FirstOrDefault(candidate => candidate.DeviceNumber == requestedDeviceNumber.Value);
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
}
