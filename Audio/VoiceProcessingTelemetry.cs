namespace PodcastWorkbench.Audio;

public sealed class VoiceProcessingTelemetry
{
    public double InputTrimDb { get; set; }

    public double HighPassActivityDb { get; set; }

    public double DePopperReductionDb { get; set; }

    public double NoiseGateReductionDb { get; set; }

    public double NoiseSuppressionReductionDb { get; set; }

    public double EchoReducerReductionDb { get; set; }

    public double CompressorGainReductionDb { get; set; }

    public double CompressorInputLevelDb { get; set; } = -100;

    public double CompressorThresholdDb { get; set; } = -18;

    public bool CompressorActive { get; set; }

    public bool GateOpen { get; set; } = true;

    public double GateOpenness { get; set; } = 1;

    public double DeEsserReductionDb { get; set; }

    public double PresenceBoostDb { get; set; }

    public double MakeupGainDb { get; set; }

    public double LimiterReductionDb { get; set; }
}

