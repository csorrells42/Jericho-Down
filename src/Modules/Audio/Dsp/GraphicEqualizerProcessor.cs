namespace JerichoDown.Modules.Audio.Dsp;

public sealed class GraphicEqualizerProcessor
{
    private const double DenormalThreshold = 1.0E-20d;

    private readonly GraphicEqualizerSettings _settings;
    private readonly double _sampleRate;
    private readonly double[] _targetGainsDb;
    private readonly double[] _smoothedGainsDb;
    private readonly double[] _coefficientGainsDb;
    private readonly double[] _cosines;
    private readonly double[] _alphas;
    private readonly double[] _b0;
    private readonly double[] _b1;
    private readonly double[] _b2;
    private readonly double[] _a1;
    private readonly double[] _a2;
    private readonly double[] _x1;
    private readonly double[] _x2;
    private readonly double[] _y1;
    private readonly double[] _y2;
    private readonly bool[] _bandActive;
    private readonly int[] _activeBandIndices;
    private readonly int[] _bypassDrainBlocks;
    private int _activeBandCount;
    private int _targetRevision = int.MinValue;
    private bool _smoothingSettled;

    public GraphicEqualizerProcessor(double sampleRate)
        : this(GraphicEqualizerSettings.Default, sampleRate)
    {
    }

    public GraphicEqualizerProcessor(GraphicEqualizerSettings settings, double sampleRate)
    {
        _settings = settings;
        _sampleRate = Math.Max(8000d, sampleRate);
        _targetGainsDb = new double[settings.BandCount];
        _smoothedGainsDb = new double[settings.BandCount];
        _coefficientGainsDb = new double[settings.BandCount];
        _cosines = new double[settings.BandCount];
        _alphas = new double[settings.BandCount];
        _b0 = new double[settings.BandCount];
        _b1 = new double[settings.BandCount];
        _b2 = new double[settings.BandCount];
        _a1 = new double[settings.BandCount];
        _a2 = new double[settings.BandCount];
        _x1 = new double[settings.BandCount];
        _x2 = new double[settings.BandCount];
        _y1 = new double[settings.BandCount];
        _y2 = new double[settings.BandCount];
        _bandActive = new bool[settings.BandCount];
        _activeBandIndices = new int[settings.BandCount];
        _bypassDrainBlocks = new int[settings.BandCount];

        InitializeFrequencyCache();
    }

    public GraphicEqualizerSettings Settings => _settings;

    public int BandCount => _settings.BandCount;

    public int ActiveBandCount => _activeBandCount;

    public int LatencySamples => 0;

    public double LatencyMilliseconds => 0d;

    public IReadOnlyList<double> BandFrequenciesHz => _settings.BandFrequenciesHz;

    public void Update(ReadOnlySpan<double> gainsDb, int targetRevision, int sampleCount)
    {
        if (targetRevision != _targetRevision)
        {
            CopyTargetGains(gainsDb);
            _targetRevision = targetRevision;
            _smoothingSettled = false;
        }

        if (_smoothingSettled)
        {
            return;
        }

        var smoothingCoefficient = _settings.CalculateGainSmoothingCoefficient(_sampleRate, sampleCount);
        var allBandsSettled = true;
        _activeBandCount = 0;

        for (var i = 0; i < _settings.BandCount; i++)
        {
            var targetGainDb = _settings.ClampGainDb(_targetGainsDb[i]);
            _smoothedGainsDb[i] += (targetGainDb - _smoothedGainsDb[i]) * smoothingCoefficient;
            if (Math.Abs(targetGainDb - _smoothedGainsDb[i]) < _settings.GainSettleToleranceDb)
            {
                _smoothedGainsDb[i] = targetGainDb;
            }
            else
            {
                allBandsSettled = false;
            }

            var gainDb = _smoothedGainsDb[i];
            if (Math.Abs(gainDb) < _settings.InactiveGainThresholdDb)
            {
                DeactivateBand(i, ref allBandsSettled);
                continue;
            }

            _bypassDrainBlocks[i] = 0;
            if (_bandActive[i] && Math.Abs(gainDb - _coefficientGainsDb[i]) < _settings.CoefficientReuseToleranceDb)
            {
                _activeBandIndices[_activeBandCount++] = i;
                continue;
            }

            UpdateBandCoefficients(i, gainDb);
        }

        _smoothingSettled = allBandsSettled;
    }

    public void Process(ReadOnlySpan<float> samples, Span<float> processed)
    {
        var count = Math.Min(samples.Length, processed.Length);
        for (var i = 0; i < count; i++)
        {
            processed[i] = (float)TransformSample(samples[i]);
        }
    }

    public double TransformSample(double sample)
    {
        for (var activeBandIndex = 0; activeBandIndex < _activeBandCount; activeBandIndex++)
        {
            var i = _activeBandIndices[activeBandIndex];
            var output = _b0[i] * sample
                + _b1[i] * _x1[i]
                + _b2[i] * _x2[i]
                - _a1[i] * _y1[i]
                - _a2[i] * _y2[i];
            _x2[i] = _x1[i];
            _x1[i] = FlushDenormal(sample);
            _y2[i] = _y1[i];
            _y1[i] = FlushDenormal(output);
            sample = _y1[i];
        }

        return sample;
    }

