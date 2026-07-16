namespace JerichoDown.Modules.Audio.Dsp;

public sealed class VoiceSampleProcessor
{
    private const double DenormalThreshold = 1.0E-20d;
    private const double LimiterTruePeakDetectorMarginDb = 0.6d;

    private readonly VoiceProcessorSettings _settings;
    private readonly GraphicEqualizerProcessor _graphicEqualizer;
    private readonly NAudioBiQuadFilterRack _naudioBiQuadFilterRack;
    private readonly NAudioPitchShiftProcessor _naudioPitchShiftProcessor;
    private readonly NAudioImpulseConvolutionProcessor _naudioImpulseConvolutionProcessor;
    private readonly NAudioEnvelopeGeneratorProcessor _naudioEnvelopeGeneratorProcessor;
    private readonly NAudioDmoEffectChain _naudioDmoEffectChain;
    private readonly double _sampleRate;
    private double _previousDcBlockerInput;
    private double _previousDcBlockerOutput;
    private double _previousHighPassInput;
    private double _previousHighPassOutput;
    private double _highPassWet;
    private double _highPassX1;
    private double _highPassX2;
    private double _highPassY1;
    private double _highPassY2;
    private double _humRemovalWet;
    private double _humNotchX1;
    private double _humNotchX2;
    private double _humNotchY1;
    private double _humNotchY2;
    private double _humHarmonicNotchX1;
    private double _humHarmonicNotchX2;
    private double _humHarmonicNotchY1;
    private double _humHarmonicNotchY2;
    private double _manualNotchWet;
    private double _manualNotchX1;
    private double _manualNotchX2;
    private double _manualNotchY1;
    private double _manualNotchY2;
    private double _parametricEqWet;
    private double _parametricEqX1;
    private double _parametricEqX2;
    private double _parametricEqY1;
    private double _parametricEqY2;
    private double _shelfEqWet;
    private double _lowShelfX1;
    private double _lowShelfX2;
    private double _lowShelfY1;
    private double _lowShelfY2;
    private double _highShelfX1;
    private double _highShelfX2;
    private double _highShelfY1;
    private double _highShelfY2;
    private double _lowPassWet;
    private double _lowPassX1;
    private double _lowPassX2;
    private double _lowPassY1;
    private double _lowPassY2;
    private double _dePopperLow;
    private double _dePopperGain = 1d;
    private double _dePopperWet;
    private double _noiseFloor = 0.0005d;
    private double _noiseSuppressionEnvelope;
    private double _noiseSuppressionGain = 1d;
    private double _noiseSuppressionWet;
    private double _echoEnvelope;
    private double _echoRecentPeak;
    private double _echoReducerGain = 1d;
    private double _echoReducerWet;
    private double _previousDeEsserInput;
    private double _previousDeEsserOutput;
    private double _deEsserBand;
    private double _deEsserDetectorEnvelope;
    private double _previousBreathInput;
    private double _previousBreathHigh;
    private double _breathBand;
    private double _breathDetectorEnvelope;
    private double _breathBodyEnvelope;
    private double _breathGain = 1d;
    private double _breathWet;
    private double _previousPresenceInput;
    private double _previousPresenceHigh;
    private double _presenceBand;
    private double _presenceGuardGain = 1d;
    private double _presenceWet;
    private double _saturationWet;
    private double _envelope;
    private double _compressorRmsEnvelope;
    private double _compressorSidechainLow;
    private double _compressorGain = 1d;
    private double _compressorWet;
    private double _lastGainReductionDb;
    private double _deEsserGain = 1d;
    private double _deEsserWet;
    private double _gateDetectorEnvelope;
    private double _gateOpenness = 1;
    private double _gateWet;
    private int _gateHoldSamplesRemaining;
    private double _expanderDetectorEnvelope;
    private double _expanderGain = 1;
    private double _expanderWet;
    private int _expanderHoldSamplesRemaining;
    private double _limiterEnvelopeReductionDb;
    private double _limiterGain = 1d;
    private double _limiterLookaheadPeak;
    private double _limiterWet;
    private double _limiterSoftClipWet;
    private double _inputTrimGain = 1d;
    private double _makeupGain = 1d;
    private double _inputTrimTargetGain = 1d;
    private double _makeupTargetGain = 1d;
    private double _levelSmoothingCoefficient;
    private bool _highPassEnabled;
    private bool _humRemovalEnabled;
    private bool _notchFilterEnabled;
    private bool _parametricEqEnabled;
    private bool _shelfEqEnabled;
    private bool _lowPassEnabled;
    private bool _dePopperEnabled;
    private bool _noiseGateEnabled;
    private bool _expanderEnabled;
    private bool _noiseSuppressionEnabled;
    private bool _echoReducerEnabled;
    private bool _compressorEnabled;
    private bool _deEsserEnabled;
    private bool _breathReducerEnabled;
    private bool _presenceEnhancerEnabled;
    private bool _saturationEnabled;
    private bool _limiterEnabled;
    private bool _limiterSoftClipEnabled;
    private double _dcBlockerCoefficient;
    private double _limiterCeiling = 1d;
    private double _highPassB0 = 1d;
    private double _highPassWetCoefficient;
    private double _highPassB1;
    private double _highPassB2;
    private double _highPassA1;
    private double _highPassA2;
    private double _lastHighPassFrequencyHz;
    private double _humNotchB0 = 1d;
    private double _humRemovalWetCoefficient;
    private double _humNotchB1;
    private double _humNotchB2;
    private double _humNotchA1;
    private double _humNotchA2;
    private double _humHarmonicNotchB0 = 1d;
    private double _humHarmonicNotchB1;
    private double _humHarmonicNotchB2;
    private double _humHarmonicNotchA1;
    private double _humHarmonicNotchA2;
    private double _lastHumRemovalFrequencyHz;
    private double _manualNotchB0 = 1d;
    private double _manualNotchWetCoefficient;
    private double _manualNotchB1;
    private double _manualNotchB2;
    private double _manualNotchA1;
    private double _manualNotchA2;
    private double _manualNotchMix = 1d;
    private double _lastNotchFilterFrequencyHz;
    private double _lastNotchFilterDepthDb = double.NaN;
    private double _lastNotchFilterQ;
    private double _parametricEqB0 = 1d;
    private double _parametricEqWetCoefficient;
    private double _parametricEqB1;
    private double _parametricEqB2;
    private double _parametricEqA1;
    private double _parametricEqA2;
    private double _lastParametricEqFrequencyHz;
    private double _lastParametricEqGainDb = double.NaN;
    private double _lastParametricEqQ;
    private double _shelfEqWetCoefficient;
    private double _lowShelfB0 = 1d;
    private double _lowShelfB1;
    private double _lowShelfB2;
    private double _lowShelfA1;
    private double _lowShelfA2;
    private double _highShelfB0 = 1d;
    private double _highShelfB1;
    private double _highShelfB2;
    private double _highShelfA1;
    private double _highShelfA2;
    private double _lastLowShelfFrequencyHz;
    private double _lastLowShelfGainDb = double.NaN;
    private double _lastHighShelfFrequencyHz;
    private double _lastHighShelfGainDb = double.NaN;
    private double _lowPassB0 = 1d;
    private double _lowPassWetCoefficient;
    private double _lowPassB1;
    private double _lowPassB2;
    private double _lowPassA1;
    private double _lowPassA2;
    private double _lastLowPassFrequencyHz;
    private double _saturationWetCoefficient;
    private double _saturationDrive = 1d;
    private double _saturationDenominator = Math.Tanh(1d);
    private double _saturationMakeup = 1d;
    private double _dePopperAlpha;
    private double _dePopperWetCoefficient;
    private double _dePopperAttackCoefficient;
    private double _dePopperReleaseCoefficient;
    private double _dePopperThresholdDb;
    private double _dePopperAmountDb;
    private double _noiseGateThreshold;
    private double _noiseGateRange;
    private double _noiseGateDetectorAttackCoefficient;
    private double _noiseGateDetectorReleaseCoefficient;
    private double _noiseGateAttackCoefficient;
    private double _noiseGateReleaseCoefficient;
    private double _noiseGateWetCoefficient;
    private int _noiseGateHoldSamples;
    private double _expanderThresholdDb;
    private double _expanderDetectorAttackCoefficient;
    private double _expanderDetectorReleaseCoefficient;
    private double _expanderAttackCoefficient;
    private double _expanderReleaseCoefficient;
    private double _expanderWetCoefficient;
    private double _expanderMinimumGain;
    private double _expanderRangeDb;
    private double _expanderRatio;
    private int _expanderHoldSamples;
    private double _noiseSuppressionDepth;
    private double _noiseSuppressionNoiseFloorMultiplier;
    private double _noiseSuppressionAttackCoefficient;
    private double _noiseSuppressionReleaseCoefficient;
    private double _noiseSuppressionWetCoefficient;
    private double _echoReducerMaximumReduction;
    private double _echoReducerTailWindow;
    private double _echoReducerAttackCoefficient;
    private double _echoReducerReleaseCoefficient;
    private double _echoReducerWetCoefficient;
    private double _compressorAttackCoefficient;
    private double _compressorReleaseCoefficient;
    private double _compressorRmsCoefficient;
    private double _compressorSidechainLowPassCoefficient;
    private double _compressorWetCoefficient;
    private double _compressorThresholdDb;
    private double _compressorRatio;
    private double _compressorKneeDb;
    private double _deEsserAttackCoefficient;
    private double _deEsserReleaseCoefficient;
    private double _deEsserDetectorAttackCoefficient;
    private double _deEsserDetectorReleaseCoefficient;
    private double _deEsserWetCoefficient;
    private double _deEsserThresholdDb;
    private double _deEsserRangeDb;
    private double _deEsserSensitivity;
    private double _breathHighPassAlpha;
    private double _breathLowPassAlpha;
    private double _breathAttackCoefficient;
    private double _breathReleaseCoefficient;
    private double _breathDetectorAttackCoefficient;
    private double _breathDetectorReleaseCoefficient;
    private double _breathWetCoefficient;
    private double _breathMaximumReductionDb;
    private double _breathSensitivity;
    private double _presenceHighPassAlpha;
    private double _presenceLowPassAlpha;
    private double _presenceBlend;
    private double _presenceWetCoefficient;
    private double _deEsserHighPassAlpha;
    private double _deEsserLowPassAlpha;
    private double _limiterTargetCeiling;
    private double _limiterAttackCoefficient;
    private double _limiterCeilingCoefficient;
    private double _limiterReleaseCoefficient;
    private double _limiterWetCoefficient;
    private double _limiterSoftClipWetCoefficient;
    private double _limiterSoftClipKneeFactor = 0.882d;
    private double _limiterSoftClipCurve = 1.12d;
    private double _limiterSoftClipCurveDenominator = 0.807568916578614d;
    private int _limiterLookaheadTargetSamples;
    private int _limiterLookaheadSamples;
    private double[] _limiterLookaheadBuffer = [];
    private double[] _limiterLookaheadPeakBuffer = [];
    private int _limiterLookaheadWriteIndex;
    private int _limiterLookaheadPeakIndex = -1;
    private int _limiterLookaheadCount;
    private double _limiterPreviousDetectorSample;
    private int _coefficientSettingsRevision = -1;
    private int _coefficientSampleCount = -1;
    private readonly double[] _equalizerGainsDb = new double[GraphicEqualizerSettings.DefaultBandCount];
    private int _equalizerSettingsRevision = -1;

