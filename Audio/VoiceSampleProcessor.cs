namespace JerichoDown.Audio;

public sealed class VoiceSampleProcessor
{
    private const double DenormalThreshold = 1.0E-20d;
    private const double EqualizerQ = 1.35d;
    private const int EqualizerBypassDrainBlocks = 2;
    private const double LimiterTruePeakDetectorMarginDb = 0.6d;
    private static readonly double[] EqualizerFrequenciesHz =
    [
        31d, 45d, 63d, 90d, 125d, 180d, 250d, 355d, 500d, 710d,
        1000d, 1400d, 2000d, 2800d, 4000d, 5600d, 8000d, 11200d, 16000d, 20000d
    ];

    private readonly VoiceProcessorSettings _settings;
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
    private double _previousPresenceInput;
    private double _previousPresenceHigh;
    private double _presenceBand;
    private double _presenceGuardGain = 1d;
    private double _presenceWet;
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
    private bool _lowPassEnabled;
    private bool _dePopperEnabled;
    private bool _noiseGateEnabled;
    private bool _expanderEnabled;
    private bool _noiseSuppressionEnabled;
    private bool _echoReducerEnabled;
    private bool _compressorEnabled;
    private bool _deEsserEnabled;
    private bool _presenceEnhancerEnabled;
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
    private double _lowPassB0 = 1d;
    private double _lowPassWetCoefficient;
    private double _lowPassB1;
    private double _lowPassB2;
    private double _lowPassA1;
    private double _lowPassA2;
    private double _lastLowPassFrequencyHz;
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
    private readonly double[] _equalizerGainsDb = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerSmoothedGainsDb = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerCoefficientGainsDb = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerCosines = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerAlphas = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerB0 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerB1 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerB2 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerA1 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerA2 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerX1 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerX2 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerY1 = new double[EqualizerFrequenciesHz.Length];
    private readonly double[] _equalizerY2 = new double[EqualizerFrequenciesHz.Length];
    private readonly bool[] _equalizerBandActive = new bool[EqualizerFrequenciesHz.Length];
    private readonly int[] _equalizerActiveBandIndices = new int[EqualizerFrequenciesHz.Length];
    private readonly int[] _equalizerBypassDrainBlocks = new int[EqualizerFrequenciesHz.Length];
    private int _equalizerActiveBandCount;
    private int _equalizerSettingsRevision = -1;
    private bool _equalizerSmoothingSettled;

    public VoiceSampleProcessor(VoiceProcessorSettings settings, int sampleRate)
    {
        _settings = settings;
        _sampleRate = Math.Max(8000d, sampleRate);
        InitializeEqualizerFrequencyCache();
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
            sample = ApplyEqualizer(sample);
            sample = ApplyNoiseSuppression(sample);
            sample = ApplyExpander(sample);
            sample = ApplyNoiseGate(sample);
            sample = ApplyEchoReducer(sample);
            sample = ApplyCompressor(sample);
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
    }

    public VoiceProcessingTelemetry Telemetry { get; private set; } = new();

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
        _lowPassEnabled = _settings.LowPassEnabled;
        _dePopperEnabled = _settings.DePopperEnabled && _settings.DePopperAmountDb > 0d;
        _noiseGateEnabled = _settings.NoiseGateEnabled;
        _expanderEnabled = _settings.ExpanderEnabled;
        _noiseSuppressionEnabled = _settings.NoiseSuppressionEnabled && _settings.NoiseSuppressionAmountDb > 0d;
        _echoReducerEnabled = _settings.EchoReducerEnabled && _settings.EchoReducerAmountDb > 0d;
        _compressorEnabled = _settings.CompressorEnabled;
        _deEsserEnabled = _settings.DeEsserEnabled && _settings.DeEsserAmountDb > 0d;
        _presenceEnhancerEnabled = _settings.PresenceEnhancerEnabled && _settings.PresenceEnhancerAmountDb > 0d;
        _dcBlockerCoefficient = Math.Exp(-2d * Math.PI * 10d / _sampleRate);
        _highPassWetCoefficient = TimeCoefficient(12d);
        _lowPassWetCoefficient = TimeCoefficient(12d);
        UpdateHighPassCoefficients();
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

