namespace PodcastWorkbench.Audio;

public sealed class VoiceSampleProcessor
{
    private const double SampleRate = 44100d;
    private readonly VoiceProcessorSettings _settings;
    private double _previousHighPassInput;
    private double _previousHighPassOutput;
    private double _dePopperLow;
    private double _noiseFloor = 0.0005d;
    private double _noiseSuppressionEnvelope;
    private double _echoEnvelope;
    private double _echoRecentPeak;
    private double _previousDeEsserInput;
    private double _previousDeEsserOutput;
    private double _previousPresenceInput;
    private double _previousPresenceHigh;
    private double _presenceBand;
    private double _envelope;
    private double _lastGainReductionDb;
    private double _gateOpenness = 1;
    private int _gateHoldSamplesRemaining;
    private double _expanderGain = 1;
    private int _expanderHoldSamplesRemaining;
    private double _limiterEnvelopeReductionDb;
    private double _limiterGain = 1d;
    private double _limiterLookaheadPeak;
    private int _limiterLookaheadSamples;
    private readonly Queue<double> _limiterLookaheadBuffer = new();

    public VoiceSampleProcessor(VoiceProcessorSettings settings)
    {
        _settings = settings;
    }

    public float[] Process(ReadOnlySpan<float> samples)
    {
        var processed = new float[samples.Length];
        var maxGainReduction = 0d;
        var maxCompressorInputLevelDb = -100d;
        var gateOpennessSum = 0d;
        var maxHighPassActivityDb = 0d;
        var maxDePopperReductionDb = 0d;
        var maxNoiseGateReductionDb = 0d;
        var maxNoiseSuppressionReductionDb = 0d;
        var maxEchoReducerReductionDb = 0d;
        var maxDeEsserReductionDb = 0d;
        var maxPresenceBoostDb = 0d;
        var maxLimiterReductionDb = 0d;

        for (var i = 0; i < samples.Length; i++)
        {
            double sample = samples[i];
            sample = ApplyInputTrim(sample);
            var before = sample;
            sample = ApplyDePopper(sample);
            maxDePopperReductionDb = Math.Max(maxDePopperReductionDb, ReductionDb(before, sample));
            before = sample;
            sample = ApplyHighPass(sample);
            maxHighPassActivityDb = Math.Max(maxHighPassActivityDb, ChangeDb(before, sample));
            before = sample;
            sample = ApplyNoiseSuppression(sample);
            maxNoiseSuppressionReductionDb = Math.Max(maxNoiseSuppressionReductionDb, ReductionDb(before, sample));
            before = sample;
            sample = ApplyExpander(sample);
            before = sample;
            sample = ApplyNoiseGate(sample);
            maxNoiseGateReductionDb = Math.Max(maxNoiseGateReductionDb, ReductionDb(before, sample));
            before = sample;
            sample = ApplyEchoReducer(sample);
            maxEchoReducerReductionDb = Math.Max(maxEchoReducerReductionDb, ReductionDb(before, sample));
            before = sample;
            sample = ApplyCompressor(sample);
            sample = ApplyDeEsser(sample);
            maxDeEsserReductionDb = Math.Max(maxDeEsserReductionDb, ReductionDb(before, sample));
            before = sample;
            sample = ApplyPresenceEnhancer(sample);
            maxPresenceBoostDb = Math.Max(maxPresenceBoostDb, BoostDb(before, sample));
            maxGainReduction = Math.Max(maxGainReduction, _lastGainReductionDb);
            maxCompressorInputLevelDb = Math.Max(maxCompressorInputLevelDb, LinearToDb(Math.Abs(sample)));
            gateOpennessSum += _gateOpenness;
            sample = ApplyMakeupGain(sample);
            before = sample;
            sample = ApplyLimiter(sample);
            maxLimiterReductionDb = Math.Max(maxLimiterReductionDb, ReductionDb(before, sample));
            processed[i] = (float)Math.Clamp(sample, -1d, 1d);
        }

        Telemetry = new VoiceProcessingTelemetry
        {
            InputTrimDb = Math.Abs(_settings.InputTrimDb),
            HighPassActivityDb = maxHighPassActivityDb,
            DePopperReductionDb = maxDePopperReductionDb,
            NoiseGateReductionDb = maxNoiseGateReductionDb,
            NoiseSuppressionReductionDb = maxNoiseSuppressionReductionDb,
            EchoReducerReductionDb = maxEchoReducerReductionDb,
            CompressorGainReductionDb = maxGainReduction,
            CompressorInputLevelDb = maxCompressorInputLevelDb,
            CompressorThresholdDb = _settings.CompressorThresholdDb,
            CompressorActive = maxGainReduction > 0.1d,
            GateOpenness = samples.Length == 0 ? 1d : gateOpennessSum / samples.Length,
            GateOpen = samples.Length == 0 || gateOpennessSum / samples.Length > 0.55d,
            DeEsserReductionDb = maxDeEsserReductionDb,
            PresenceBoostDb = maxPresenceBoostDb,
            MakeupGainDb = Math.Abs(_settings.MakeupGainDb),
            LimiterReductionDb = maxLimiterReductionDb
        };

        return processed;
    }