    public VoiceSampleProcessor(VoiceProcessorSettings settings, int sampleRate)
    {
        _settings = settings;
        _sampleRate = Math.Max(8000d, sampleRate);
        _graphicEqualizer = new GraphicEqualizerProcessor(_sampleRate);
        _naudioBiQuadFilterRack = new NAudioBiQuadFilterRack(_sampleRate);
        _naudioPitchShiftProcessor = new NAudioPitchShiftProcessor(_sampleRate);
        _naudioImpulseConvolutionProcessor = new NAudioImpulseConvolutionProcessor(_sampleRate);
        _naudioEnvelopeGeneratorProcessor = new NAudioEnvelopeGeneratorProcessor(_sampleRate);
        _naudioDmoEffectChain = new NAudioDmoEffectChain(_sampleRate);
    }

    public float[] Process(ReadOnlySpan<float> samples)
    {
        var processed = new float[samples.Length];
        Process(samples, processed);
        return processed;
    }

    public void Process(ReadOnlySpan<float> samples, Span<float> processed)
    {
        var maxGainReduction = 0d;
        var maxCompressorInputLevel = 0d;
        var gateOpennessSum = 0d;
        var sampleCount = Math.Min(samples.Length, processed.Length);
        UpdateBlockCoefficients(sampleCount);
        var inputTrimTargetGain = _inputTrimTargetGain;
        var makeupTargetGain = _makeupTargetGain;
        var levelSmoothingCoefficient = _levelSmoothingCoefficient;

        for (var i = 0; i < sampleCount; i++)
        {
            double sample = Math.Clamp(SanitizeAudioSample(samples[i]), -1d, 1d);
            sample = ApplyDcBlocker(sample);
            _inputTrimGain = SmoothLevelGain(_inputTrimGain, inputTrimTargetGain, levelSmoothingCoefficient);
            sample *= _inputTrimGain;
            sample = ApplyDePopper(sample);
            sample = ApplyHighPass(sample);
            sample = ApplyHumRemoval(sample);
            sample = ApplyManualNotch(sample);
            sample = _graphicEqualizer.TransformSample(sample);
            sample = ApplyParametricEq(sample);
            sample = ApplyShelfEq(sample);
            sample = _naudioBiQuadFilterRack.Transform(sample);
            sample = ApplySaturation(sample);
            sample = _naudioEnvelopeGeneratorProcessor.Transform(sample);
            sample = ApplyNoiseSuppression(sample);
            sample = ApplyExpander(sample);
            sample = ApplyNoiseGate(sample);
            sample = ApplyEchoReducer(sample);
            sample = ApplyCompressor(sample);
            sample = ApplyBreathReducer(sample);
            sample = ApplyDeEsser(sample);
            sample = ApplyPresenceEnhancer(sample);
            sample = ApplyLowPass(sample);
            maxGainReduction = Math.Max(maxGainReduction, _lastGainReductionDb);
            maxCompressorInputLevel = Math.Max(maxCompressorInputLevel, Math.Abs(sample));
            gateOpennessSum += _gateOpenness;
            _makeupGain = SmoothLevelGain(_makeupGain, makeupTargetGain, levelSmoothingCoefficient);
            sample *= _makeupGain;
            sample = ApplyLimiter(sample);
            sample = SanitizeAudioSample(sample);
            processed[i] = (float)ApplyOutputSafetyClip(sample);
        }

        _naudioPitchShiftProcessor.Transform(processed[..sampleCount]);
        _naudioImpulseConvolutionProcessor.Transform(processed[..sampleCount]);
        _naudioDmoEffectChain.Transform(processed[..sampleCount]);

        var telemetry = Telemetry;
        telemetry.InputTrimDb = Math.Abs(_settings.InputTrimDb);
        telemetry.HighPassActivityDb = 0d;
        telemetry.DePopperReductionDb = 0d;
        telemetry.NoiseGateReductionDb = 0d;
        telemetry.NoiseSuppressionReductionDb = 0d;
        telemetry.EchoReducerReductionDb = 0d;
        telemetry.CompressorGainReductionDb = maxGainReduction;
        telemetry.CompressorInputLevelDb = LinearToDb(maxCompressorInputLevel);
        telemetry.CompressorThresholdDb = _settings.CompressorThresholdDb;
        telemetry.CompressorActive = maxGainReduction > 0.1d;
        telemetry.GateOpenness = sampleCount == 0 ? 1d : gateOpennessSum / sampleCount;
        telemetry.GateOpen = sampleCount == 0 || gateOpennessSum / sampleCount > 0.55d;
        telemetry.DeEsserReductionDb = 0d;
        telemetry.PresenceBoostDb = 0d;
        telemetry.MakeupGainDb = Math.Abs(_settings.MakeupGainDb);
        telemetry.LimiterReductionDb = _limiterEnvelopeReductionDb;
        telemetry.GraphicEqualizerLatencySamples = GraphicEqualizerLatencySamples;
        telemetry.GraphicEqualizerLatencyMilliseconds = GraphicEqualizerLatencyMilliseconds;
        telemetry.LimiterLookaheadLatencySamples = LimiterLookaheadLatencySamples;
        telemetry.LimiterLookaheadLatencyMilliseconds = LimiterLookaheadLatencyMilliseconds;
    }

    public VoiceProcessingTelemetry Telemetry { get; private set; } = new();

    public int GraphicEqualizerLatencySamples => _graphicEqualizer.LatencySamples;

    public double GraphicEqualizerLatencyMilliseconds => _graphicEqualizer.LatencyMilliseconds;

    public int LimiterLookaheadLatencySamples => CalculateLimiterLookaheadTargetSamples();

    public double LimiterLookaheadLatencyMilliseconds => SamplesToMilliseconds(LimiterLookaheadLatencySamples);

    public int KnownDspAlgorithmicLatencySamples => GraphicEqualizerLatencySamples + LimiterLookaheadLatencySamples;

    public double KnownDspAlgorithmicLatencyMilliseconds => GraphicEqualizerLatencyMilliseconds + LimiterLookaheadLatencyMilliseconds;

