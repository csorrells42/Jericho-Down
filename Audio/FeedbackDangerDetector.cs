namespace JerichoDown.Audio;

public static class FeedbackDangerDetector
{
    private const double MinimumDisplayFrequency = 20d;
    private const double MaximumDisplayFrequency = 20000d;
    private const double MinimumFeedbackFrequency = 140d;
    private const double MaximumFeedbackFrequency = 12000d;
    private const double MinimumPeakMagnitude = 0.50d;
    private const double MinimumSpikeDifference = 0.18d;
    private const int NeighborhoodRadius = 8;
    private const int GuardRadius = 1;

    public static FeedbackDangerResult Analyze(IReadOnlyList<double> magnitudes)
    {
        if (magnitudes.Count < NeighborhoodRadius * 2 + 3)
        {
            return FeedbackDangerResult.None;
        }

        var bestScore = 0d;
        var bestFrequency = 0d;
        var bestDifference = 0d;
        var startIndex = FrequencyToIndex(MinimumFeedbackFrequency, magnitudes.Count);
        var endIndex = FrequencyToIndex(MaximumFeedbackFrequency, magnitudes.Count);
        startIndex = Math.Clamp(startIndex, NeighborhoodRadius, magnitudes.Count - NeighborhoodRadius - 1);
        endIndex = Math.Clamp(endIndex, startIndex, magnitudes.Count - NeighborhoodRadius - 1);

        for (var i = startIndex; i <= endIndex; i++)
        {
            var peak = SanitizeMagnitude(magnitudes[i]);
            if (peak < MinimumPeakMagnitude)
            {
                continue;
            }

            var neighborhoodAverage = CalculateNeighborhoodAverage(magnitudes, i);
            var spikeDifference = peak - neighborhoodAverage;
            if (spikeDifference < MinimumSpikeDifference)
            {
                continue;
            }

            var leftSlope = peak - SanitizeMagnitude(magnitudes[i - GuardRadius - 1]);
            var rightSlope = peak - SanitizeMagnitude(magnitudes[i + GuardRadius + 1]);
            var narrowness = Math.Clamp(Math.Min(leftSlope, rightSlope) / 0.22d, 0d, 1d);
            var score = Math.Clamp((spikeDifference - MinimumSpikeDifference) / 0.24d, 0d, 1d) * 0.72d
                + Math.Clamp((peak - MinimumPeakMagnitude) / 0.35d, 0d, 1d) * 0.20d
                + narrowness * 0.08d;

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestFrequency = FrequencyForIndex(i, magnitudes.Count);
            bestDifference = spikeDifference;
        }

        return bestScore <= 0d
            ? FeedbackDangerResult.None
            : new FeedbackDangerResult(true, bestScore, bestFrequency, bestDifference * 70d);
    }

    public static int FrequencyToIndex(double frequencyHz, int magnitudeCount)
    {
        if (magnitudeCount <= 1)
        {
            return 0;
        }

        var clamped = Math.Clamp(frequencyHz, MinimumDisplayFrequency, MaximumDisplayFrequency);
        var position = Math.Log(clamped / MinimumDisplayFrequency) / Math.Log(MaximumDisplayFrequency / MinimumDisplayFrequency);
        return Math.Clamp((int)Math.Round(position * (magnitudeCount - 1)), 0, magnitudeCount - 1);
    }

    private static double CalculateNeighborhoodAverage(IReadOnlyList<double> magnitudes, int centerIndex)
    {
        var sum = 0d;
        var count = 0;
        var start = centerIndex - NeighborhoodRadius;
        var end = centerIndex + NeighborhoodRadius;
        for (var i = start; i <= end; i++)
        {
            if (Math.Abs(i - centerIndex) <= GuardRadius)
            {
                continue;
            }

            sum += SanitizeMagnitude(magnitudes[i]);
            count++;
        }

        return count == 0 ? 0d : sum / count;
    }

    private static double FrequencyForIndex(int index, int magnitudeCount)
    {
        if (magnitudeCount <= 1)
        {
            return MinimumDisplayFrequency;
        }

        var position = Math.Clamp(index / (double)(magnitudeCount - 1), 0d, 1d);
        return MinimumDisplayFrequency * Math.Pow(MaximumDisplayFrequency / MinimumDisplayFrequency, position);
    }

    private static double SanitizeMagnitude(double magnitude)
    {
        return double.IsFinite(magnitude) ? Math.Clamp(magnitude, 0d, 1d) : 0d;
    }
}

public sealed record FeedbackDangerResult(
    bool IsDangerous,
    double Score,
    double FrequencyHz,
    double SpikeRiseDb)
{
    public static FeedbackDangerResult None { get; } = new(false, 0d, 0d, 0d);
}
