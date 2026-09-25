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
    public const int LowLatencyMilliseconds = 35;
    public const int MinimumCustomLatencyMilliseconds = 6;
    public const int MaximumCustomLatencyMilliseconds = 240;
    public const int StabilityProcessedOutputMaximumBufferMilliseconds = 350;
    public const int BalancedProcessedOutputMaximumBufferMilliseconds = 240;
    public const int LowLatencyProcessedOutputMaximumBufferMilliseconds = 160;
    public const int ProcessedOutputProviderBufferMilliseconds = 500;

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

    public TimeSpan ProcessedOutputInitialBufferDuration => TimeSpan.FromMilliseconds(EffectiveLatencyMilliseconds);

    public TimeSpan ProcessedOutputTargetBufferDuration => TimeSpan.FromMilliseconds(EffectiveLatencyMilliseconds);

    public TimeSpan ProcessedOutputMaximumBufferDuration => TimeSpan.FromMilliseconds(Profile switch
    {
        WasapiOutputLatencyProfile.Balanced => BalancedProcessedOutputMaximumBufferMilliseconds,
        WasapiOutputLatencyProfile.LowLatency => LowLatencyProcessedOutputMaximumBufferMilliseconds,
        WasapiOutputLatencyProfile.Custom => Math.Clamp(EffectiveLatencyMilliseconds * 3, 120, 500),
        _ => StabilityProcessedOutputMaximumBufferMilliseconds
    });

    public TimeSpan ProcessedOutputProviderBufferDuration => TimeSpan.FromMilliseconds(
        Math.Max(ProcessedOutputProviderBufferMilliseconds, (int)ProcessedOutputMaximumBufferDuration.TotalMilliseconds));

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