    private void UpdateBlockCoefficients(int sampleCount)
    {
        var settingsRevision = _settings.SettingsRevision;
        if (settingsRevision == _coefficientSettingsRevision && sampleCount == _coefficientSampleCount)
        {
            UpdateEqualizerCoefficients(sampleCount);
            return;
        }

        _coefficientSettingsRevision = settingsRevision;
        _coefficientSampleCount = sampleCount;
        var dt = 1d / _sampleRate;
        _inputTrimTargetGain = DbToLinear(Math.Clamp(_settings.InputTrimDb, -18d, 18d));
        _makeupTargetGain = DbToLinear(Math.Clamp(_settings.MakeupGainDb, -12d, 18d));
        _levelSmoothingCoefficient = TimeCoefficient(6d);
        _highPassEnabled = _settings.HighPassEnabled;
        _humRemovalEnabled = _settings.HumRemovalEnabled;
        _notchFilterEnabled = _settings.NotchFilterEnabled && _settings.NotchFilterDepthDb > 0d;
        _parametricEqEnabled = _settings.ParametricEqEnabled && Math.Abs(_settings.ParametricEqGainDb) > 0.01d;
        _shelfEqEnabled = _settings.ShelfEqEnabled
            && (Math.Abs(_settings.LowShelfGainDb) > 0.01d || Math.Abs(_settings.HighShelfGainDb) > 0.01d);
        _lowPassEnabled = _settings.LowPassEnabled;
        _dePopperEnabled = _settings.DePopperEnabled && _settings.DePopperAmountDb > 0d;
        _noiseGateEnabled = _settings.NoiseGateEnabled;
        _expanderEnabled = _settings.ExpanderEnabled;
        _noiseSuppressionEnabled = _settings.NoiseSuppressionEnabled && _settings.NoiseSuppressionAmountDb > 0d;
        _echoReducerEnabled = _settings.EchoReducerEnabled && _settings.EchoReducerAmountDb > 0d;
        _compressorEnabled = _settings.CompressorEnabled;
        _deEsserEnabled = _settings.DeEsserEnabled && _settings.DeEsserAmountDb > 0d;
        _breathReducerEnabled = _settings.BreathReducerEnabled && _settings.BreathReducerAmountDb > 0d;
        _presenceEnhancerEnabled = _settings.PresenceEnhancerEnabled && _settings.PresenceEnhancerAmountDb > 0d;
        _saturationEnabled = _settings.SaturationEnabled && _settings.SaturationAmount > 0d;
        _dcBlockerCoefficient = Math.Exp(-2d * Math.PI * 10d / _sampleRate);
        _highPassWetCoefficient = TimeCoefficient(12d);
        _humRemovalWetCoefficient = TimeCoefficient(12d);
        _manualNotchWetCoefficient = TimeCoefficient(12d);
        _parametricEqWetCoefficient = TimeCoefficient(12d);
        _shelfEqWetCoefficient = TimeCoefficient(12d);
        _lowPassWetCoefficient = TimeCoefficient(12d);
        UpdateHighPassCoefficients();
        UpdateHumRemovalCoefficients();
        UpdateManualNotchCoefficients();
        UpdateParametricEqCoefficients();
        UpdateShelfEqCoefficients();
        UpdateLowPassCoefficients();

        var dePopperCutoffHz = Math.Clamp(_settings.DePopperFrequencyHz, 80d, 320d);
        _dePopperAlpha = 1d - Math.Exp(-2d * Math.PI * dePopperCutoffHz / _sampleRate);
        _dePopperWetCoefficient = TimeCoefficient(10d);
        _dePopperAttackCoefficient = TimeCoefficient(1.5d);
        _dePopperReleaseCoefficient = TimeCoefficient(80d);
        _dePopperThresholdDb = Math.Clamp(_settings.DePopperThresholdDb, -48d, -12d);
        _dePopperAmountDb = _settings.DePopperAmountDb;

        _noiseGateThreshold = DbToLinear(_settings.NoiseGateThresholdDb);
        _noiseGateRange = DbToLinear(-Math.Clamp(_settings.NoiseGateRangeDb, 6d, 60d));
        _noiseGateDetectorAttackCoefficient = TimeCoefficient(Math.Min(3d, Math.Max(0.5d, _settings.NoiseGateAttackMs * 0.5d)));
        _noiseGateDetectorReleaseCoefficient = TimeCoefficient(45d);
        _noiseGateAttackCoefficient = TimeCoefficient(_settings.NoiseGateAttackMs);
        _noiseGateReleaseCoefficient = TimeCoefficient(_settings.NoiseGateReleaseMs);
        _noiseGateWetCoefficient = TimeCoefficient(14d);
        _noiseGateHoldSamples = (int)(Math.Clamp(_settings.NoiseGateHoldMs, 0d, 500d) * _sampleRate / 1000d);

        _expanderThresholdDb = Math.Clamp(_settings.ExpanderThresholdDb, -70d, -15d);
        _expanderDetectorAttackCoefficient = TimeCoefficient(Math.Min(4d, Math.Max(0.5d, _settings.ExpanderAttackMs * 0.5d)));
        _expanderDetectorReleaseCoefficient = TimeCoefficient(65d);
        _expanderAttackCoefficient = TimeCoefficient(_settings.ExpanderAttackMs);
        _expanderReleaseCoefficient = TimeCoefficient(_settings.ExpanderReleaseMs);
        _expanderWetCoefficient = TimeCoefficient(18d);
        _expanderRangeDb = Math.Clamp(_settings.ExpanderRangeDb, 0d, 48d);
        _expanderRatio = Math.Clamp(_settings.ExpanderRatio, 1d, 6d);
        _expanderHoldSamples = (int)(Math.Clamp(_settings.ExpanderHoldMs, 0d, 500d) * _sampleRate / 1000d);
        _expanderMinimumGain = DbToLinear(-_expanderRangeDb);

        _noiseSuppressionDepth = DbToLinear(-_settings.NoiseSuppressionAmountDb);
        _noiseSuppressionNoiseFloorMultiplier = 11d - Math.Clamp(_settings.NoiseSuppressionSensitivity, 1d, 10d);
        _noiseSuppressionAttackCoefficient = TimeCoefficient(8d);
        _noiseSuppressionReleaseCoefficient = TimeCoefficient(90d);
        _noiseSuppressionWetCoefficient = TimeCoefficient(20d);
        _echoReducerMaximumReduction = DbToLinear(-_settings.EchoReducerAmountDb);
        var echoSensitivity = Math.Clamp(_settings.EchoReducerSensitivity, 1d, 10d);
        _echoReducerTailWindow = Math.Clamp(0.7d - echoSensitivity * 0.045d, 0.2d, 0.65d);
        _echoReducerAttackCoefficient = TimeCoefficient(12d);
        _echoReducerReleaseCoefficient = TimeCoefficient(160d);
        _echoReducerWetCoefficient = TimeCoefficient(22d);

        _compressorAttackCoefficient = TimeCoefficient(_settings.CompressorAttackMs);
        _compressorReleaseCoefficient = TimeCoefficient(_settings.CompressorReleaseMs);
        _compressorRmsCoefficient = TimeCoefficient(25d);
        _compressorSidechainLowPassCoefficient = 1d - Math.Exp(-2d * Math.PI * 135d / _sampleRate);
        _compressorWetCoefficient = TimeCoefficient(18d);
        _compressorThresholdDb = _settings.CompressorThresholdDb;
        _compressorRatio = Math.Max(1d, _settings.CompressorRatio);
        _compressorKneeDb = Math.Clamp(_settings.CompressorKneeDb, 0d, 18d);
        _deEsserAttackCoefficient = TimeCoefficient(2d);
        _deEsserReleaseCoefficient = TimeCoefficient(45d);
        _deEsserDetectorAttackCoefficient = TimeCoefficient(1.2d);
        _deEsserDetectorReleaseCoefficient = TimeCoefficient(32d);
        _deEsserWetCoefficient = TimeCoefficient(12d);
        _deEsserThresholdDb = Math.Clamp(_settings.DeEsserThresholdDb, -60d, -12d);
        _deEsserRangeDb = Math.Clamp(_settings.DeEsserRangeDb, 1d, 18d);
        _deEsserSensitivity = Math.Clamp(_settings.DeEsserAmountDb, 0d, 12d) / 12d;
        _breathAttackCoefficient = TimeCoefficient(3d);
        _breathReleaseCoefficient = TimeCoefficient(150d);
        _breathDetectorAttackCoefficient = TimeCoefficient(2d);
        _breathDetectorReleaseCoefficient = TimeCoefficient(120d);
        _breathWetCoefficient = TimeCoefficient(10d);
        _breathMaximumReductionDb = Math.Clamp(_settings.BreathReducerAmountDb, 0d, 18d);
        _breathSensitivity = Math.Clamp(_settings.BreathReducerSensitivity, 1d, 10d);

        var centerHz = Math.Clamp(_settings.PresenceEnhancerFrequencyHz, 1500d, 6500d);
        var widthHz = Math.Clamp(_settings.PresenceEnhancerWidthHz, 800d, 5000d);
        var presenceHighPassHz = Math.Max(300d, centerHz - widthHz / 2d);
        var presenceLowPassHz = Math.Min(9000d, centerHz + widthHz / 2d);
        var presenceHighPassRc = 1d / (2d * Math.PI * presenceHighPassHz);
        _presenceHighPassAlpha = presenceHighPassRc / (presenceHighPassRc + dt);
        _presenceLowPassAlpha = 1d - Math.Exp(-2d * Math.PI * presenceLowPassHz / _sampleRate);
        _presenceBlend = Math.Clamp(DbToLinear(_settings.PresenceEnhancerAmountDb) - 1d, 0d, 1.5d);
        _presenceWetCoefficient = TimeCoefficient(18d);

        var saturationAmount = Math.Clamp(_settings.SaturationAmount, 0d, 10d) / 10d;
        _saturationWetCoefficient = TimeCoefficient(18d);
        _saturationDrive = 1d + saturationAmount * 2.8d;
        _saturationDenominator = Math.Max(0.000001d, Math.Tanh(_saturationDrive));
        _saturationMakeup = 1d / (1d + saturationAmount * 0.9d);

        var deEsserCutoffHz = Math.Clamp(_settings.DeEsserFrequencyHz, 3500d, 10000d);
        var deEsserRc = 1d / (2d * Math.PI * deEsserCutoffHz);
        _deEsserHighPassAlpha = deEsserRc / (deEsserRc + dt);
        var deEsserLowPassHz = Math.Clamp(deEsserCutoffHz + 3500d, 6500d, Math.Min(14000d, _sampleRate * 0.45d));
        _deEsserLowPassAlpha = 1d - Math.Exp(-2d * Math.PI * deEsserLowPassHz / _sampleRate);
        const double breathHighPassHz = 2500d;
        var breathHighPassRc = 1d / (2d * Math.PI * breathHighPassHz);
        _breathHighPassAlpha = breathHighPassRc / (breathHighPassRc + dt);
        var breathLowPassHz = Math.Min(12000d, _sampleRate * 0.45d);
        _breathLowPassAlpha = 1d - Math.Exp(-2d * Math.PI * breathLowPassHz / _sampleRate);

        _limiterEnabled = _settings.LimiterEnabled;
        _limiterSoftClipEnabled = _settings.LimiterSoftClipEnabled;
        _limiterTargetCeiling = DbToLinear(Math.Clamp(_settings.LimiterCeilingDb, -18d, -0.1d));
        _limiterAttackCoefficient = TimeCoefficient(1d);
        _limiterCeilingCoefficient = TimeCoefficient(8d);
        _limiterReleaseCoefficient = TimeCoefficient(_settings.LimiterReleaseMs);
        _limiterWetCoefficient = TimeCoefficient(10d);
        _limiterLookaheadTargetSamples = CalculateLimiterLookaheadTargetSamples();
        var limiterSoftClipDriveDb = Math.Clamp(_settings.LimiterSoftClipDriveDb, 0d, 12d);
        _limiterSoftClipWetCoefficient = TimeCoefficient(12d);
        _limiterSoftClipKneeFactor = Math.Clamp(0.9d - limiterSoftClipDriveDb * 0.012d, 0.76d, 0.9d);
        _limiterSoftClipCurve = 1d + limiterSoftClipDriveDb * 0.08d;
        _limiterSoftClipCurveDenominator = Math.Tanh(_limiterSoftClipCurve);
        _naudioBiQuadFilterRack.UpdateFromSettings(_settings);
        _naudioPitchShiftProcessor.UpdateFromSettings(_settings);
        _naudioImpulseConvolutionProcessor.UpdateFromSettings(_settings);
        _naudioEnvelopeGeneratorProcessor.UpdateFromSettings(_settings);
        _naudioDmoEffectChain.UpdateFromSettings(_settings);
        UpdateEqualizerCoefficients(sampleCount);
    }

    private double ApplyHighPass(double sample)
    {
        if (!_highPassEnabled && _highPassWet <= 0d)
        {
            ResetHighPassState();
            return sample;
        }

        var output = FlushDenormal(_highPassB0 * sample + _highPassB1 * _highPassX1 + _highPassB2 * _highPassX2 - _highPassA1 * _highPassY1 - _highPassA2 * _highPassY2);
        _highPassX2 = _highPassX1;
        _highPassX1 = FlushDenormal(sample);
        _highPassY2 = _highPassY1;
        _highPassY1 = output;
        _previousHighPassInput = _highPassX1;
        _previousHighPassOutput = output;

        var targetWet = _highPassEnabled ? 1d : 0d;
        _highPassWet = FlushDenormal(_highPassWet + (targetWet - _highPassWet) * _highPassWetCoefficient);
        if (!_highPassEnabled && _highPassWet < 0.0001d)
        {
            _highPassWet = 0d;
            ResetHighPassState();
        }
        else if (_highPassEnabled && _highPassWet > 0.9999d)
        {
            _highPassWet = 1d;
        }

        return sample + (output - sample) * _highPassWet;
    }

    private double ApplyDcBlocker(double sample)
    {
        var output = FlushDenormal(sample - _previousDcBlockerInput + _dcBlockerCoefficient * _previousDcBlockerOutput);
        _previousDcBlockerInput = FlushDenormal(sample);
        _previousDcBlockerOutput = output;
        return output;
    }

    private void ResetHighPassState()
    {
        if (_previousHighPassInput == 0d
            && _previousHighPassOutput == 0d
            && _highPassX1 == 0d
            && _highPassX2 == 0d
            && _highPassY1 == 0d
            && _highPassY2 == 0d)
        {
            return;
        }

        _previousHighPassInput = 0d;
        _previousHighPassOutput = 0d;
        _highPassX1 = 0d;
        _highPassX2 = 0d;
        _highPassY1 = 0d;
        _highPassY2 = 0d;
    }

    private void UpdateHighPassCoefficients()
    {
        var frequencyHz = Math.Clamp(_settings.HighPassFrequencyHz, 20d, Math.Min(240d, _sampleRate * 0.45d));
        if (Math.Abs(frequencyHz - _lastHighPassFrequencyHz) < 0.01d)
        {
            return;
        }

        _lastHighPassFrequencyHz = frequencyHz;
        var omega = 2d * Math.PI * frequencyHz / _sampleRate;
        var sine = Math.Sin(omega);
        var cosine = Math.Cos(omega);
        const double q = 0.7071067811865476d;
        var alpha = sine / (2d * q);
        var b0 = (1d + cosine) / 2d;
        var b1 = -(1d + cosine);
        var b2 = (1d + cosine) / 2d;
        var a0 = 1d + alpha;
        var a1 = -2d * cosine;
        var a2 = 1d - alpha;

        _highPassB0 = b0 / a0;
        _highPassB1 = b1 / a0;
        _highPassB2 = b2 / a0;
        _highPassA1 = a1 / a0;
        _highPassA2 = a2 / a0;
    }

