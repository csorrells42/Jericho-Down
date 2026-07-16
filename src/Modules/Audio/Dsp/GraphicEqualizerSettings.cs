namespace JerichoDown.Modules.Audio.Dsp;

public sealed class GraphicEqualizerSettings
{
    public const int DefaultBandCount = 20;
    public const double DefaultBandQ = 1.35d;
    public const double DefaultMinimumGainDb = -12d;
    public const double DefaultMaximumGainDb = 12d;
    public const double DefaultGainSmoothingMilliseconds = 35d;
    public const int DefaultBypassDrainBlocks = 2;
    public const double DefaultInactiveGainThresholdDb = 0.01d;
    public const double DefaultGainSettleToleranceDb = 0.005d;
    public const double DefaultCoefficientReuseToleranceDb = 0.002d;

    private static readonly double[] BuiltInBandFrequenciesHz =
    [
        31d, 45d, 63d, 90d, 125d, 180d, 250d, 355d, 500d, 710d,
        1000d, 1400d, 2000d, 2800d, 4000d, 5600d, 8000d, 11200d, 16000d, 20000d
    ];

    private readonly double[] _bandFrequenciesHz;

    public GraphicEqualizerSettings(
        IReadOnlyList<double>? bandFrequenciesHz = null,
        double bandQ = DefaultBandQ,
        double minimumGainDb = DefaultMinimumGainDb,
        double maximumGainDb = DefaultMaximumGainDb,
        double gainSmoothingMilliseconds = DefaultGainSmoothingMilliseconds,
        int bypassDrainBlocks = DefaultBypassDrainBlocks,
        double inactiveGainThresholdDb = DefaultInactiveGainThresholdDb,
        double gainSettleToleranceDb = DefaultGainSettleToleranceDb,
        double coefficientReuseToleranceDb = DefaultCoefficientReuseToleranceDb)
    {
        _bandFrequenciesHz = NormalizeBands(bandFrequenciesHz ?? BuiltInBandFrequenciesHz);
        BandQ = Math.Clamp(bandQ, 0.1d, 20d);
        MinimumGainDb = Math.Min(minimumGainDb, maximumGainDb);
        MaximumGainDb = Math.Max(minimumGainDb, maximumGainDb);
        GainSmoothingMilliseconds = Math.Clamp(gainSmoothingMilliseconds, 0.1d, 2000d);
        BypassDrainBlocks = Math.Max(0, bypassDrainBlocks);
        InactiveGainThresholdDb = Math.Max(0d, inactiveGainThresholdDb);
        GainSettleToleranceDb = Math.Max(0.000001d, gainSettleToleranceDb);
        CoefficientReuseToleranceDb = Math.Max(0.000001d, coefficientReuseToleranceDb);
    }

    public static GraphicEqualizerSettings Default { get; } = new();

    public static IReadOnlyList<double> DefaultBandFrequenciesHz => BuiltInBandFrequenciesHz;

    public IReadOnlyList<double> BandFrequenciesHz => _bandFrequenciesHz;

    public int BandCount => _bandFrequenciesHz.Length;

    public double BandQ { get; }

    public double MinimumGainDb { get; }

    public double MaximumGainDb { get; }

    public double GainSmoothingMilliseconds { get; }

    public int BypassDrainBlocks { get; }

    public double InactiveGainThresholdDb { get; }

    public double GainSettleToleranceDb { get; }

    public double CoefficientReuseToleranceDb { get; }

    public double ClampGainDb(double gainDb) => Math.Clamp(gainDb, MinimumGainDb, MaximumGainDb);

    public double ClampBandFrequencyForSampleRate(int bandIndex, double sampleRate)
    {
        if (bandIndex < 0 || bandIndex >= _bandFrequenciesHz.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex));
        }

        var nyquist = Math.Max(4000d, sampleRate / 2d);
        return Math.Clamp(_bandFrequenciesHz[bandIndex], 20d, nyquist * 0.92d);
    }

    public double[] CreateFlatGains() => new double[_bandFrequenciesHz.Length];

    public double[] CreateBandAndMidpointFrequencies()
    {
        var points = new List<double>(_bandFrequenciesHz.Length * 2 - 1);
        for (var i = 0; i < _bandFrequenciesHz.Length; i++)
        {
            points.Add(_bandFrequenciesHz[i]);
            if (i < _bandFrequenciesHz.Length - 1)
            {
                points.Add(Math.Sqrt(_bandFrequenciesHz[i] * _bandFrequenciesHz[i + 1]));
            }
        }

        return points.ToArray();
    }

    public int FindNearestBandIndex(double frequencyHz)
    {
        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _bandFrequenciesHz.Length; i++)
        {
            var distance = Math.Abs(Math.Log(Math.Max(1d, frequencyHz) / _bandFrequenciesHz[i]));
            if (distance >= bestDistance)
            {
                continue;
            }

            bestIndex = i;
            bestDistance = distance;
        }

        return bestIndex;
    }

    public double CalculateGainSmoothingCoefficient(double sampleRate, int sampleCount)
    {
        var clampedSampleRate = Math.Max(8000d, sampleRate);
        var clampedSampleCount = Math.Max(1, sampleCount);
        return 1d - Math.Exp(-clampedSampleCount / (clampedSampleRate * GainSmoothingMilliseconds / 1000d));
    }

    private static double[] NormalizeBands(IReadOnlyList<double> bandFrequenciesHz)
    {
        var normalized = bandFrequenciesHz
            .Where(frequencyHz => double.IsFinite(frequencyHz) && frequencyHz > 0d)
            .Distinct()
            .OrderBy(frequencyHz => frequencyHz)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one valid EQ band frequency is required.", nameof(bandFrequenciesHz));
        }

        return normalized;
    }
}
