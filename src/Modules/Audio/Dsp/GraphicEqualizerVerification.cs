namespace JerichoDown.Modules.Audio.Dsp;

public static class GraphicEqualizerVerification
{
    public const int VerificationSampleRate = 48000;
    public const double BoostGainDb = 6d;
    public const double CutGainDb = -6d;

    private const double ToneAmplitude = 0.05d;
    private const double ToneDurationSeconds = 1.75d;
    private const int MeasurementStartIndex = VerificationSampleRate * 3 / 4;

    public static IReadOnlyList<double> BandFrequenciesHz => GraphicEqualizerSettings.Default.BandFrequenciesHz;

    public static GraphicEqualizerResponse Measure(double requestedGainDb) =>
        new(VerificationSampleRate, requestedGainDb, MeasureAllBands(requestedGainDb));

    public static IReadOnlyList<GraphicEqualizerBandResponse> MeasureAllBands(double requestedGainDb)
    {
        var settings = GraphicEqualizerSettings.Default;
        var bypassMagnitudes = settings.BandFrequenciesHz.ToDictionary(
            frequencyHz => frequencyHz,
            frequencyHz => MeasureBypassMagnitude(settings, frequencyHz));
        var measurements = new List<GraphicEqualizerBandResponse>(settings.BandCount);

        for (var bandIndex = 0; bandIndex < settings.BandCount; bandIndex++)
        {
            measurements.Add(MeasureBand(settings, bandIndex, requestedGainDb, bypassMagnitudes));
        }

        return measurements;
    }

    public static GraphicEqualizerBandResponse MeasureBand(int bandIndex, double requestedGainDb)
    {
        var settings = GraphicEqualizerSettings.Default;
        var bypassMagnitudes = settings.BandFrequenciesHz.ToDictionary(
            frequencyHz => frequencyHz,
            frequencyHz => MeasureBypassMagnitude(settings, frequencyHz));

        return MeasureBand(settings, bandIndex, requestedGainDb, bypassMagnitudes);
    }

    private static GraphicEqualizerBandResponse MeasureBand(
        GraphicEqualizerSettings settings,
        int bandIndex,
        double requestedGainDb,
        IReadOnlyDictionary<double, double> bypassMagnitudes)
    {
        if (bandIndex < 0 || bandIndex >= settings.BandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex));
        }

        var center = MeasureFrequencyDelta(settings, bandIndex, bandIndex, requestedGainDb, bypassMagnitudes);
        var lower = bandIndex > 0
            ? MeasureFrequencyDelta(settings, bandIndex, bandIndex - 1, requestedGainDb, bypassMagnitudes)
            : null;
        var upper = bandIndex < settings.BandCount - 1
            ? MeasureFrequencyDelta(settings, bandIndex, bandIndex + 1, requestedGainDb, bypassMagnitudes)
            : null;

        return new GraphicEqualizerBandResponse(
            bandIndex,
            settings.BandFrequenciesHz[bandIndex],
            requestedGainDb,
            center,
            lower,
            upper);
    }

    private static GraphicEqualizerFrequencyResponse MeasureFrequencyDelta(
        GraphicEqualizerSettings settings,
        int drivenBandIndex,
        int measuredBandIndex,
        double requestedGainDb,
        IReadOnlyDictionary<double, double> bypassMagnitudes)
    {
        var measuredFrequencyHz = settings.BandFrequenciesHz[measuredBandIndex];
        var processed = ProcessTone(settings, measuredFrequencyHz, gains => gains[drivenBandIndex] = requestedGainDb);
        var processedMagnitude = CalculateToneMagnitude(processed.Samples, measuredFrequencyHz, MeasurementStartIndex);
        var bypassMagnitude = bypassMagnitudes[measuredFrequencyHz];
        var deltaDb = ToDecibels(processedMagnitude / bypassMagnitude);

        return new GraphicEqualizerFrequencyResponse(
            measuredFrequencyHz,
            bypassMagnitude,
            processedMagnitude,
            deltaDb,
            processed.ModeledDeltaDb);
    }

    private static double MeasureBypassMagnitude(GraphicEqualizerSettings settings, double frequencyHz)
    {
        var bypass = ProcessTone(settings, frequencyHz, _ => { });
        return CalculateToneMagnitude(bypass.Samples, frequencyHz, MeasurementStartIndex);
    }

    private static ProcessedTone ProcessTone(
        GraphicEqualizerSettings settings,
        double frequencyHz,
        Action<double[]> configureGains)
    {
        var gains = settings.CreateFlatGains();
        configureGains(gains);

        var processor = new GraphicEqualizerProcessor(settings, VerificationSampleRate);
        var samples = GenerateSine(frequencyHz, ToneAmplitude, ToneDurationSeconds);
        var processed = new float[samples.Length];
        processor.Update(gains, 1, samples.Length);
        var modeledDeltaDb = processor.CalculateCurrentResponseDb(frequencyHz);
        processor.Process(samples, processed);

        return new ProcessedTone(processed, modeledDeltaDb);
    }

    private static float[] GenerateSine(double frequencyHz, double amplitude, double durationSeconds)
    {
        var sampleCount = (int)(VerificationSampleRate * durationSeconds);
        var samples = new float[sampleCount];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(Math.Sin(2d * Math.PI * frequencyHz * i / VerificationSampleRate) * amplitude);
        }

        return samples;
    }

    private static double CalculateToneMagnitude(IReadOnlyList<float> samples, double frequencyHz, int startIndex)
    {
        var real = 0d;
        var imaginary = 0d;
        var count = 0;

        for (var i = startIndex; i < samples.Count; i++)
        {
            var phase = 2d * Math.PI * frequencyHz * (i - startIndex) / VerificationSampleRate;
            real += samples[i] * Math.Cos(phase);
            imaginary -= samples[i] * Math.Sin(phase);
            count++;
        }

        return count == 0 ? 0d : 2d * Math.Sqrt(real * real + imaginary * imaginary) / count;
    }

    private static double ToDecibels(double ratio) => 20d * Math.Log10(Math.Max(1e-12d, ratio));

    private sealed record ProcessedTone(IReadOnlyList<float> Samples, double ModeledDeltaDb);
}