    private double ApplyHumRemoval(double sample)
    {
        if (!_humRemovalEnabled && _humRemovalWet <= 0d)
        {
            ResetHumRemovalState();
            return sample;
        }

        var notched = ApplyHumNotch(
            sample,
            _humNotchB0,
            _humNotchB1,
            _humNotchB2,
            _humNotchA1,
            _humNotchA2,
            ref _humNotchX1,
            ref _humNotchX2,
            ref _humNotchY1,
            ref _humNotchY2);
        notched = ApplyHumNotch(
            notched,
            _humHarmonicNotchB0,
            _humHarmonicNotchB1,
            _humHarmonicNotchB2,
            _humHarmonicNotchA1,
            _humHarmonicNotchA2,
            ref _humHarmonicNotchX1,
            ref _humHarmonicNotchX2,
            ref _humHarmonicNotchY1,
            ref _humHarmonicNotchY2);

        var targetWet = _humRemovalEnabled ? 1d : 0d;
        _humRemovalWet = FlushDenormal(_humRemovalWet + (targetWet - _humRemovalWet) * _humRemovalWetCoefficient);
        if (!_humRemovalEnabled && _humRemovalWet < 0.0001d)
        {
            _humRemovalWet = 0d;
            ResetHumRemovalState();
        }
        else if (_humRemovalEnabled && _humRemovalWet > 0.9999d)
        {
            _humRemovalWet = 1d;
        }

        return sample + (notched - sample) * _humRemovalWet;
    }

    private static double ApplyHumNotch(
        double sample,
        double b0,
        double b1,
        double b2,
        double a1,
        double a2,
        ref double x1,
        ref double x2,
        ref double y1,
        ref double y2)
    {
        var output = FlushDenormal(b0 * sample + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2);
        x2 = x1;
        x1 = FlushDenormal(sample);
        y2 = y1;
        y1 = output;
        return output;
    }

    private static double ApplyBiquad(
        double sample,
        double b0,
        double b1,
        double b2,
        double a1,
        double a2,
        ref double x1,
        ref double x2,
        ref double y1,
        ref double y2)
    {
        var output = FlushDenormal(b0 * sample + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2);
        x2 = x1;
        x1 = FlushDenormal(sample);
        y2 = y1;
        y1 = output;
        return output;
    }

    private void ResetHumRemovalState()
    {
        _humNotchX1 = 0d;
        _humNotchX2 = 0d;
        _humNotchY1 = 0d;
        _humNotchY2 = 0d;
        _humHarmonicNotchX1 = 0d;
        _humHarmonicNotchX2 = 0d;
        _humHarmonicNotchY1 = 0d;
        _humHarmonicNotchY2 = 0d;
    }

    private void UpdateHumRemovalCoefficients()
    {
        var frequencyHz = Math.Clamp(_settings.HumRemovalFrequencyHz, 45d, 65d);
        if (Math.Abs(frequencyHz - _lastHumRemovalFrequencyHz) < 0.01d)
        {
            return;
        }

        _lastHumRemovalFrequencyHz = frequencyHz;
        SetNotchCoefficients(
            frequencyHz,
            24d,
            out _humNotchB0,
            out _humNotchB1,
            out _humNotchB2,
            out _humNotchA1,
            out _humNotchA2);
        SetNotchCoefficients(
            frequencyHz * 2d,
            30d,
            out _humHarmonicNotchB0,
            out _humHarmonicNotchB1,
            out _humHarmonicNotchB2,
            out _humHarmonicNotchA1,
            out _humHarmonicNotchA2);
    }

    private void SetNotchCoefficients(
        double frequencyHz,
        double q,
        out double b0,
        out double b1,
        out double b2,
        out double a1,
        out double a2)
    {
        var clampedFrequency = Math.Clamp(frequencyHz, 20d, _sampleRate * 0.45d);
        var omega = 2d * Math.PI * clampedFrequency / _sampleRate;
        var sine = Math.Sin(omega);
        var cosine = Math.Cos(omega);
        var alpha = sine / (2d * Math.Max(1d, q));
        var a0 = 1d + alpha;

        b0 = 1d / a0;
        b1 = -2d * cosine / a0;
        b2 = 1d / a0;
        a1 = -2d * cosine / a0;
        a2 = (1d - alpha) / a0;
    }

    private double ApplyManualNotch(double sample)
    {
        if (!_notchFilterEnabled && _manualNotchWet <= 0d)
        {
            ResetManualNotchState();
            return sample;
        }

        var notched = ApplyHumNotch(
            sample,
            _manualNotchB0,
            _manualNotchB1,
            _manualNotchB2,
            _manualNotchA1,
            _manualNotchA2,
            ref _manualNotchX1,
            ref _manualNotchX2,
            ref _manualNotchY1,
            ref _manualNotchY2);
        var shaped = sample + (notched - sample) * _manualNotchMix;
        var targetWet = _notchFilterEnabled ? 1d : 0d;
        _manualNotchWet = FlushDenormal(_manualNotchWet + (targetWet - _manualNotchWet) * _manualNotchWetCoefficient);
        if (!_notchFilterEnabled && _manualNotchWet < 0.0001d)
        {
            _manualNotchWet = 0d;
            ResetManualNotchState();
        }
        else if (_notchFilterEnabled && _manualNotchWet > 0.9999d)
        {
            _manualNotchWet = 1d;
        }

        return sample + (shaped - sample) * _manualNotchWet;
    }

    private void ResetManualNotchState()
    {
        _manualNotchX1 = 0d;
        _manualNotchX2 = 0d;
        _manualNotchY1 = 0d;
        _manualNotchY2 = 0d;
    }

    private void UpdateManualNotchCoefficients()
    {
        var frequencyHz = Math.Clamp(_settings.NotchFilterFrequencyHz, 80d, Math.Min(12000d, _sampleRate * 0.45d));
        var depthDb = Math.Clamp(_settings.NotchFilterDepthDb, 0d, 36d);
        var q = Math.Clamp(_settings.NotchFilterQ, 2d, 60d);
        if (Math.Abs(frequencyHz - _lastNotchFilterFrequencyHz) < 0.01d
            && Math.Abs(depthDb - _lastNotchFilterDepthDb) < 0.001d
            && Math.Abs(q - _lastNotchFilterQ) < 0.001d)
        {
            return;
        }

        _lastNotchFilterFrequencyHz = frequencyHz;
        _lastNotchFilterDepthDb = depthDb;
        _lastNotchFilterQ = q;
        _manualNotchMix = Math.Clamp(depthDb / 36d, 0d, 1d);
        SetNotchCoefficients(
            frequencyHz,
            q,
            out _manualNotchB0,
            out _manualNotchB1,
            out _manualNotchB2,
            out _manualNotchA1,
            out _manualNotchA2);
    }

    private double ApplyLowPass(double sample)
    {
        if (!_lowPassEnabled && _lowPassWet <= 0d)
        {
            ResetLowPassState();
            return sample;
        }

        var output = FlushDenormal(_lowPassB0 * sample + _lowPassB1 * _lowPassX1 + _lowPassB2 * _lowPassX2 - _lowPassA1 * _lowPassY1 - _lowPassA2 * _lowPassY2);
        _lowPassX2 = _lowPassX1;
        _lowPassX1 = FlushDenormal(sample);
        _lowPassY2 = _lowPassY1;
        _lowPassY1 = output;

        var targetWet = _lowPassEnabled ? 1d : 0d;
        _lowPassWet = FlushDenormal(_lowPassWet + (targetWet - _lowPassWet) * _lowPassWetCoefficient);
        if (!_lowPassEnabled && _lowPassWet < 0.0001d)
        {
            _lowPassWet = 0d;
            ResetLowPassState();
        }
        else if (_lowPassEnabled && _lowPassWet > 0.9999d)
        {
            _lowPassWet = 1d;
        }

        return sample + (output - sample) * _lowPassWet;
    }

    private void ResetLowPassState()
    {
        if (_lowPassX1 == 0d
            && _lowPassX2 == 0d
            && _lowPassY1 == 0d
            && _lowPassY2 == 0d)
        {
            return;
        }

        _lowPassX1 = 0d;
        _lowPassX2 = 0d;
        _lowPassY1 = 0d;
        _lowPassY2 = 0d;
    }

    private void UpdateLowPassCoefficients()
    {
        var frequencyHz = Math.Clamp(_settings.LowPassFrequencyHz, 1000d, Math.Min(20000d, _sampleRate * 0.45d));
        if (Math.Abs(frequencyHz - _lastLowPassFrequencyHz) < 0.01d)
        {
            return;
        }

        _lastLowPassFrequencyHz = frequencyHz;
        var omega = 2d * Math.PI * frequencyHz / _sampleRate;
        var sine = Math.Sin(omega);
        var cosine = Math.Cos(omega);
        const double q = 0.7071067811865476d;
        var alpha = sine / (2d * q);
        var b0 = (1d - cosine) / 2d;
        var b1 = 1d - cosine;
        var b2 = (1d - cosine) / 2d;
        var a0 = 1d + alpha;
        var a1 = -2d * cosine;
        var a2 = 1d - alpha;

        _lowPassB0 = b0 / a0;
        _lowPassB1 = b1 / a0;
        _lowPassB2 = b2 / a0;
        _lowPassA1 = a1 / a0;
        _lowPassA2 = a2 / a0;
    }

    private double ApplyDePopper(double sample)
    {
        if (!_dePopperEnabled && _dePopperWet <= 0d)
        {
            _dePopperLow = 0d;
            _dePopperGain = 1d;
            return sample;
        }

        _dePopperLow = FlushDenormal(_dePopperLow + _dePopperAlpha * (sample - _dePopperLow));

        var low = _dePopperLow;
        var high = sample - low;
        var lowLevelDb = LinearToDb(Math.Abs(low));
        var targetGain = 1d;
        if (lowLevelDb > _dePopperThresholdDb)
        {
            var overThresholdDb = lowLevelDb - _dePopperThresholdDb;
            var reductionDb = Math.Min(_dePopperAmountDb, overThresholdDb * 0.9d);
            targetGain = DbToLinear(-reductionDb);
        }

        _dePopperGain = SmoothReductionGain(
            _dePopperGain,
            targetGain,
            _dePopperAttackCoefficient,
            _dePopperReleaseCoefficient);
        var processed = high + low * _dePopperGain;

        var targetWet = _dePopperEnabled ? 1d : 0d;
        _dePopperWet = FlushDenormal(_dePopperWet + (targetWet - _dePopperWet) * _dePopperWetCoefficient);
        if (!_dePopperEnabled && _dePopperWet < 0.0001d)
        {
            _dePopperWet = 0d;
            _dePopperLow = 0d;
            _dePopperGain = 1d;
        }
        else if (_dePopperEnabled && _dePopperWet > 0.9999d)
        {
            _dePopperWet = 1d;
        }

        return sample + (processed - sample) * _dePopperWet;
    }

    private void UpdateEqualizerCoefficients(int sampleCount)
    {
        var equalizerRevision = _settings.EqualizerRevision;
        if (equalizerRevision != _equalizerSettingsRevision)
        {
            _settings.CopyEqualizerGainsTo(_equalizerGainsDb);
            _equalizerSettingsRevision = equalizerRevision;
        }

        _graphicEqualizer.Update(_equalizerGainsDb, _equalizerSettingsRevision, sampleCount);
    }

    private double ApplyParametricEq(double sample)
    {
        if (!_parametricEqEnabled && _parametricEqWet <= 0d)
        {
            ResetParametricEqState();
            return sample;
        }

        var output = FlushDenormal(_parametricEqB0 * sample
            + _parametricEqB1 * _parametricEqX1
            + _parametricEqB2 * _parametricEqX2
            - _parametricEqA1 * _parametricEqY1
            - _parametricEqA2 * _parametricEqY2);
        _parametricEqX2 = _parametricEqX1;
        _parametricEqX1 = FlushDenormal(sample);
        _parametricEqY2 = _parametricEqY1;
        _parametricEqY1 = output;

        var targetWet = _parametricEqEnabled ? 1d : 0d;
        _parametricEqWet = FlushDenormal(_parametricEqWet + (targetWet - _parametricEqWet) * _parametricEqWetCoefficient);
        if (!_parametricEqEnabled && _parametricEqWet < 0.0001d)
        {
            _parametricEqWet = 0d;
            ResetParametricEqState();
        }
        else if (_parametricEqEnabled && _parametricEqWet > 0.9999d)
        {
            _parametricEqWet = 1d;
        }

        return sample + (output - sample) * _parametricEqWet;
    }