    public VoiceProcessingTelemetry Telemetry { get; private set; } = new();

    private double ApplyInputTrim(double sample)
    {
        return sample * DbToLinear(Math.Clamp(_settings.InputTrimDb, -18d, 18d));
    }

    private double ApplyHighPass(double sample)
    {
        if (!_settings.HighPassEnabled)
        {
            return sample;
        }

        var rc = 1d / (2d * Math.PI * Math.Max(20d, _settings.HighPassFrequencyHz));
        var dt = 1d / SampleRate;
        var alpha = rc / (rc + dt);
        var output = alpha * (_previousHighPassOutput + sample - _previousHighPassInput);
        _previousHighPassInput = sample;
        _previousHighPassOutput = output;
        return output;
    }

    private double ApplyDePopper(double sample)
    {
        if (!_settings.DePopperEnabled || _settings.DePopperAmountDb <= 0)
        {
            return sample;
        }

        var cutoffHz = Math.Clamp(_settings.DePopperFrequencyHz, 80d, 320d);
        var alpha = 1d - Math.Exp(-2d * Math.PI * cutoffHz / SampleRate);
        _dePopperLow += alpha * (sample - _dePopperLow);

        var low = _dePopperLow;
        var high = sample - low;
        var lowLevelDb = LinearToDb(Math.Abs(low));
        var thresholdDb = Math.Clamp(_settings.DePopperThresholdDb, -48d, -12d);
        if (lowLevelDb <= thresholdDb)
        {
            return sample;
        }

        var overThresholdDb = lowLevelDb - thresholdDb;
        var reductionDb = Math.Min(_settings.DePopperAmountDb, overThresholdDb * 0.9d);
        return high + low * DbToLinear(-reductionDb);
    }

    private double ApplyNoiseGate(double sample)
    {
        if (!_settings.NoiseGateEnabled)
        {
            _gateOpenness = 1d;
            _gateHoldSamplesRemaining = 0;
            return sample;
        }

        var threshold = DbToLinear(_settings.NoiseGateThresholdDb);
        var level = Math.Abs(sample);
        var gateRange = DbToLinear(-Math.Clamp(_settings.NoiseGateRangeDb, 6d, 60d));
        double targetOpenness;
        if (level >= threshold)
        {
            targetOpenness = 1d;
            _gateHoldSamplesRemaining = (int)(Math.Clamp(_settings.NoiseGateHoldMs, 0d, 500d) * SampleRate / 1000d);
        }
        else if (_gateHoldSamplesRemaining > 0)
        {
            targetOpenness = 1d;
            _gateHoldSamplesRemaining--;
        }
        else
        {
            var openness = Math.Clamp(level / Math.Max(0.000001d, threshold), 0d, 1d);
            targetOpenness = gateRange + (1d - gateRange) * openness * openness;
        }

        var coefficient = targetOpenness > _gateOpenness
            ? TimeCoefficient(_settings.NoiseGateAttackMs)
            : TimeCoefficient(_settings.NoiseGateReleaseMs);
        _gateOpenness += (targetOpenness - _gateOpenness) * coefficient;
        _gateOpenness = Math.Clamp(_gateOpenness, gateRange, 1d);
        return sample * _gateOpenness;
    }