        var centerHz = Math.Clamp(_settings.PresenceEnhancerFrequencyHz, 1500d, 6500d);
        var widthHz = Math.Clamp(_settings.PresenceEnhancerWidthHz, 800d, 5000d);
        var presenceHighPassHz = Math.Max(300d, centerHz - widthHz / 2d);
        var presenceLowPassHz = Math.Min(9000d, centerHz + widthHz / 2d);
        var presenceHighPassRc = 1d / (2d * Math.PI * presenceHighPassHz);
        _presenceHighPassAlpha = presenceHighPassRc / (presenceHighPassRc + dt);
        _presenceLowPassAlpha = 1d - Math.Exp(-2d * Math.PI * presenceLowPassHz / _sampleRate);
        _presenceBlend = Math.Clamp(DbToLinear(_settings.PresenceEnhancerAmountDb) - 1d, 0d, 1.5d);
        _presenceWetCoefficient = TimeCoefficient(18d);

        var deEsserCutoffHz = Math.Clamp(_settings.DeEsserFrequencyHz, 3500d, 10000d);
        var deEsserRc = 1d / (2d * Math.PI * deEsserCutoffHz);
        _deEsserHighPassAlpha = deEsserRc / (deEsserRc + dt);
        var deEsserLowPassHz = Math.Clamp(deEsserCutoffHz + 3500d, 6500d, Math.Min(14000d, _sampleRate * 0.45d));
        _deEsserLowPassAlpha = 1d - Math.Exp(-2d * Math.PI * deEsserLowPassHz / _sampleRate);