    private void ResetParametricEqState()
    {
        _parametricEqX1 = 0d;
        _parametricEqX2 = 0d;
        _parametricEqY1 = 0d;
        _parametricEqY2 = 0d;
    }

    private void UpdateParametricEqCoefficients()
    {
        var frequencyHz = Math.Clamp(_settings.ParametricEqFrequencyHz, 40d, Math.Min(16000d, _sampleRate * 0.45d));
        var gainDb = Math.Clamp(_settings.ParametricEqGainDb, -12d, 12d);
        var q = Math.Clamp(_settings.ParametricEqQ, 0.25d, 24d);
        if (Math.Abs(frequencyHz - _lastParametricEqFrequencyHz) < 0.01d
            && Math.Abs(gainDb - _lastParametricEqGainDb) < 0.001d
            && Math.Abs(q - _lastParametricEqQ) < 0.001d)
        {
            return;
        }

        _lastParametricEqFrequencyHz = frequencyHz;
        _lastParametricEqGainDb = gainDb;
        _lastParametricEqQ = q;
        var omega = 2d * Math.PI * frequencyHz / _sampleRate;
        var sine = Math.Sin(omega);
        var cosine = Math.Cos(omega);
        var alpha = sine / (2d * q);
        var amplitude = Math.Pow(10d, gainDb / 40d);
        var b0 = 1d + alpha * amplitude;
        var b1 = -2d * cosine;
        var b2 = 1d - alpha * amplitude;
        var a0 = 1d + alpha / amplitude;
        var a1 = -2d * cosine;
        var a2 = 1d - alpha / amplitude;

        _parametricEqB0 = b0 / a0;
        _parametricEqB1 = b1 / a0;
        _parametricEqB2 = b2 / a0;
        _parametricEqA1 = a1 / a0;
        _parametricEqA2 = a2 / a0;
    }

    private double ApplyShelfEq(double sample)
    {
        if (!_shelfEqEnabled && _shelfEqWet <= 0d)
        {
            ResetShelfEqState();
            return sample;
        }

        var shaped = ApplyBiquad(
            sample,
            _lowShelfB0,
            _lowShelfB1,
            _lowShelfB2,
            _lowShelfA1,
            _lowShelfA2,
            ref _lowShelfX1,
            ref _lowShelfX2,
            ref _lowShelfY1,
            ref _lowShelfY2);
        shaped = ApplyBiquad(
            shaped,
            _highShelfB0,
            _highShelfB1,
            _highShelfB2,
            _highShelfA1,
            _highShelfA2,
            ref _highShelfX1,
            ref _highShelfX2,
            ref _highShelfY1,
            ref _highShelfY2);

        var targetWet = _shelfEqEnabled ? 1d : 0d;
        _shelfEqWet = FlushDenormal(_shelfEqWet + (targetWet - _shelfEqWet) * _shelfEqWetCoefficient);
        if (!_shelfEqEnabled && _shelfEqWet < 0.0001d)
        {
            _shelfEqWet = 0d;
            ResetShelfEqState();
        }
        else if (_shelfEqEnabled && _shelfEqWet > 0.9999d)
        {
            _shelfEqWet = 1d;
        }

        return sample + (shaped - sample) * _shelfEqWet;
    }

    private void ResetShelfEqState()
    {
        _lowShelfX1 = 0d;
        _lowShelfX2 = 0d;
        _lowShelfY1 = 0d;
        _lowShelfY2 = 0d;
        _highShelfX1 = 0d;
        _highShelfX2 = 0d;
        _highShelfY1 = 0d;
        _highShelfY2 = 0d;
    }

    private void UpdateShelfEqCoefficients()
    {
        var lowFrequencyHz = Math.Clamp(_settings.LowShelfFrequencyHz, 40d, Math.Min(800d, _sampleRate * 0.45d));
        var lowGainDb = Math.Clamp(_settings.LowShelfGainDb, -12d, 12d);
        var highFrequencyHz = Math.Clamp(_settings.HighShelfFrequencyHz, 1500d, Math.Min(16000d, _sampleRate * 0.45d));
        var highGainDb = Math.Clamp(_settings.HighShelfGainDb, -12d, 12d);
        if (Math.Abs(lowFrequencyHz - _lastLowShelfFrequencyHz) >= 0.01d
            || Math.Abs(lowGainDb - _lastLowShelfGainDb) >= 0.001d)
        {
            _lastLowShelfFrequencyHz = lowFrequencyHz;
            _lastLowShelfGainDb = lowGainDb;
            SetShelfCoefficients(
                lowFrequencyHz,
                lowGainDb,
                lowShelf: true,
                out _lowShelfB0,
                out _lowShelfB1,
                out _lowShelfB2,
                out _lowShelfA1,
                out _lowShelfA2);
        }

        if (Math.Abs(highFrequencyHz - _lastHighShelfFrequencyHz) >= 0.01d
            || Math.Abs(highGainDb - _lastHighShelfGainDb) >= 0.001d)
        {
            _lastHighShelfFrequencyHz = highFrequencyHz;
            _lastHighShelfGainDb = highGainDb;
            SetShelfCoefficients(
                highFrequencyHz,
                highGainDb,
                lowShelf: false,
                out _highShelfB0,
                out _highShelfB1,
                out _highShelfB2,
                out _highShelfA1,
                out _highShelfA2);
        }
    }

    private void SetShelfCoefficients(
        double frequencyHz,
        double gainDb,
        bool lowShelf,
        out double b0,
        out double b1,
        out double b2,
        out double a1,
        out double a2)
    {
        var amplitude = Math.Pow(10d, gainDb / 40d);
        var omega = 2d * Math.PI * frequencyHz / _sampleRate;
        var sine = Math.Sin(omega);
        var cosine = Math.Cos(omega);
        const double shelfSlope = 1d;
        var beta = Math.Sqrt(amplitude) * Math.Sqrt((amplitude + 1d / amplitude) * (1d / shelfSlope - 1d) + 2d);

        double rawB0;
        double rawB1;
        double rawB2;
        double rawA0;
        double rawA1;
        double rawA2;
        if (lowShelf)
        {
            rawB0 = amplitude * ((amplitude + 1d) - (amplitude - 1d) * cosine + beta * sine);
            rawB1 = 2d * amplitude * ((amplitude - 1d) - (amplitude + 1d) * cosine);
            rawB2 = amplitude * ((amplitude + 1d) - (amplitude - 1d) * cosine - beta * sine);
            rawA0 = (amplitude + 1d) + (amplitude - 1d) * cosine + beta * sine;
            rawA1 = -2d * ((amplitude - 1d) + (amplitude + 1d) * cosine);
            rawA2 = (amplitude + 1d) + (amplitude - 1d) * cosine - beta * sine;
        }
        else
        {
            rawB0 = amplitude * ((amplitude + 1d) + (amplitude - 1d) * cosine + beta * sine);
            rawB1 = -2d * amplitude * ((amplitude - 1d) + (amplitude + 1d) * cosine);
            rawB2 = amplitude * ((amplitude + 1d) + (amplitude - 1d) * cosine - beta * sine);
            rawA0 = (amplitude + 1d) - (amplitude - 1d) * cosine + beta * sine;
            rawA1 = 2d * ((amplitude - 1d) - (amplitude + 1d) * cosine);
            rawA2 = (amplitude + 1d) - (amplitude - 1d) * cosine - beta * sine;
        }

        b0 = rawB0 / rawA0;
        b1 = rawB1 / rawA0;
        b2 = rawB2 / rawA0;
        a1 = rawA1 / rawA0;
        a2 = rawA2 / rawA0;
    }

    private double ApplySaturation(double sample)
    {
        if (!_saturationEnabled && _saturationWet <= 0d)
        {
            return sample;
        }

        var driven = Math.Clamp(sample, -1d, 1d) * _saturationDrive;
        var saturated = Math.Tanh(driven) / _saturationDenominator * _saturationMakeup;
        var targetWet = _saturationEnabled ? 1d : 0d;
        _saturationWet = FlushDenormal(_saturationWet + (targetWet - _saturationWet) * _saturationWetCoefficient);
        if (!_saturationEnabled && _saturationWet < 0.0001d)
        {
            _saturationWet = 0d;
        }
        else if (_saturationEnabled && _saturationWet > 0.9999d)
        {
            _saturationWet = 1d;
        }

        return sample + (saturated - sample) * _saturationWet;
    }

    private double ApplyNoiseGate(double sample)
    {
        if (!_noiseGateEnabled)
        {
            if (_gateWet <= 0d)
            {
                ResetNoiseGateState();
                return sample;
            }

            _gateWet = FlushDenormal(_gateWet + (0d - _gateWet) * _noiseGateWetCoefficient);
            _gateOpenness = FlushDenormal(_gateOpenness + (1d - _gateOpenness) * _noiseGateAttackCoefficient);
            _gateOpenness = Math.Clamp(_gateOpenness, _noiseGateRange, 1d);
            if (_gateWet < 0.0001d)
            {
                _gateWet = 0d;
                ResetNoiseGateState();
            }

            return sample + (sample * _gateOpenness - sample) * _gateWet;
        }

        var level = TrackLevelEnvelope(
            ref _gateDetectorEnvelope,
            Math.Abs(sample),
            _noiseGateDetectorAttackCoefficient,
            _noiseGateDetectorReleaseCoefficient);
        double targetOpenness;
        if (level >= _noiseGateThreshold)
        {
            targetOpenness = 1d;
            _gateHoldSamplesRemaining = _noiseGateHoldSamples;
        }
        else if (_gateHoldSamplesRemaining > 0)
        {
            targetOpenness = 1d;
            _gateHoldSamplesRemaining--;
        }
        else
        {
            var openness = Math.Clamp(level / Math.Max(0.000001d, _noiseGateThreshold), 0d, 1d);
            targetOpenness = _noiseGateRange + (1d - _noiseGateRange) * openness * openness;
        }

        var coefficient = targetOpenness > _gateOpenness
            ? _noiseGateAttackCoefficient
            : _noiseGateReleaseCoefficient;
        _gateOpenness += (targetOpenness - _gateOpenness) * coefficient;
        _gateOpenness = Math.Clamp(_gateOpenness, _noiseGateRange, 1d);
        _gateWet = FlushDenormal(_gateWet + (1d - _gateWet) * _noiseGateWetCoefficient);
        if (_gateWet > 0.9999d)
        {
            _gateWet = 1d;
        }

        return sample + (sample * _gateOpenness - sample) * _gateWet;
    }

    private void ResetNoiseGateState()
    {
        if (_gateDetectorEnvelope == 0d
            && _gateOpenness == 1d
            && _gateWet == 0d
            && _gateHoldSamplesRemaining == 0)
        {
            return;
        }

        _gateDetectorEnvelope = 0d;
        _gateOpenness = 1d;
        _gateWet = 0d;
        _gateHoldSamplesRemaining = 0;
    }

