namespace JerichoDown.Modules.Audio.Dsp;

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

    public double ProgramPeakLevel { get; set; }

    public double ProgramRmsLevel { get; set; }

    public double MasterLimiterReductionDb { get; set; }

    public double MasterNormalizeGain { get; set; } = 1d;

    public double AudioCallbackIntervalMs { get; set; }

    public double AudioProcessingTimeMs { get; set; }

    public double AudioBufferDurationMs { get; set; }

    public double AudioExpectedCallbackIntervalMs { get; set; }

    public int AudioLateFrameCount { get; set; }

    public int AudioBufferResetCount { get; set; }

    public int SpectrumSkippedFrameCount { get; set; }

    public VoiceProcessingTelemetry Snapshot()
    {
        return new VoiceProcessingTelemetry
        {
            InputTrimDb = InputTrimDb,
            HighPassActivityDb = HighPassActivityDb,
            DePopperReductionDb = DePopperReductionDb,
            NoiseGateReductionDb = NoiseGateReductionDb,
            NoiseSuppressionReductionDb = NoiseSuppressionReductionDb,
            EchoReducerReductionDb = EchoReducerReductionDb,
            CompressorGainReductionDb = CompressorGainReductionDb,
            CompressorInputLevelDb = CompressorInputLevelDb,
            CompressorThresholdDb = CompressorThresholdDb,
            CompressorActive = CompressorActive,
            GateOpen = GateOpen,
            GateOpenness = GateOpenness,
            DeEsserReductionDb = DeEsserReductionDb,
            PresenceBoostDb = PresenceBoostDb,
            MakeupGainDb = MakeupGainDb,
            LimiterReductionDb = LimiterReductionDb,
            ProgramPeakLevel = ProgramPeakLevel,
            ProgramRmsLevel = ProgramRmsLevel,
            MasterLimiterReductionDb = MasterLimiterReductionDb,
            MasterNormalizeGain = MasterNormalizeGain,
            AudioCallbackIntervalMs = AudioCallbackIntervalMs,
            AudioProcessingTimeMs = AudioProcessingTimeMs,
            AudioBufferDurationMs = AudioBufferDurationMs,
            AudioExpectedCallbackIntervalMs = AudioExpectedCallbackIntervalMs,
            AudioLateFrameCount = AudioLateFrameCount,
            AudioBufferResetCount = AudioBufferResetCount,
            SpectrumSkippedFrameCount = SpectrumSkippedFrameCount
        };
    }
}

