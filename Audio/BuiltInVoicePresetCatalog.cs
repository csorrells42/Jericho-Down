namespace JerichoDown.Audio;

internal sealed class BuiltInVoicePreset
{
    private readonly Action<VoiceProcessorSettings>? _configureAdditionalSettings;

    public BuiltInVoicePreset(
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
        IReadOnlyList<double> gains,
        Action<VoiceProcessorSettings>? configureAdditionalSettings = null)
    {
        Name = name;
        Description = description;
        HighPassFrequencyHz = highPassFrequencyHz;
        DePopperAmountDb = dePopperAmountDb;
        GateThresholdDb = gateThresholdDb;
        NoiseSuppressionAmountDb = noiseSuppressionAmountDb;
        EchoReducerAmountDb = echoReducerAmountDb;
        CompressorThresholdDb = compressorThresholdDb;
        CompressorRatio = compressorRatio;
        DeEsserAmountDb = deEsserAmountDb;
        MakeupGainDb = makeupGainDb;
        LimiterCeilingDb = limiterCeilingDb;
        Gains = gains.ToArray();
        _configureAdditionalSettings = configureAdditionalSettings;
    }

    public string Name { get; }

    public string Description { get; }

    public double HighPassFrequencyHz { get; }

    public double DePopperAmountDb { get; }

    public double GateThresholdDb { get; }

    public double NoiseSuppressionAmountDb { get; }

    public double EchoReducerAmountDb { get; }

    public double CompressorThresholdDb { get; }

    public double CompressorRatio { get; }

    public double DeEsserAmountDb { get; }

    public double MakeupGainDb { get; }

    public double LimiterCeilingDb { get; }

    public IReadOnlyList<double> Gains { get; }

    public void ConfigureAdditionalSettings(VoiceProcessorSettings settings)
    {
        _configureAdditionalSettings?.Invoke(settings);
    }
}

internal static class BuiltInVoicePresetCatalog
{
    public static BuiltInVoicePreset Flat { get; } = new(
        "Flat",
        "Neutral reference. No EQ curve, gentle defaults, useful when you want to hear the mic without a voice style.",
        80, 0, -55, 0, 0, -20, 2, 0, 0, -1,
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    public static BuiltInVoicePreset PodcastClean { get; } = new(
        "Podcast Clean",
        "Cuts rumble and mud, adds presence for clearer speech, and uses moderate compression for steady level.",
        85, 4, -50, 4, 2, -18, 3, 4, 2.5, -1,
        [-4, -3, -2, -1, 0, 0.5, 0, -1, -1.5, -1, 0, 1, 2, 2.5, 2, 1, 0.5, 0, -1, -2]);

    public static BuiltInVoicePreset WarmRadio { get; } = new(
        "Warm Radio",
        "Adds body and warmth, gently tames harshness, and uses a slightly stronger compressor for a denser sound.",
        70, 3, -52, 3, 3, -20, 3.5, 3, 3, -1,
        [-5, -3, -1, 0.5, 2, 2.5, 2, 1, 0, -1, -1.5, -1, 0, 1, 1.5, 1, 0, -0.5, -1, -2],
        settings =>
        {
            settings.SaturationEnabled = true;
            settings.SaturationAmount = 2.5;
        });

    public static BuiltInVoicePreset DeepWarm { get; } = new(
        "Deep Warm",
        "Leans into a deep, warm broadcast tone: strong chest and low-mid body, carved mud, softened bite, and dense compression.",
        58, 4.5, -52, 2.5, 1.5, -22, 4.2, 2.5, 2.5, -1,
        [-6, -4, -1, 1.5, 3.5, 3, 2, 0.5, -1.5, -2, -1, -0.2, 0.6, 1, 0.5, -0.5, -1.5, -2, -2.5, -3],
        settings =>
        {
            settings.InputTrimDb = 0;
            settings.DePopperFrequencyHz = 150;
            settings.DePopperThresholdDb = -32;
            settings.ExpanderThresholdDb = -58;
            settings.ExpanderRatio = 1.45;
            settings.ExpanderRangeDb = 8;
            settings.NoiseGateThresholdDb = -52;
            settings.NoiseGateAttackMs = 8;
            settings.NoiseGateHoldMs = 130;
            settings.NoiseGateReleaseMs = 220;
            settings.NoiseGateRangeDb = 20;
            settings.NoiseSuppressionSensitivity = 3;
            settings.EchoReducerSensitivity = 3.5;
            settings.CompressorAttackMs = 16;
            settings.CompressorReleaseMs = 190;
            settings.CompressorKneeDb = 8;
            settings.DeEsserFrequencyHz = 5600;
            settings.DeEsserThresholdDb = -34;
            settings.DeEsserRangeDb = 6;
            settings.PresenceEnhancerAmountDb = 1.1;
            settings.PresenceEnhancerFrequencyHz = 2600;
            settings.PresenceEnhancerWidthHz = 1800;
            settings.SaturationEnabled = true;
            settings.SaturationAmount = 3.5;
            settings.LimiterSoftClipDriveDb = 1;
            settings.LimiterLookaheadMs = 3;
            settings.LimiterReleaseMs = 85;
        });

    public static BuiltInVoicePreset NoisyRoom { get; } = new(
        "Noisy Room",
        "Raises the high-pass filter and tightens the gate to fight room noise, with firmer compression.",
        110, 6, -42, 8, 4, -21, 4, 5, 1.5, -1,
        [-8, -6, -4, -3, -2, -1, -1, -1.5, -2, -2, -1, 0, 1.5, 2, 1, 0, -1, -2, -3, -4]);

    public static BuiltInVoicePreset BrightHeadset { get; } = new(
        "Bright Headset",
        "Adds clarity and presence for darker headset mics while trimming low rumble and high fizz.",
        95, 4, -48, 5, 2, -16, 2.5, 6, 1.5, -1,
        [-5, -4, -3, -2, -1.5, -1, -1, -0.5, 0, 0.5, 1, 1.5, 2.5, 3, 2.5, 1.5, 0.5, -0.5, -1.5, -3]);

    private static readonly BuiltInVoicePreset[] Presets =
    [
        Flat,
        PodcastClean,
        WarmRadio,
        DeepWarm,
        NoisyRoom,
        BrightHeadset
    ];

    public static BuiltInVoicePreset? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Presets.FirstOrDefault(preset => preset.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