    private double ApplyExpander(double sample)
    {
        if (!_expanderEnabled)
        {
            if (_expanderWet <= 0d)
            {
                ResetExpanderState();
                return sample;
            }

            _expanderWet = FlushDenormal(_expanderWet + (0d - _expanderWet) * _expanderWetCoefficient);
            _expanderGain = SmoothReductionGain(
                _expanderGain,
                1d,
                _expanderAttackCoefficient,
                _expanderReleaseCoefficient);

            if (_expanderWet < 0.0001d)
            {
                _expanderWet = 0d;
                ResetExpanderState();
            }

            return sample + (sample * Math.Clamp(_expanderGain, _expanderMinimumGain, 1d) - sample) * _expanderWet;
        }

        var level = TrackLevelEnvelope(
            ref _expanderDetectorEnvelope,
            Math.Abs(sample),
            _expanderDetectorAttackCoefficient,
            _expanderDetectorReleaseCoefficient);
        var levelDb = LinearToDb(level);
        double targetGain;
        if (levelDb >= _expanderThresholdDb)
        {
            targetGain = 1d;
            _expanderHoldSamplesRemaining = _expanderHoldSamples;
        }
        else if (_expanderHoldSamplesRemaining > 0)
        {
            targetGain = 1d;
            _expanderHoldSamplesRemaining--;
        }
        else
        {
            var distanceBelowThreshold = _expanderThresholdDb - levelDb;
            var reductionDb = Math.Min(_expanderRangeDb, distanceBelowThreshold * (_expanderRatio - 1d));
            targetGain = DbToLinear(-reductionDb);
        }

        var coefficient = targetGain > _expanderGain
            ? _expanderAttackCoefficient
            : _expanderReleaseCoefficient;
        _expanderGain += (targetGain - _expanderGain) * coefficient;
        var gain = Math.Clamp(_expanderGain, _expanderMinimumGain, 1d);
        _expanderWet = FlushDenormal(_expanderWet + (1d - _expanderWet) * _expanderWetCoefficient);
        if (_expanderWet > 0.9999d)
        {
            _expanderWet = 1d;
        }

        return sample + (sample * gain - sample) * _expanderWet;
    }

    private void ResetExpanderState()
    {
        if (_expanderDetectorEnvelope == 0d
            && _expanderGain == 1d
            && _expanderWet == 0d
            && _expanderHoldSamplesRemaining == 0)
        {
            return;
        }

        _expanderDetectorEnvelope = 0d;
        _expanderGain = 1d;
        _expanderWet = 0d;
        _expanderHoldSamplesRemaining = 0;
    }

    private static double TrackLevelEnvelope(ref double envelope, double level, double attackCoefficient, double releaseCoefficient)
    {
        var coefficient = level > envelope ? attackCoefficient : releaseCoefficient;
        envelope = FlushDenormal(envelope + (level - envelope) * coefficient);
        return envelope;
    }

    private double ApplyNoiseSuppression(double sample)
    {
        if (!_noiseSuppressionEnabled)
        {
            if (_noiseSuppressionWet <= 0d)
            {
                ResetNoiseSuppressionState();
                return sample;
            }

            TrackNoiseFloor(Math.Abs(sample));
            _noiseSuppressionWet = FlushDenormal(_noiseSuppressionWet + (0d - _noiseSuppressionWet) * _noiseSuppressionWetCoefficient);
            _noiseSuppressionGain = SmoothReductionGain(
                _noiseSuppressionGain,
                1d,
                _noiseSuppressionAttackCoefficient,
                _noiseSuppressionReleaseCoefficient);

            if (_noiseSuppressionWet < 0.0001d)
            {
                _noiseSuppressionWet = 0d;
                ResetNoiseSuppressionState();
            }

            return sample + (sample * _noiseSuppressionGain - sample) * _noiseSuppressionWet;
        }

        var instantLevel = Math.Abs(sample);
        _noiseSuppressionEnvelope = instantLevel > _noiseSuppressionEnvelope
            ? _noiseSuppressionEnvelope + (instantLevel - _noiseSuppressionEnvelope) * 0.12d
            : _noiseSuppressionEnvelope + (instantLevel - _noiseSuppressionEnvelope) * 0.004d;
        _noiseSuppressionEnvelope = FlushDenormal(_noiseSuppressionEnvelope);

        var level = _noiseSuppressionEnvelope;
        TrackNoiseFloor(level);

        var noiseThreshold = Math.Max(0.000001d, _noiseFloor * _noiseSuppressionNoiseFloorMultiplier);
        var targetGain = 1d;
        if (level < noiseThreshold)
        {
            var openness = Math.Clamp(level / noiseThreshold, 0d, 1d);
            targetGain = _noiseSuppressionDepth + (1d - _noiseSuppressionDepth) * openness * openness;
        }

        _noiseSuppressionGain = SmoothReductionGain(
            _noiseSuppressionGain,
            targetGain,
            _noiseSuppressionAttackCoefficient,
            _noiseSuppressionReleaseCoefficient);
        _noiseSuppressionWet = FlushDenormal(_noiseSuppressionWet + (1d - _noiseSuppressionWet) * _noiseSuppressionWetCoefficient);
        if (_noiseSuppressionWet > 0.9999d)
        {
            _noiseSuppressionWet = 1d;
        }

        return sample + (sample * _noiseSuppressionGain - sample) * _noiseSuppressionWet;
    }

    private void ResetNoiseSuppressionState()
    {
        if (_noiseSuppressionEnvelope == 0d
            && _noiseSuppressionGain == 1d
            && _noiseSuppressionWet == 0d)
        {
            return;
        }

        _noiseSuppressionEnvelope = 0d;
        _noiseSuppressionGain = 1d;
        _noiseSuppressionWet = 0d;
    }

    private void TrackNoiseFloor(double level)
    {
        if (level < _noiseFloor * 3d)
        {
            _noiseFloor += (level - _noiseFloor) * 0.002d;
        }
        else
        {
            _noiseFloor += (level - _noiseFloor) * 0.00002d;
        }

        _noiseFloor = Math.Clamp(FlushDenormal(_noiseFloor), 0.0000001d, 0.25d);
    }

    private double ApplyEchoReducer(double sample)
    {
        if (!_echoReducerEnabled)
        {
            if (_echoReducerWet <= 0d)
            {
                ResetEchoReducerState();
                return sample;
            }

            TrackEchoEnvelope(Math.Abs(sample));
            _echoReducerWet = FlushDenormal(_echoReducerWet + (0d - _echoReducerWet) * _echoReducerWetCoefficient);
            _echoReducerGain = SmoothReductionGain(
                _echoReducerGain,
                1d,
                _echoReducerAttackCoefficient,
                _echoReducerReleaseCoefficient);

            if (_echoReducerWet < 0.0001d)
            {
                _echoReducerWet = 0d;
                ResetEchoReducerState();
            }

            return sample + (sample * _echoReducerGain - sample) * _echoReducerWet;
        }

        TrackEchoEnvelope(Math.Abs(sample));

        var targetGain = 1d;
        if (_echoRecentPeak >= 0.0005d && _echoEnvelope <= _echoRecentPeak * _echoReducerTailWindow)
        {
            var tailRatio = Math.Clamp(_echoEnvelope / Math.Max(0.000001d, _echoRecentPeak * _echoReducerTailWindow), 0d, 1d);
            targetGain = _echoReducerMaximumReduction + (1d - _echoReducerMaximumReduction) * tailRatio;
        }

        _echoReducerGain = SmoothReductionGain(
            _echoReducerGain,
            targetGain,
            _echoReducerAttackCoefficient,
            _echoReducerReleaseCoefficient);
        _echoReducerWet = FlushDenormal(_echoReducerWet + (1d - _echoReducerWet) * _echoReducerWetCoefficient);
        if (_echoReducerWet > 0.9999d)
        {
            _echoReducerWet = 1d;
        }

        return sample + (sample * _echoReducerGain - sample) * _echoReducerWet;
    }

    private void TrackEchoEnvelope(double level)
    {
        _echoEnvelope = level > _echoEnvelope
            ? _echoEnvelope + (level - _echoEnvelope) * 0.08d
            : _echoEnvelope + (level - _echoEnvelope) * 0.004d;
        _echoEnvelope = FlushDenormal(_echoEnvelope);
        _echoRecentPeak = Math.Max(_echoRecentPeak * 0.9995d, _echoEnvelope);
        _echoRecentPeak = FlushDenormal(_echoRecentPeak);
    }

    private void ResetEchoReducerState()
    {
        if (_echoEnvelope == 0d
            && _echoRecentPeak == 0d
            && _echoReducerGain == 1d
            && _echoReducerWet == 0d)
        {
            return;
        }

        _echoEnvelope = 0d;
        _echoRecentPeak = 0d;
        _echoReducerGain = 1d;
        _echoReducerWet = 0d;
    }

    private double ApplyCompressor(double sample)
    {
        if (!_compressorEnabled)
        {
            if (_compressorWet <= 0d)
            {
                _lastGainReductionDb = 0d;
                ResetCompressorState();
                return sample;
            }

            _compressorWet = FlushDenormal(_compressorWet + (0d - _compressorWet) * _compressorWetCoefficient);
            _compressorGain = SmoothReductionGain(
                _compressorGain,
                1d,
                _compressorAttackCoefficient,
                _compressorReleaseCoefficient);

            if (_compressorWet < 0.0001d)
            {
                _compressorWet = 0d;
                ResetCompressorState();
            }

            _lastGainReductionDb = _compressorWet > 0d
                ? Math.Max(0d, -LinearToDb(Math.Clamp(_compressorGain, 0.000001d, 1d))) * _compressorWet
                : 0d;
            return sample + (sample * Math.Clamp(_compressorGain, 0d, 1d) - sample) * _compressorWet;
        }

        var detectorSample = GetCompressorDetectorSample(sample);
        var level = Math.Abs(detectorSample);
        _envelope = level > _envelope
            ? _envelope + (level - _envelope) * _compressorAttackCoefficient
            : _envelope + (level - _envelope) * _compressorReleaseCoefficient;
        _envelope = FlushDenormal(_envelope);

        _compressorRmsEnvelope = FlushDenormal(_compressorRmsEnvelope + (level * level - _compressorRmsEnvelope) * _compressorRmsCoefficient);
        var rmsLevel = Math.Sqrt(Math.Max(0d, _compressorRmsEnvelope));
        var detectorLevel = Math.Max(rmsLevel, _envelope * 0.55d);

        var levelDb = LinearToDb(detectorLevel);
        var overThresholdDb = levelDb - _compressorThresholdDb;
        var kneeDb = _compressorKneeDb;
        var ratio = _compressorRatio;
        var gainDb = 0d;
        if (kneeDb <= 0d && overThresholdDb <= 0d)
        {
            gainDb = 0d;
        }
        else if (kneeDb > 0d && overThresholdDb > -kneeDb / 2d && overThresholdDb < kneeDb / 2d)
        {
            var kneePosition = overThresholdDb + kneeDb / 2d;
            gainDb = (1d / ratio - 1d) * kneePosition * kneePosition / (2d * kneeDb);
        }
        else if (overThresholdDb > 0d)
        {
            gainDb = overThresholdDb / ratio - overThresholdDb;
        }
        else
        {
            gainDb = 0d;
        }

        var targetGain = DbToLinear(gainDb);
        var gainCoefficient = targetGain < _compressorGain
            ? _compressorAttackCoefficient
            : _compressorReleaseCoefficient;
        _compressorGain = FlushDenormal(_compressorGain + (targetGain - _compressorGain) * gainCoefficient);
        _compressorGain = Math.Clamp(_compressorGain, 0d, 1d);
        _lastGainReductionDb = Math.Max(0d, -LinearToDb(_compressorGain));
        _compressorWet = FlushDenormal(_compressorWet + (1d - _compressorWet) * _compressorWetCoefficient);
        if (_compressorWet > 0.9999d)
        {
            _compressorWet = 1d;
        }

        return sample + (sample * _compressorGain - sample) * _compressorWet;
    }