    private double ApplyExpander(double sample)
    {
        if (!_settings.ExpanderEnabled)
        {
            _expanderGain = 1d;
            _expanderHoldSamplesRemaining = 0;
            return sample;
        }

        var levelDb = LinearToDb(Math.Abs(sample));
        var thresholdDb = Math.Clamp(_settings.ExpanderThresholdDb, -70d, -15d);
        double targetGain;
        if (levelDb >= thresholdDb)
        {
            targetGain = 1d;
            _expanderHoldSamplesRemaining = (int)(Math.Clamp(_settings.ExpanderHoldMs, 0d, 500d) * SampleRate / 1000d);
        }
        else if (_expanderHoldSamplesRemaining > 0)
        {
            targetGain = 1d;
            _expanderHoldSamplesRemaining--;
        }
        else
        {
            var distanceBelowThreshold = thresholdDb - levelDb;
            var ratio = Math.Clamp(_settings.ExpanderRatio, 1d, 6d);
            var reductionDb = Math.Min(Math.Clamp(_settings.ExpanderRangeDb, 0d, 48d), distanceBelowThreshold * (ratio - 1d));
            targetGain = DbToLinear(-reductionDb);
        }

        var coefficient = targetGain > _expanderGain
            ? TimeCoefficient(_settings.ExpanderAttackMs)
            : TimeCoefficient(_settings.ExpanderReleaseMs);
        _expanderGain += (targetGain - _expanderGain) * coefficient;
        return sample * Math.Clamp(_expanderGain, DbToLinear(-Math.Clamp(_settings.ExpanderRangeDb, 0d, 48d)), 1d);
    }

    private double ApplyNoiseSuppression(double sample)
    {
        if (!_settings.NoiseSuppressionEnabled || _settings.NoiseSuppressionAmountDb <= 0)
        {
            return sample;
        }

        var instantLevel = Math.Abs(sample);
        _noiseSuppressionEnvelope = instantLevel > _noiseSuppressionEnvelope
            ? _noiseSuppressionEnvelope + (instantLevel - _noiseSuppressionEnvelope) * 0.12d
            : _noiseSuppressionEnvelope + (instantLevel - _noiseSuppressionEnvelope) * 0.004d;

        var level = _noiseSuppressionEnvelope;
        if (level < _noiseFloor * 3d)
        {
            _noiseFloor += (level - _noiseFloor) * 0.002d;
        }
        else
        {
            _noiseFloor += (level - _noiseFloor) * 0.00002d;
        }

        var sensitivity = Math.Clamp(_settings.NoiseSuppressionSensitivity, 1d, 10d);
        var noiseThreshold = Math.Max(0.000001d, _noiseFloor * (11d - sensitivity));
        if (level >= noiseThreshold)
        {
            return sample;
        }

        var depth = DbToLinear(-_settings.NoiseSuppressionAmountDb);
        var openness = Math.Clamp(level / noiseThreshold, 0d, 1d);
        var gain = depth + (1d - depth) * openness * openness;
        return sample * gain;
    }

    private double ApplyEchoReducer(double sample)
    {
        if (!_settings.EchoReducerEnabled || _settings.EchoReducerAmountDb <= 0)
        {
            _echoEnvelope = Math.Abs(sample);
            _echoRecentPeak = Math.Max(_echoRecentPeak * 0.999d, _echoEnvelope);
            return sample;
        }

        var level = Math.Abs(sample);
        _echoEnvelope = level > _echoEnvelope
            ? _echoEnvelope + (level - _echoEnvelope) * 0.08d
            : _echoEnvelope + (level - _echoEnvelope) * 0.004d;
        _echoRecentPeak = Math.Max(_echoRecentPeak * 0.9995d, _echoEnvelope);

        var sensitivity = Math.Clamp(_settings.EchoReducerSensitivity, 1d, 10d);
        var tailWindow = Math.Clamp(0.7d - sensitivity * 0.045d, 0.2d, 0.65d);
        if (_echoRecentPeak < 0.0005d || _echoEnvelope > _echoRecentPeak * tailWindow)
        {
            return sample;
        }

        var tailRatio = Math.Clamp(_echoEnvelope / Math.Max(0.000001d, _echoRecentPeak * tailWindow), 0d, 1d);
        var maxReduction = DbToLinear(-_settings.EchoReducerAmountDb);
        var gain = maxReduction + (1d - maxReduction) * tailRatio;
        return sample * gain;
    }

