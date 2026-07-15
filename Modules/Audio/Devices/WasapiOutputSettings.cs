namespace JerichoDown.Modules.Audio.Devices;

public enum WasapiOutputLatencyProfile
{
    Stability,
    Balanced,
    LowLatency,
    Custom
}

public sealed record WasapiOutputSettings(
    WasapiOutputLatencyProfile Profile,
    bool ExclusiveMode,
    int CustomLatencyMilliseconds)
{
    public const int StabilityLatencyMilliseconds = 120;
    public const int BalancedLatencyMilliseconds = 80;
    public const int LowLatencyMilliseconds = 45;
    public const int MinimumCustomLatencyMilliseconds = 30;
    public const int MaximumCustomLatencyMilliseconds = 240;

    public static WasapiOutputSettings Default { get; } = new(
        WasapiOutputLatencyProfile.Stability,
        ExclusiveMode: false,
        StabilityLatencyMilliseconds);

    public int EffectiveLatencyMilliseconds => Profile switch
    {
        WasapiOutputLatencyProfile.Balanced => BalancedLatencyMilliseconds,
        WasapiOutputLatencyProfile.LowLatency => LowLatencyMilliseconds,
        WasapiOutputLatencyProfile.Custom => ClampCustomLatency(CustomLatencyMilliseconds),
        _ => StabilityLatencyMilliseconds
    };

    public bool UseEventDrivenOutput => true;

    public string DisplayText => $"{FormatProfile(Profile)} {(ExclusiveMode ? "exclusive" : "shared")}, {EffectiveLatencyMilliseconds} ms";

    public static WasapiOutputSettings FromPersisted(string? profile, bool exclusiveMode, int? customLatencyMilliseconds)
    {
        return new WasapiOutputSettings(
            ParseProfile(profile),
            exclusiveMode,
            ClampCustomLatency(customLatencyMilliseconds ?? StabilityLatencyMilliseconds));
    }

    public static WasapiOutputLatencyProfile ParseProfile(string? value)
    {
        return Enum.TryParse<WasapiOutputLatencyProfile>(value, ignoreCase: true, out var profile)
            ? profile
            : WasapiOutputLatencyProfile.Stability;
    }

    public static int ClampCustomLatency(int latencyMilliseconds)
    {
        return Math.Clamp(latencyMilliseconds, MinimumCustomLatencyMilliseconds, MaximumCustomLatencyMilliseconds);
    }

    public static string FormatProfile(WasapiOutputLatencyProfile profile)
    {
        return profile switch
        {
            WasapiOutputLatencyProfile.Balanced => "Balanced",
            WasapiOutputLatencyProfile.LowLatency => "Low latency",
            WasapiOutputLatencyProfile.Custom => "Custom",
            _ => "Stability"
        };
    }
}