    private void ResetCompressorState()
    {
        if (_envelope == 0d
            && _compressorRmsEnvelope == 0d
            && _compressorSidechainLow == 0d
            && _compressorGain == 1d
            && _compressorWet == 0d
            && _lastGainReductionDb == 0d)
        {
            return;
        }

        _envelope = 0d;
        _compressorRmsEnvelope = 0d;
        _compressorSidechainLow = 0d;
        _compressorGain = 1d;
        _compressorWet = 0d;
        _lastGainReductionDb = 0d;
    }

    private double GetCompressorDetectorSample(double sample)
    {
        _compressorSidechainLow = FlushDenormal(_compressorSidechainLow + (sample - _compressorSidechainLow) * _compressorSidechainLowPassCoefficient);
        var sidechainHigh = sample - _compressorSidechainLow;
        return sidechainHigh + sample * 0.35d;
    }

    private double ApplyBreathReducer(double sample)
    {
        if (!_breathReducerEnabled && _breathWet <= 0d)
        {
            ResetBreathReducerState();
            return sample;
        }

        var breathBand = ApplyBreathBand(sample);
        var body = sample - breathBand;
        var breathLevel = TrackLevelEnvelope(
            ref _breathDetectorEnvelope,
            Math.Abs(breathBand),
            _breathDetectorAttackCoefficient,
            _breathDetectorReleaseCoefficient);
        var bodyLevel = TrackLevelEnvelope(
            ref _breathBodyEnvelope,
            Math.Abs(body),
            _breathDetectorAttackCoefficient,
            _breathDetectorReleaseCoefficient);
        var breathRatio = breathLevel / Math.Max(0.000001d, breathLevel + bodyLevel);
        var ratioThreshold = Math.Clamp(0.58d - _breathSensitivity * 0.045d, 0.16d, 0.55d);
        var breathiness = Math.Clamp((breathRatio - ratioThreshold) / Math.Max(0.000001d, 1d - ratioThreshold), 0d, 1d);
        var overallDb = LinearToDb(breathLevel + bodyLevel);
        var quietFavor = Math.Clamp((-3d - overallDb) / 24d, 0.65d, 1d);
        var reductionDb = _breathMaximumReductionDb * breathiness * quietFavor;
        var targetGain = DbToLinear(-reductionDb);
        var gainCoefficient = targetGain < _breathGain
            ? _breathAttackCoefficient
            : _breathReleaseCoefficient;
        _breathGain = FlushDenormal(_breathGain + (targetGain - _breathGain) * gainCoefficient);
        _breathGain = Math.Clamp(_breathGain, 0d, 1d);
        var processed = body + breathBand * _breathGain;

        var targetWet = _breathReducerEnabled ? 1d : 0d;
        _breathWet = FlushDenormal(_breathWet + (targetWet - _breathWet) * _breathWetCoefficient);
        if (!_breathReducerEnabled && _breathWet < 0.0001d)
        {
            _breathWet = 0d;
            ResetBreathReducerState();
        }
        else if (_breathReducerEnabled && _breathWet > 0.9999d)
        {
            _breathWet = 1d;
        }

        return sample + (processed - sample) * _breathWet;
    }

    private double ApplyBreathBand(double sample)
    {
        var high = FlushDenormal(_breathHighPassAlpha * (_previousBreathHigh + sample - _previousBreathInput));
        _previousBreathInput = FlushDenormal(sample);
        _previousBreathHigh = high;
        _breathBand = FlushDenormal(_breathBand + _breathLowPassAlpha * (high - _breathBand));
        return _breathBand;
    }

    private void ResetBreathReducerState()
    {
        if (_previousBreathInput == 0d
            && _previousBreathHigh == 0d
            && _breathBand == 0d
            && _breathDetectorEnvelope == 0d
            && _breathBodyEnvelope == 0d
            && _breathGain == 1d
            && _breathWet == 0d)
        {
            return;
        }

        _previousBreathInput = 0d;
        _previousBreathHigh = 0d;
        _breathBand = 0d;
        _breathDetectorEnvelope = 0d;
        _breathBodyEnvelope = 0d;
        _breathGain = 1d;
        _breathWet = 0d;
    }

    private double ApplyDeEsser(double sample)
    {
        if (!_deEsserEnabled && _deEsserWet <= 0d)
        {
            ResetDeEsserState();
            return sample;
        }

        var sibilance = ApplyDeEsserBandPass(sample);
        var body = sample - sibilance;
        var sibilanceLevel = TrackLevelEnvelope(
            ref _deEsserDetectorEnvelope,
            Math.Abs(sibilance),
            _deEsserDetectorAttackCoefficient,
            _deEsserDetectorReleaseCoefficient);
        var sibilanceLevelDb = LinearToDb(sibilanceLevel);
        var reductionDb = 0d;
        if (sibilanceLevelDb > _deEsserThresholdDb)
        {
            var overThresholdDb = sibilanceLevelDb - _deEsserThresholdDb;
            reductionDb = Math.Min(_deEsserRangeDb, overThresholdDb * (0.35d + _deEsserSensitivity * 0.55d));
        }

        var targetGain = DbToLinear(-reductionDb);
        var gainCoefficient = targetGain < _deEsserGain
            ? _deEsserAttackCoefficient
            : _deEsserReleaseCoefficient;
        _deEsserGain = FlushDenormal(_deEsserGain + (targetGain - _deEsserGain) * gainCoefficient);
        _deEsserGain = Math.Clamp(_deEsserGain, 0d, 1d);
        var processed = body + sibilance * _deEsserGain;

        var targetWet = _deEsserEnabled ? 1d : 0d;
        _deEsserWet = FlushDenormal(_deEsserWet + (targetWet - _deEsserWet) * _deEsserWetCoefficient);
        if (!_deEsserEnabled && _deEsserWet < 0.0001d)
        {
            _deEsserWet = 0d;
            ResetDeEsserState();
        }
        else if (_deEsserEnabled && _deEsserWet > 0.9999d)
        {
            _deEsserWet = 1d;
        }

        return sample + (processed - sample) * _deEsserWet;
    }

    private double ApplyPresenceEnhancer(double sample)
    {
        if (!_presenceEnhancerEnabled && _presenceWet <= 0d)
        {
            ResetPresenceState();
            return sample;
        }

        var high = FlushDenormal(_presenceHighPassAlpha * (_previousPresenceHigh + sample - _previousPresenceInput));
        _previousPresenceInput = FlushDenormal(sample);
        _previousPresenceHigh = high;

        _presenceBand = FlushDenormal(_presenceBand + _presenceLowPassAlpha * (high - _presenceBand));
        var targetGuardGain = _deEsserEnabled
            ? Math.Clamp(0.65d + _deEsserGain * 0.35d, 0.65d, 1d)
            : 1d;
        var guardCoefficient = targetGuardGain < _presenceGuardGain
            ? _deEsserAttackCoefficient
            : _deEsserReleaseCoefficient;
        _presenceGuardGain = FlushDenormal(_presenceGuardGain + (targetGuardGain - _presenceGuardGain) * guardCoefficient);

        var processed = sample + _presenceBand * _presenceBlend * _presenceGuardGain;
        var targetWet = _presenceEnhancerEnabled ? 1d : 0d;
        _presenceWet = FlushDenormal(_presenceWet + (targetWet - _presenceWet) * _presenceWetCoefficient);
        if (!_presenceEnhancerEnabled && _presenceWet < 0.0001d)
        {
            _presenceWet = 0d;
            ResetPresenceState();
        }
        else if (_presenceEnhancerEnabled && _presenceWet > 0.9999d)
        {
            _presenceWet = 1d;
        }

        return sample + (processed - sample) * _presenceWet;
    }

    private void ResetPresenceState()
    {
        if (_previousPresenceInput == 0d
            && _previousPresenceHigh == 0d
            && _presenceBand == 0d
            && _presenceGuardGain == 1d
            && _presenceWet == 0d)
        {
            return;
        }

        _previousPresenceInput = 0d;
        _previousPresenceHigh = 0d;
        _presenceBand = 0d;
        _presenceGuardGain = 1d;
        _presenceWet = 0d;
    }

    private double ApplyDeEsserBandPass(double sample)
    {
        var output = FlushDenormal(_deEsserHighPassAlpha * (_previousDeEsserOutput + sample - _previousDeEsserInput));
        _previousDeEsserInput = FlushDenormal(sample);
        _previousDeEsserOutput = output;
        _deEsserBand = FlushDenormal(_deEsserBand + _deEsserLowPassAlpha * (output - _deEsserBand));
        return _deEsserBand;
    }

    private void ResetDeEsserState()
    {
        if (_previousDeEsserInput == 0d
            && _previousDeEsserOutput == 0d
            && _deEsserBand == 0d
            && _deEsserDetectorEnvelope == 0d
            && _deEsserGain == 1d
            && _deEsserWet == 0d)
        {
            return;
        }

        _previousDeEsserInput = 0d;
        _previousDeEsserOutput = 0d;
        _deEsserBand = 0d;
        _deEsserDetectorEnvelope = 0d;
        _deEsserGain = 1d;
        _deEsserWet = 0d;
    }