    private double ApplyCompressor(double sample)
    {
        if (!_settings.CompressorEnabled)
        {
            _lastGainReductionDb = 0d;
            return sample;
        }

        var level = Math.Abs(sample);
        var attack = TimeCoefficient(_settings.CompressorAttackMs);
        var release = TimeCoefficient(_settings.CompressorReleaseMs);
        _envelope = level > _envelope
            ? _envelope + (level - _envelope) * attack
            : _envelope + (level - _envelope) * release;

        var levelDb = LinearToDb(_envelope);
        var overThresholdDb = levelDb - _settings.CompressorThresholdDb;
        var kneeDb = Math.Clamp(_settings.CompressorKneeDb, 0d, 18d);
        if (kneeDb <= 0d && overThresholdDb <= 0d)
        {
            _lastGainReductionDb = 0d;
            return sample;
        }

        var ratio = Math.Max(1d, _settings.CompressorRatio);
        double gainDb;
        if (kneeDb > 0d && overThresholdDb > -kneeDb / 2d && overThresholdDb < kneeDb / 2d)
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
            _lastGainReductionDb = 0d;
            return sample;
        }

        _lastGainReductionDb = Math.Abs(gainDb);
        return sample * DbToLinear(gainDb);
    }

    private double ApplyDeEsser(double sample)
    {
        if (!_settings.DeEsserEnabled || _settings.DeEsserAmountDb <= 0)
        {
            return sample;
        }

        var sibilance = ApplyDeEsserHighPass(sample);
        var body = sample - sibilance;
        var sibilanceLevelDb = LinearToDb(Math.Abs(sibilance));
        var thresholdDb = Math.Clamp(_settings.DeEsserThresholdDb, -60d, -12d);
        if (sibilanceLevelDb <= thresholdDb)
        {
            return sample;
        }

        var overThresholdDb = sibilanceLevelDb - thresholdDb;
        var rangeDb = Math.Clamp(_settings.DeEsserRangeDb, 1d, 18d);
        var sensitivity = Math.Clamp(_settings.DeEsserAmountDb, 0d, 12d) / 12d;
        var reductionDb = Math.Min(rangeDb, overThresholdDb * (0.35d + sensitivity * 0.55d));
        return body + sibilance * DbToLinear(-reductionDb);
    }

    private double ApplyPresenceEnhancer(double sample)
    {
        if (!_settings.PresenceEnhancerEnabled || _settings.PresenceEnhancerAmountDb <= 0)
        {
            return sample;
        }

        var centerHz = Math.Clamp(_settings.PresenceEnhancerFrequencyHz, 1500d, 6500d);
        var widthHz = Math.Clamp(_settings.PresenceEnhancerWidthHz, 800d, 5000d);
        var highPassHz = Math.Max(300d, centerHz - widthHz / 2d);
        var lowPassHz = Math.Min(9000d, centerHz + widthHz / 2d);
        var highPassRc = 1d / (2d * Math.PI * highPassHz);
        var dt = 1d / SampleRate;
        var highPassAlpha = highPassRc / (highPassRc + dt);
        var high = highPassAlpha * (_previousPresenceHigh + sample - _previousPresenceInput);
        _previousPresenceInput = sample;
        _previousPresenceHigh = high;

        var lowPassAlpha = 1d - Math.Exp(-2d * Math.PI * lowPassHz / SampleRate);
        _presenceBand += lowPassAlpha * (high - _presenceBand);

        var blend = Math.Clamp(DbToLinear(_settings.PresenceEnhancerAmountDb) - 1d, 0d, 1.5d);
        return sample + _presenceBand * blend;
    }

    private double ApplyDeEsserHighPass(double sample)
    {
        var cutoffHz = Math.Clamp(_settings.DeEsserFrequencyHz, 3500d, 10000d);
        var rc = 1d / (2d * Math.PI * cutoffHz);
        var dt = 1d / SampleRate;
        var alpha = rc / (rc + dt);
        var output = alpha * (_previousDeEsserOutput + sample - _previousDeEsserInput);
        _previousDeEsserInput = sample;
        _previousDeEsserOutput = output;
        return output;
    }

    private double ApplyMakeupGain(double sample)
    {
        return sample * DbToLinear(_settings.MakeupGainDb);
    }

    private double ApplyLimiter(double sample)
    {
        if (!_settings.LimiterEnabled)
        {
            ResetLimiterLookahead();
            _limiterGain = 1d;
            return sample;
        }

        var ceiling = DbToLinear(_settings.LimiterCeilingDb);
        var desiredGain = Math.Abs(sample) > ceiling
            ? ceiling / Math.Max(0.000001d, Math.Abs(sample))
            : 1d;
        if (_settings.LimiterLookaheadEnabled && _settings.LimiterLookaheadMs > 0)
        {
            var lookaheadSamples = Math.Clamp((int)(SampleRate * _settings.LimiterLookaheadMs / 1000d), 1, (int)(SampleRate * 0.02d));
            if (_limiterLookaheadSamples != lookaheadSamples)
            {
                ResetLimiterLookahead();
                _limiterLookaheadSamples = lookaheadSamples;
            }

            _limiterLookaheadBuffer.Enqueue(sample);
            _limiterLookaheadPeak = Math.Max(_limiterLookaheadPeak, Math.Abs(sample));
            if (_limiterLookaheadBuffer.Count <= lookaheadSamples)
            {
                return 0d;
            }

            desiredGain = _limiterLookaheadPeak > ceiling
                ? ceiling / Math.Max(0.000001d, _limiterLookaheadPeak)
                : 1d;
            var delayedSample = _limiterLookaheadBuffer.Dequeue();
            sample = delayedSample;
            if (Math.Abs(delayedSample) >= _limiterLookaheadPeak - 0.000000001d)
            {
                RecalculateLimiterLookaheadPeak();
            }
        }
        else
        {
            ResetLimiterLookahead();
        }

        var gainCoefficient = desiredGain < _limiterGain
            ? TimeCoefficient(1d)
            : TimeCoefficient(_settings.LimiterReleaseMs);
        _limiterGain += (desiredGain - _limiterGain) * gainCoefficient;
        sample *= Math.Clamp(_limiterGain, 0d, 1d);

        if (_settings.LimiterSoftClipEnabled)
        {
            var drive = DbToLinear(Math.Clamp(_settings.LimiterSoftClipDriveDb, 0d, 12d));
            sample = Math.Tanh(sample * drive / Math.Max(0.000001d, ceiling)) * ceiling;
        }

        var limited = Math.Clamp(sample, -ceiling, ceiling);
        var instantReduction = ReductionDb(sample, limited);
        var coefficient = TimeCoefficient(_settings.LimiterReleaseMs);
        _limiterEnvelopeReductionDb = instantReduction > _limiterEnvelopeReductionDb
            ? instantReduction
            : _limiterEnvelopeReductionDb + (instantReduction - _limiterEnvelopeReductionDb) * coefficient;
        return limited;
    }

    private void ResetLimiterLookahead()
    {
        _limiterLookaheadBuffer.Clear();
        _limiterLookaheadPeak = 0d;
        _limiterLookaheadSamples = 0;
    }

    private void RecalculateLimiterLookaheadPeak()
    {
        var peak = 0d;
        foreach (var bufferedSample in _limiterLookaheadBuffer)
        {
            peak = Math.Max(peak, Math.Abs(bufferedSample));
        }

        _limiterLookaheadPeak = peak;
    }

    private static double TimeCoefficient(double milliseconds)
    {
        var clampedMs = Math.Clamp(milliseconds, 0.1d, 2000d);
        return 1d - Math.Exp(-1d / (SampleRate * clampedMs / 1000d));
    }

    private static double DbToLinear(double db)
    {
        return Math.Pow(10d, db / 20d);
    }

    private static double LinearToDb(double linear)
    {
        return 20d * Math.Log10(Math.Max(0.0000001d, linear));
    }

    private static double ReductionDb(double before, double after)
    {
        var beforeAbs = Math.Abs(before);
        var afterAbs = Math.Abs(after);
        if (beforeAbs <= afterAbs)
        {
            return 0d;
        }

        return Math.Clamp(LinearToDb(beforeAbs) - LinearToDb(afterAbs), 0d, 60d);
    }

    private static double BoostDb(double before, double after)
    {
        var beforeAbs = Math.Abs(before);
        var afterAbs = Math.Abs(after);
        if (afterAbs <= beforeAbs)
        {
            return 0d;
        }

        return Math.Clamp(LinearToDb(afterAbs) - LinearToDb(beforeAbs), 0d, 24d);
    }

    private static double ChangeDb(double before, double after)
    {
        return Math.Clamp(Math.Abs(LinearToDb(Math.Abs(after)) - LinearToDb(Math.Abs(before))), 0d, 60d);
    }
}


