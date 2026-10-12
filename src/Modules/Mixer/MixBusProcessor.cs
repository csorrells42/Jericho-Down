using JerichoDown.Modules.Audio.Devices;
using JerichoDown.Modules.Audio.Dsp;

namespace JerichoDown.Modules.Mixer;

public sealed class MixBusProcessor
{
    private const double NormalizedTargetRms = 0.125d;
    private const double MinimumNormalizeGain = 0.25d;
    private const double MaximumNormalizeGain = 4d;
    private double _normalizeGain = 1d;

    public MixBusTelemetry LastTelemetry { get; private set; } = MixBusTelemetry.Silence;

    public void Reset()
    {
        _normalizeGain = 1d;
        LastTelemetry = MixBusTelemetry.Silence;
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

        var limiterCeiling = DbToLinear(Math.Clamp(
            double.IsFinite(settings.LimiterCeilingDb) ? settings.LimiterCeilingDb : -1d, -24d, 0d));
        var peak = 0d;
        var sumSquares = 0d;
        var sampleCount = 0;
        var limiterReductionDb = 0d;
        if (settings.OutputMode == MixBusOutputMode.Mono && input.Length >= 2)
        {
            var frameCount = input.Length / 2;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var leftIndex = frame * 2;
                var rightIndex = leftIndex + 1;
                var combined = (float)((SanitizeSample(input[leftIndex]) + SanitizeSample(input[rightIndex])) * 0.5d);
                var mono = (float)ProcessSample(combined, masterGain, limiterCeiling, settings.LimiterEnabled, out var reductionDb);
                limiterReductionDb = Math.Max(limiterReductionDb, reductionDb);
                output[leftIndex] = mono;
                output[rightIndex] = mono;
                AccumulateTelemetrySample(mono, ref peak, ref sumSquares, ref sampleCount);
                AccumulateTelemetrySample(mono, ref peak, ref sumSquares, ref sampleCount);
            }

            if (input.Length % 2 != 0)
            {
                output[input.Length - 1] = (float)ProcessSample(
                    input[^1],
                    masterGain,
                    limiterCeiling,
                    settings.LimiterEnabled,
                    out var oddReductionDb);
                limiterReductionDb = Math.Max(limiterReductionDb, oddReductionDb);
                AccumulateTelemetrySample(output[input.Length - 1], ref peak, ref sumSquares, ref sampleCount);
            }

            LastTelemetry = CreateTelemetry(peak, sumSquares, sampleCount, limiterReductionDb, _normalizeGain);
            return;
        }

        for (var i = 0; i < input.Length; i++)
        {
            output[i] = (float)ProcessSample(
                input[i],
                masterGain,
                limiterCeiling,
                settings.LimiterEnabled,
                out var sampleReductionDb);
            limiterReductionDb = Math.Max(limiterReductionDb, sampleReductionDb);
            AccumulateTelemetrySample(output[i], ref peak, ref sumSquares, ref sampleCount);
        }

        LastTelemetry = CreateTelemetry(peak, sumSquares, sampleCount, limiterReductionDb, _normalizeGain);
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
        return Math.Clamp(sample, -ceiling, ceiling);
    }

    private static double ProcessSample(
        float input,
        double masterGain,
        double limiterCeiling,
        bool limiterEnabled,
        out double limiterReductionDb)
    {
        var sample = SanitizeSample(input) * masterGain;
        limiterReductionDb = 0d;
        if (limiterEnabled)
        {
            var beforeLimiter = Math.Abs(sample);
            sample = ApplyLimiter(sample, limiterCeiling);
            var afterLimiter = Math.Abs(sample);
            if (beforeLimiter > afterLimiter && beforeLimiter > 0.000001d)
            {
                limiterReductionDb = Math.Max(0d, 20d * Math.Log10(beforeLimiter / Math.Max(afterLimiter, 0.000001d)));
            }
        }

        return Math.Clamp(sample, -1d, 1d);
    }

    private static void AccumulateTelemetrySample(float sample, ref double peak, ref double sumSquares, ref int sampleCount)
    {
        if (!float.IsFinite(sample))
        {
            return;
        }

        var sanitized = Math.Clamp(sample, -1f, 1f);
        peak = Math.Max(peak, Math.Abs(sanitized));
        sumSquares += sanitized * sanitized;
        sampleCount++;
    }

    private static MixBusTelemetry CreateTelemetry(
        double peak,
        double sumSquares,
        int sampleCount,
        double limiterReductionDb,
        double normalizeGain)
    {
        var rms = sampleCount > 0
            ? Math.Sqrt(Math.Max(0d, sumSquares / sampleCount))
            : 0d;
        return new MixBusTelemetry(
            Math.Clamp(peak, 0d, 1d),
            Math.Clamp(rms, 0d, 1d),
            Math.Max(0d, limiterReductionDb),
            Math.Clamp(double.IsFinite(normalizeGain) ? normalizeGain : 1d, MinimumNormalizeGain, MaximumNormalizeGain));
    }

    private static double DbToLinear(double decibels)
    {
        return Math.Pow(10d, decibels / 20d);
    }

    private static double SanitizeSample(float sample)
    {
        // The floating-point sum may exceed full scale before master attenuation/normalization.
        return float.IsFinite(sample) ? sample : 0f;
    }
}

public sealed record MixBusTelemetry(double PeakLevel, double RmsLevel, double LimiterReductionDb, double NormalizeGain)
{
    public static MixBusTelemetry Silence { get; } = new(0d, 0d, 0d, 1d);
}

public sealed record MixBusSettings(
    double MasterVolumePercent,
    bool AutoNormalizeEnabled,
    bool LimiterEnabled,
    double LimiterCeilingDb,
    MixBusOutputMode OutputMode = MixBusOutputMode.Stereo)
{
    public static MixBusSettings Default { get; } = new(100d, true, true, -1d, MixBusOutputMode.Stereo);
}

public enum MixBusOutputMode
{
    Stereo,
    Mono
}

public sealed record MicrophoneLiveChannelSettings(
    int ChannelNumber,
    int DeviceNumber,
    InputChannelMode InputChannelMode,
    VoiceProcessorSettings ProcessorSettings,
    double VolumePercent,
    double InputGainDb,
    double Pan,
    bool IsPolarityInverted,
    bool IsSoloed,
    double DelayMilliseconds,
    bool IsEnabled,
    bool IsMuted,
    string? EndpointId = null,
    AudioInputBackend Backend = AudioInputBackend.Windows,
    bool DuplicateMonoToStereo = false);