    private double ApplyLimiter(double sample)
    {
        var drySample = sample;
        if (!_limiterEnabled && _limiterWet <= 0d)
        {
            _limiterSoftClipWet = 0d;
            ResetLimiterState();
            return drySample;
        }

        var targetWet = _limiterEnabled ? 1d : 0d;
        _limiterWet = FlushDenormal(_limiterWet + (targetWet - _limiterWet) * _limiterWetCoefficient);
        if (!_limiterEnabled && _limiterWet < 0.0001d)
        {
            _limiterWet = 0d;
            _limiterSoftClipWet = 0d;
            ResetLimiterState();
            return drySample;
        }
        else if (_limiterEnabled && _limiterWet > 0.9999d)
        {
            _limiterWet = 1d;
        }

        _limiterCeiling += (_limiterTargetCeiling - _limiterCeiling) * _limiterCeilingCoefficient;
        var ceiling = Math.Clamp(_limiterCeiling, 0.001d, 1d);
        if (!_limiterEnabled)
        {
            ResetLimiterLookahead();
            _limiterGain = SmoothReductionGain(
                _limiterGain,
                1d,
                _limiterAttackCoefficient,
                _limiterReleaseCoefficient);
            _limiterSoftClipWet = FlushDenormal(_limiterSoftClipWet + (0d - _limiterSoftClipWet) * _limiterSoftClipWetCoefficient);
            if (_limiterSoftClipWet < 0.0001d)
            {
                _limiterSoftClipWet = 0d;
            }

            var bypassLimited = Math.Clamp(sample * Math.Clamp(_limiterGain, 0d, 1d), -ceiling, ceiling);
            var bypassReduction = Math.Max(0d, -LinearToDb(Math.Clamp(_limiterGain, 0.000001d, 1d))) * _limiterWet;
            _limiterEnvelopeReductionDb = bypassReduction > _limiterEnvelopeReductionDb
                ? bypassReduction
                : _limiterEnvelopeReductionDb + (bypassReduction - _limiterEnvelopeReductionDb) * _limiterReleaseCoefficient;
            _limiterEnvelopeReductionDb = FlushDenormal(_limiterEnvelopeReductionDb);
            return drySample + (bypassLimited - drySample) * _limiterWet;
        }

        var detectorPeak = CalculateLimiterDetectorPeak(sample);
        var desiredGain = detectorPeak > ceiling
            ? ceiling / Math.Max(0.000001d, detectorPeak)
            : 1d;
        var usingLookahead = false;
        if (_limiterEnabled && _limiterLookaheadTargetSamples > 0)
        {
            usingLookahead = true;
            if (!TryGetLimiterDelayedSample(sample, detectorPeak, _limiterLookaheadTargetSamples, out var delayedSample, out var lookaheadPeak))
            {
                return 0d;
            }

            desiredGain = lookaheadPeak > ceiling
                ? ceiling / Math.Max(0.000001d, lookaheadPeak)
                : 1d;
            sample = delayedSample;
        }
        else
        {
            ResetLimiterLookahead();
        }

        var gainCoefficient = desiredGain < _limiterGain
            ? _limiterAttackCoefficient
            : _limiterReleaseCoefficient;
        _limiterGain = usingLookahead && desiredGain < _limiterGain
            ? desiredGain
            : FlushDenormal(_limiterGain + (desiredGain - _limiterGain) * gainCoefficient);
        sample *= Math.Clamp(_limiterGain, 0d, 1d);

        var targetSoftClipWet = _limiterEnabled && _limiterSoftClipEnabled ? 1d : 0d;
        _limiterSoftClipWet = FlushDenormal(_limiterSoftClipWet + (targetSoftClipWet - _limiterSoftClipWet) * _limiterSoftClipWetCoefficient);
        if (targetSoftClipWet == 0d && _limiterSoftClipWet < 0.0001d)
        {
            _limiterSoftClipWet = 0d;
        }
        else if (targetSoftClipWet == 1d && _limiterSoftClipWet > 0.9999d)
        {
            _limiterSoftClipWet = 1d;
        }

        if (_limiterSoftClipWet > 0d)
        {
            var clipped = ApplyTransparentSoftClip(sample, ceiling);
            sample += (clipped - sample) * _limiterSoftClipWet;
        }

        var limited = Math.Clamp(sample, -ceiling, ceiling);
        var sampleAbs = Math.Abs(sample);
        var limitedAbs = Math.Abs(limited);
        var instantReduction = sampleAbs > limitedAbs
            ? Math.Clamp(20d * Math.Log10(sampleAbs / Math.Max(0.0000001d, limitedAbs)), 0d, 60d)
            : 0d;
        instantReduction *= _limiterWet;
        _limiterEnvelopeReductionDb = instantReduction > _limiterEnvelopeReductionDb
            ? instantReduction
            : _limiterEnvelopeReductionDb + (instantReduction - _limiterEnvelopeReductionDb) * _limiterReleaseCoefficient;
        _limiterEnvelopeReductionDb = FlushDenormal(_limiterEnvelopeReductionDb);
        return drySample + (limited - drySample) * _limiterWet;
    }

    private void ResetLimiterState()
    {
        ResetLimiterLookahead();
        _limiterPreviousDetectorSample = 0d;
        _limiterGain = 1d;
        _limiterEnvelopeReductionDb = 0d;
    }

    private double CalculateLimiterDetectorPeak(double sample)
    {
        var peak = Math.Max(Math.Abs(sample), Math.Abs(_limiterPreviousDetectorSample));
        _limiterPreviousDetectorSample = sample;
        return peak * DbToLinear(LimiterTruePeakDetectorMarginDb);
    }

    private double ApplyTransparentSoftClip(double sample, double ceiling)
    {
        var absoluteSample = Math.Abs(sample);
        if (absoluteSample <= 0d)
        {
            return sample;
        }

        var kneeStart = ceiling * _limiterSoftClipKneeFactor;
        if (absoluteSample <= kneeStart)
        {
            return sample;
        }

        var kneeDepth = Math.Max(0.000001d, ceiling - kneeStart);
        var overKnee = Math.Clamp((absoluteSample - kneeStart) / kneeDepth, 0d, 1.5d);
        var shaped = kneeStart + kneeDepth * Math.Tanh(overKnee * _limiterSoftClipCurve) / _limiterSoftClipCurveDenominator;
        return Math.CopySign(Math.Min(shaped, ceiling), sample);
    }

    private static double ApplyOutputSafetyClip(double sample)
    {
        const double kneeStart = 0.985d;
        var absoluteSample = Math.Abs(sample);
        if (absoluteSample <= kneeStart)
        {
            return sample;
        }

        var kneeDepth = 1d - kneeStart;
        var overKnee = Math.Clamp((absoluteSample - kneeStart) / kneeDepth, 0d, 8d);
        var shaped = kneeStart + kneeDepth * Math.Tanh(overKnee);
        return Math.CopySign(Math.Min(shaped, 1d), sample);
    }

    private void ResetLimiterLookahead()
    {
        if (_limiterLookaheadSamples == 0
            && _limiterLookaheadWriteIndex == 0
            && _limiterLookaheadPeakIndex == -1
            && _limiterLookaheadCount == 0
            && _limiterLookaheadPeak == 0d)
        {
            return;
        }

        Array.Clear(_limiterLookaheadBuffer);
        Array.Clear(_limiterLookaheadPeakBuffer);
        _limiterLookaheadPeak = 0d;
        _limiterLookaheadSamples = 0;
        _limiterLookaheadWriteIndex = 0;
        _limiterLookaheadPeakIndex = -1;
        _limiterLookaheadCount = 0;
    }

    private bool TryGetLimiterDelayedSample(double sample, double detectorPeak, int lookaheadSamples, out double delayedSample, out double lookaheadPeak)
    {
        var capacity = lookaheadSamples + 1;
        if (_limiterLookaheadSamples != lookaheadSamples
            || _limiterLookaheadBuffer.Length != capacity
            || _limiterLookaheadPeakBuffer.Length != capacity)
        {
            _limiterLookaheadBuffer = new double[capacity];
            _limiterLookaheadPeakBuffer = new double[capacity];
            Array.Fill(_limiterLookaheadBuffer, sample);
            Array.Fill(_limiterLookaheadPeakBuffer, detectorPeak);
            _limiterLookaheadSamples = lookaheadSamples;
            _limiterLookaheadWriteIndex = 0;
            _limiterLookaheadPeakIndex = 0;
            _limiterLookaheadCount = capacity;
            _limiterLookaheadPeak = detectorPeak;
        }

        var insertedIndex = _limiterLookaheadWriteIndex;
        _limiterLookaheadBuffer[insertedIndex] = sample;
        _limiterLookaheadPeakBuffer[insertedIndex] = detectorPeak;
        if (_limiterLookaheadPeakIndex < 0 || detectorPeak >= _limiterLookaheadPeak)
        {
            _limiterLookaheadPeak = detectorPeak;
            _limiterLookaheadPeakIndex = insertedIndex;
        }

        _limiterLookaheadWriteIndex++;
        if (_limiterLookaheadWriteIndex == capacity)
        {
            _limiterLookaheadWriteIndex = 0;
        }
        if (_limiterLookaheadCount < capacity)
        {
            _limiterLookaheadCount++;
        }

        if (_limiterLookaheadCount <= lookaheadSamples)
        {
            delayedSample = 0d;
            lookaheadPeak = _limiterLookaheadPeak;
            return false;
        }

        var delayedIndex = _limiterLookaheadWriteIndex;
        delayedSample = _limiterLookaheadBuffer[delayedIndex];
        lookaheadPeak = _limiterLookaheadPeak;
        _limiterLookaheadBuffer[delayedIndex] = 0d;
        _limiterLookaheadPeakBuffer[delayedIndex] = 0d;
        if (delayedIndex == _limiterLookaheadPeakIndex)
        {
            RecalculateLimiterLookaheadPeak();
        }

        return true;
    }

    private void RecalculateLimiterLookaheadPeak()
    {
        var peak = 0d;
        var peakIndex = -1;
        for (var i = 0; i < _limiterLookaheadPeakBuffer.Length; i++)
        {
            var bufferedSamplePeak = _limiterLookaheadPeakBuffer[i];
            if (peakIndex < 0 || bufferedSamplePeak > peak)
            {
                peakIndex = i;
            }

            peak = Math.Max(peak, bufferedSamplePeak);
        }

        _limiterLookaheadPeak = peak;
        _limiterLookaheadPeakIndex = peakIndex;
    }

    private double BlockTimeCoefficient(double milliseconds, int sampleCount)
    {
        var clampedMs = Math.Clamp(milliseconds, 0.1d, 2000d);
        var clampedSampleCount = Math.Max(1, sampleCount);
        return 1d - Math.Exp(-clampedSampleCount / (_sampleRate * clampedMs / 1000d));
    }

    private double TimeCoefficient(double milliseconds)
    {
        var clampedMs = Math.Clamp(milliseconds, 0.1d, 2000d);
        return 1d - Math.Exp(-1d / (_sampleRate * clampedMs / 1000d));
    }

    private int CalculateLimiterLookaheadTargetSamples()
    {
        return _settings.LimiterEnabled && _settings.LimiterLookaheadEnabled && _settings.LimiterLookaheadMs > 0d
            ? Math.Clamp((int)(_sampleRate * _settings.LimiterLookaheadMs / 1000d), 1, (int)(_sampleRate * 0.02d))
            : 0;
    }

    private double SamplesToMilliseconds(int samples)
    {
        return Math.Max(0, samples) * 1000d / _sampleRate;
    }

    private static double SmoothReductionGain(double currentGain, double targetGain, double attackCoefficient, double releaseCoefficient)
    {
        var coefficient = targetGain < currentGain
            ? attackCoefficient
            : releaseCoefficient;
        var gain = currentGain + (targetGain - currentGain) * coefficient;
        return Math.Clamp(FlushDenormal(gain), 0d, 1d);
    }

    private static double SmoothLevelGain(double currentGain, double targetGain, double coefficient)
    {
        var gain = FlushDenormal(currentGain + (targetGain - currentGain) * coefficient);
        return Math.Abs(gain - targetGain) < 0.000001d
            ? targetGain
            : gain;
    }

    private static double DbToLinear(double db)
    {
        return Math.Pow(10d, db / 20d);
    }

    private static double LinearToDb(double linear)
    {
        return 20d * Math.Log10(Math.Max(0.0000001d, linear));
    }

    private static double SanitizeAudioSample(double sample)
    {
        if (!double.IsFinite(sample))
        {
            return 0d;
        }

        return FlushDenormal(sample);
    }

    private static double FlushDenormal(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0d;
        }

        return Math.Abs(value) < DenormalThreshold ? 0d : value;
    }

}


