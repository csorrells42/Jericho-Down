namespace JerichoDown.Audio;

public static class LiveMixAudibility
{
    public static bool IsAudible(bool isEnabled, bool isMuted, bool isSoloed, bool hasSolo)
    {
        return isEnabled && !isMuted && (!hasSolo || isSoloed);
    }

    public static double ResolveVolume(double volumeLinear, bool isEnabled, bool isMuted, bool isSoloed, bool hasSolo)
    {
        if (!IsAudible(isEnabled, isMuted, isSoloed, hasSolo))
        {
            return 0d;
        }

        return Math.Clamp(double.IsFinite(volumeLinear) ? volumeLinear : 0d, 0d, 8d);
    }
}
