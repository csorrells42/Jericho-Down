namespace JerichoDown.Audio;

public sealed class MixBusProcessor
{
    private const double NormalizedTargetRms = 0.125d;
    private const double MinimumNormalizeGain = 0.25d;
    private const double MaximumNormalizeGain = 4d;
    private double _normalizeGain = 1d;

    public void Reset()
    {
        _normalizeGain = 1d;
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output, MixBusSettings settings)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output buffer must be at least as long as the input buffer.", nameof(output));
        }

        var masterGain = PercentToLinear(settings.MasterVolumePercent);
        if (settings.AutoNormalizeEnabled)
        {
            masterGain *= UpdateNormalizeGain(input);
        }
        else
        {
            _normalizeGain += (1d - _normalizeGain) * 0.025d;
        }

        var limiterCeiling = DbToLinear(Math.Clamp(settings.LimiterCeilingDb, -24d, 0d));
        for (var i = 0; i < input.Length; i++)
        {
            var sample = SanitizeSample(input[i]) * masterGain;
            if (settings.LimiterEnabled)
            {
                sample = ApplyLimiter(sample, limiterCeiling);
            }

            output[i] = (float)Math.Clamp(sample, -1d, 1d);
        }
    }

    public static double PercentToLinear(double percent)
    {
        return Math.Clamp(double.IsFinite(percent) ? percent : 100d, 0d, 200d) / 100d;
    }

    private double UpdateNormalizeGain(ReadOnlySpan<float> input)
    {
        if (input.IsEmpty)
        {
            return _normalizeGain;
        }

        var sumSquares = 0d;
        var count = 0;
        foreach (var sample in input)
        {
            var sanitized = SanitizeSample(sample);
            sumSquares += sanitized * sanitized;
            count++;
        }

        if (count == 0)
        {
            return _normalizeGain;
        }

        var rms = Math.Sqrt(sumSquares / count);
        if (rms < 0.0001d)
        {
            _normalizeGain += (1d - _normalizeGain) * 0.01d;
            return _normalizeGain;
        }

        var targetGain = Math.Clamp(NormalizedTargetRms / rms, MinimumNormalizeGain, MaximumNormalizeGain);
        var smoothing = targetGain < _normalizeGain ? 0.18d : 0.035d;
        _normalizeGain += (targetGain - _normalizeGain) * smoothing;
        return _normalizeGain;
    }

    private static double ApplyLimiter(double sample, double ceiling)
    {
        var absolute = Math.Abs(sample);
        if (absolute <= ceiling)
        {
            return sample;
        }

        var overshoot = absolute - ceiling;
        var softened = ceiling + Math.Tanh(overshoot / Math.Max(0.0001d, 1d - ceiling)) * (1d - ceiling);
        return Math.CopySign(softened, sample);
    }

    private static double DbToLinear(double decibels)
    {
        return Math.Pow(10d, decibels / 20d);
    }

    private static double SanitizeSample(float sample)
    {
        return Math.Clamp(float.IsFinite(sample) ? sample : 0f, -1f, 1f);
    }
}

public sealed record MixBusSettings(
    double MasterVolumePercent,
    bool AutoNormalizeEnabled,
    bool LimiterEnabled,
    double LimiterCeilingDb)
{
    public static MixBusSettings Default { get; } = new(100d, true, true, -1d);
}

public sealed record MicrophoneLiveChannelSettings(
    int ChannelNumber,
    int DeviceNumber,
    InputChannelMode InputChannelMode,
    VoiceProcessorSettings ProcessorSettings,
    double VolumePercent,
    bool IsEnabled,
    bool IsMuted);