    public double CalculateCurrentResponseDb(double frequencyHz)
    {
        if (_activeBandCount == 0)
        {
            return 0d;
        }

        var omega = 2d * Math.PI * Math.Clamp(frequencyHz, 1d, _sampleRate / 2d) / _sampleRate;
        var z1Real = Math.Cos(omega);
        var z1Imaginary = -Math.Sin(omega);
        var z2Real = Math.Cos(2d * omega);
        var z2Imaginary = -Math.Sin(2d * omega);
        var magnitude = 1d;

        for (var activeBandIndex = 0; activeBandIndex < _activeBandCount; activeBandIndex++)
        {
            var i = _activeBandIndices[activeBandIndex];
            var numeratorReal = _b0[i] + _b1[i] * z1Real + _b2[i] * z2Real;
            var numeratorImaginary = _b1[i] * z1Imaginary + _b2[i] * z2Imaginary;
            var denominatorReal = 1d + _a1[i] * z1Real + _a2[i] * z2Real;
            var denominatorImaginary = _a1[i] * z1Imaginary + _a2[i] * z2Imaginary;
            var numeratorMagnitude = Math.Sqrt(numeratorReal * numeratorReal + numeratorImaginary * numeratorImaginary);
            var denominatorMagnitude = Math.Sqrt(denominatorReal * denominatorReal + denominatorImaginary * denominatorImaginary);
            magnitude *= numeratorMagnitude / Math.Max(1.0E-12d, denominatorMagnitude);
        }

        return 20d * Math.Log10(Math.Max(1.0E-12d, magnitude));
    }

    private void CopyTargetGains(ReadOnlySpan<double> gainsDb)
    {
        for (var i = 0; i < _targetGainsDb.Length; i++)
        {
            _targetGainsDb[i] = i < gainsDb.Length ? _settings.ClampGainDb(gainsDb[i]) : 0d;
        }
    }

    private void DeactivateBand(int bandIndex, ref bool allBandsSettled)
    {
        var previousCoefficientGainDb = _coefficientGainsDb[bandIndex];
        _coefficientGainsDb[bandIndex] = 0d;
        _b0[bandIndex] = 1d;
        _b1[bandIndex] = 0d;
        _b2[bandIndex] = 0d;
        _a1[bandIndex] = 0d;
        _a2[bandIndex] = 0d;

        if (_bandActive[bandIndex])
        {
            if (_bypassDrainBlocks[bandIndex] <= 0 && Math.Abs(previousCoefficientGainDb) > _settings.CoefficientReuseToleranceDb)
            {
                _bypassDrainBlocks[bandIndex] = _settings.BypassDrainBlocks;
            }

            if (_bypassDrainBlocks[bandIndex] > 0)
            {
                _activeBandIndices[_activeBandCount++] = bandIndex;
                _bypassDrainBlocks[bandIndex]--;
                allBandsSettled = false;
                return;
            }

            ResetBandState(bandIndex);
        }

        _bandActive[bandIndex] = false;
    }

    private void UpdateBandCoefficients(int bandIndex, double gainDb)
    {
        _bandActive[bandIndex] = true;
        _activeBandIndices[_activeBandCount++] = bandIndex;
        _coefficientGainsDb[bandIndex] = gainDb;

        var cosine = _cosines[bandIndex];
        var alpha = _alphas[bandIndex];
        var amplitude = Math.Pow(10d, gainDb / 40d);
        var b0 = 1d + alpha * amplitude;
        var b1 = -2d * cosine;
        var b2 = 1d - alpha * amplitude;
        var a0 = 1d + alpha / amplitude;
        var a1 = -2d * cosine;
        var a2 = 1d - alpha / amplitude;

        _b0[bandIndex] = b0 / a0;
        _b1[bandIndex] = b1 / a0;
        _b2[bandIndex] = b2 / a0;
        _a1[bandIndex] = a1 / a0;
        _a2[bandIndex] = a2 / a0;
    }

    private void InitializeFrequencyCache()
    {
        for (var i = 0; i < _settings.BandCount; i++)
        {
            var frequencyHz = _settings.ClampBandFrequencyForSampleRate(i, _sampleRate);
            var omega = 2d * Math.PI * frequencyHz / _sampleRate;
            var sine = Math.Sin(omega);
            _cosines[i] = Math.Cos(omega);
            _alphas[i] = sine / (2d * _settings.BandQ);
        }
    }

    private void ResetBandState(int bandIndex)
    {
        _x1[bandIndex] = 0d;
        _x2[bandIndex] = 0d;
        _y1[bandIndex] = 0d;
        _y2[bandIndex] = 0d;
    }

    private static double FlushDenormal(double value) => Math.Abs(value) < DenormalThreshold ? 0d : value;
}