        _limiterEnabled = _settings.LimiterEnabled;
        _limiterSoftClipEnabled = _settings.LimiterSoftClipEnabled;
        _limiterTargetCeiling = DbToLinear(Math.Clamp(_settings.LimiterCeilingDb, -18d, -0.1d));
        _limiterAttackCoefficient = TimeCoefficient(1d);
        _limiterCeilingCoefficient = TimeCoefficient(8d);
        _limiterReleaseCoefficient = TimeCoefficient(_settings.LimiterReleaseMs);
        _limiterWetCoefficient = TimeCoefficient(10d);
        _limiterLookaheadTargetSamples = _settings.LimiterLookaheadEnabled && _settings.LimiterLookaheadMs > 0d
            ? Math.Clamp((int)(_sampleRate * _settings.LimiterLookaheadMs / 1000d), 1, (int)(_sampleRate * 0.02d))
            : 0;
        var limiterSoftClipDriveDb = Math.Clamp(_settings.LimiterSoftClipDriveDb, 0d, 12d);
        _limiterSoftClipWetCoefficient = TimeCoefficient(12d);
        _limiterSoftClipKneeFactor = Math.Clamp(0.9d - limiterSoftClipDriveDb * 0.012d, 0.76d, 0.9d);
        _limiterSoftClipCurve = 1d + limiterSoftClipDriveDb * 0.08d;
        _limiterSoftClipCurveDenominator = Math.Tanh(_limiterSoftClipCurve);
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
            _equalizerSmoothingSettled = false;
        }

        if (_equalizerSmoothingSettled)
        {
            return;
        }

        var smoothingCoefficient = BlockTimeCoefficient(35d, sampleCount);
        var allBandsSettled = true;
        _equalizerActiveBandCount = 0;
        for (var i = 0; i < EqualizerFrequenciesHz.Length; i++)
        {
            var targetGainDb = Math.Clamp(_equalizerGainsDb[i], -12d, 12d);
            _equalizerSmoothedGainsDb[i] += (targetGainDb - _equalizerSmoothedGainsDb[i]) * smoothingCoefficient;
            if (Math.Abs(targetGainDb - _equalizerSmoothedGainsDb[i]) < 0.005d)
            {
                _equalizerSmoothedGainsDb[i] = targetGainDb;
            }
            else
            {
                allBandsSettled = false;
            }

            var gainDb = _equalizerSmoothedGainsDb[i];
            if (Math.Abs(gainDb) < 0.01d)
            {
                var previousCoefficientGainDb = _equalizerCoefficientGainsDb[i];
                _equalizerCoefficientGainsDb[i] = 0d;
                _equalizerB0[i] = 1d;
                _equalizerB1[i] = 0d;
                _equalizerB2[i] = 0d;
                _equalizerA1[i] = 0d;
                _equalizerA2[i] = 0d;

                if (_equalizerBandActive[i])
                {
                    if (_equalizerBypassDrainBlocks[i] <= 0 && Math.Abs(previousCoefficientGainDb) > 0.002d)
                    {
                        _equalizerBypassDrainBlocks[i] = EqualizerBypassDrainBlocks;
                    }

                    if (_equalizerBypassDrainBlocks[i] > 0)
                    {
                        _equalizerActiveBandIndices[_equalizerActiveBandCount++] = i;
                        _equalizerBypassDrainBlocks[i]--;
                        allBandsSettled = false;
                        continue;
                    }

                    ResetEqualizerBandState(i);
                }

                _equalizerBandActive[i] = false;
                continue;
            }

            _equalizerBypassDrainBlocks[i] = 0;
            if (_equalizerBandActive[i] && Math.Abs(gainDb - _equalizerCoefficientGainsDb[i]) < 0.002d)
            {
                _equalizerActiveBandIndices[_equalizerActiveBandCount++] = i;
                continue;
            }

            _equalizerBandActive[i] = true;
            _equalizerActiveBandIndices[_equalizerActiveBandCount++] = i;
            _equalizerCoefficientGainsDb[i] = gainDb;
            var cosine = _equalizerCosines[i];
            var alpha = _equalizerAlphas[i];
            var amplitude = Math.Pow(10d, gainDb / 40d);
            var b0 = 1d + alpha * amplitude;
            var b1 = -2d * cosine;
            var b2 = 1d - alpha * amplitude;
            var a0 = 1d + alpha / amplitude;
            var a1 = -2d * cosine;
            var a2 = 1d - alpha / amplitude;

            _equalizerB0[i] = b0 / a0;
            _equalizerB1[i] = b1 / a0;
            _equalizerB2[i] = b2 / a0;
            _equalizerA1[i] = a1 / a0;
            _equalizerA2[i] = a2 / a0;
        }

        _equalizerSmoothingSettled = allBandsSettled;
    }

    private void InitializeEqualizerFrequencyCache()
    {
        var nyquist = _sampleRate / 2d;
        for (var i = 0; i < EqualizerFrequenciesHz.Length; i++)
        {
            var frequencyHz = Math.Clamp(EqualizerFrequenciesHz[i], 20d, nyquist * 0.92d);
            var omega = 2d * Math.PI * frequencyHz / _sampleRate;
            var sine = Math.Sin(omega);
            _equalizerCosines[i] = Math.Cos(omega);
            _equalizerAlphas[i] = sine / (2d * EqualizerQ);
        }
    }

    private void ResetEqualizerBandState(int bandIndex)
    {
        _equalizerX1[bandIndex] = 0d;
        _equalizerX2[bandIndex] = 0d;
        _equalizerY1[bandIndex] = 0d;
        _equalizerY2[bandIndex] = 0d;
    }

    private double ApplyEqualizer(double sample)
    {
        for (var activeBandIndex = 0; activeBandIndex < _equalizerActiveBandCount; activeBandIndex++)
        {
            var i = _equalizerActiveBandIndices[activeBandIndex];

            var output = _equalizerB0[i] * sample
                + _equalizerB1[i] * _equalizerX1[i]
                + _equalizerB2[i] * _equalizerX2[i]
                - _equalizerA1[i] * _equalizerY1[i]
                - _equalizerA2[i] * _equalizerY2[i];
            _equalizerX2[i] = _equalizerX1[i];
            _equalizerX1[i] = FlushDenormal(sample);
            _equalizerY2[i] = _equalizerY1[i];
            _equalizerY1[i] = FlushDenormal(output);
            sample = _equalizerY1[i];
        }

        return sample;
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


