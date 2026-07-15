using System.Globalization;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed record GraphicEqualizerFrequencyResponse(
    double FrequencyHz,
    double BypassMagnitude,
    double ProcessedMagnitude,
    double DeltaDb)
{
    public bool IsFinite =>
        double.IsFinite(FrequencyHz) &&
        double.IsFinite(BypassMagnitude) &&
        double.IsFinite(ProcessedMagnitude) &&
        double.IsFinite(DeltaDb) &&
        BypassMagnitude > 0d &&
        ProcessedMagnitude > 0d;
}

public sealed record GraphicEqualizerBandResponse(
    int BandIndex,
    double CenterFrequencyHz,
    double RequestedGainDb,
    GraphicEqualizerFrequencyResponse Center,
    GraphicEqualizerFrequencyResponse? LowerAdjacent,
    GraphicEqualizerFrequencyResponse? UpperAdjacent)
{
    private const double CenterToleranceDb = 2.0d;
    private const double AdjacentStrengthToleranceDb = 0.35d;

    public string BandLabel => GraphicEqualizerVerificationHarness.FormatFrequency(CenterFrequencyHz);

    public IReadOnlyList<GraphicEqualizerFrequencyResponse> AdjacentResponses
    {
        get
        {
            var responses = new List<GraphicEqualizerFrequencyResponse>(2);

            if (LowerAdjacent is not null)
            {
                responses.Add(LowerAdjacent);
            }

            if (UpperAdjacent is not null)
            {
                responses.Add(UpperAdjacent);
            }

            return responses;
        }
    }

    public bool CenterTracksRequestedGain => Math.Abs(Center.DeltaDb - RequestedGainDb) <= CenterToleranceDb;

    public bool AdjacentMeasurementsAreFinite => AdjacentResponses.All(response => response.IsFinite);

    public double StrongestAdjacentMagnitudeDb => AdjacentResponses.Count == 0
        ? 0d
        : AdjacentResponses.Max(response => Math.Abs(response.DeltaDb));

    public bool CenterIsStrongestMeasuredResponse =>
        StrongestAdjacentMagnitudeDb <= Math.Abs(Center.DeltaDb) + AdjacentStrengthToleranceDb;

    public bool Passed =>
        Center.IsFinite &&
        CenterTracksRequestedGain &&
        AdjacentMeasurementsAreFinite &&
        CenterIsStrongestMeasuredResponse;

    public string MeasurementSummary
    {
        get
        {
            var lower = LowerAdjacent is null
                ? "lower none"
                : $"lower {GraphicEqualizerVerificationHarness.FormatFrequency(LowerAdjacent.FrequencyHz)} {GraphicEqualizerVerificationHarness.FormatDeltaDb(LowerAdjacent.DeltaDb)}";
            var upper = UpperAdjacent is null
                ? "upper none"
                : $"upper {GraphicEqualizerVerificationHarness.FormatFrequency(UpperAdjacent.FrequencyHz)} {GraphicEqualizerVerificationHarness.FormatDeltaDb(UpperAdjacent.DeltaDb)}";

            return string.Format(
                CultureInfo.InvariantCulture,
                "center {0} {1}; {2}; {3}",
                GraphicEqualizerVerificationHarness.FormatFrequency(CenterFrequencyHz),
                GraphicEqualizerVerificationHarness.FormatDeltaDb(Center.DeltaDb),
                lower,
                upper);
        }
    }

    public string Details => string.Format(
        CultureInfo.InvariantCulture,
        "Requested {0} on {1} slider. Center error {2}; strongest adjacent {3}; bypass magnitude {4:0.000000}; processed magnitude {5:0.000000}.",
        GraphicEqualizerVerificationHarness.FormatDeltaDb(RequestedGainDb),
        BandLabel,
        GraphicEqualizerVerificationHarness.FormatDeltaDb(Center.DeltaDb - RequestedGainDb),
        GraphicEqualizerVerificationHarness.FormatDeltaDb(StrongestAdjacentMagnitudeDb),
        Center.BypassMagnitude,
        Center.ProcessedMagnitude);
}

public static class GraphicEqualizerVerificationHarness
{
    public const int VerificationSampleRate = 48000;
    public const double BoostGainDb = 6d;
    public const double CutGainDb = -6d;

    private const double ToneAmplitude = 0.05d;
    private const double ToneDurationSeconds = 1.75d;
    private const int MeasurementStartIndex = VerificationSampleRate * 3 / 4;

    private static readonly double[] EqualizerFrequenciesHz =
    [
        31d, 45d, 63d, 90d, 125d, 180d, 250d, 355d, 500d, 710d,
        1000d, 1400d, 2000d, 2800d, 4000d, 5600d, 8000d, 11200d, 16000d, 20000d
    ];

    public static IReadOnlyList<double> BandFrequenciesHz => EqualizerFrequenciesHz;

    public static IReadOnlyList<GraphicEqualizerBandResponse> MeasureAllBands(double requestedGainDb)
    {
        var bypassMagnitudes = EqualizerFrequenciesHz.ToDictionary(
            frequencyHz => frequencyHz,
            frequencyHz => MeasureBypassMagnitude(frequencyHz));
        var measurements = new List<GraphicEqualizerBandResponse>(EqualizerFrequenciesHz.Length);

        for (var bandIndex = 0; bandIndex < EqualizerFrequenciesHz.Length; bandIndex++)
        {
            measurements.Add(MeasureBand(bandIndex, requestedGainDb, bypassMagnitudes));
        }

        return measurements;
    }

    public static GraphicEqualizerBandResponse MeasureBand(int bandIndex, double requestedGainDb)
    {
        var bypassMagnitudes = EqualizerFrequenciesHz.ToDictionary(
            frequencyHz => frequencyHz,
            frequencyHz => MeasureBypassMagnitude(frequencyHz));

        return MeasureBand(bandIndex, requestedGainDb, bypassMagnitudes);
    }

    public static string FormatFrequency(double frequencyHz)
    {
        if (frequencyHz >= 1000d)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} kHz", frequencyHz / 1000d);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0} Hz", frequencyHz);
    }

    public static string FormatDeltaDb(double valueDb) =>
        string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00;0.00} dB", valueDb);

    private static GraphicEqualizerBandResponse MeasureBand(
        int bandIndex,
        double requestedGainDb,
        IReadOnlyDictionary<double, double> bypassMagnitudes)
    {
        if (bandIndex < 0 || bandIndex >= EqualizerFrequenciesHz.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex));
        }

        var center = MeasureFrequencyDelta(bandIndex, bandIndex, requestedGainDb, bypassMagnitudes);
        var lower = bandIndex > 0
            ? MeasureFrequencyDelta(bandIndex, bandIndex - 1, requestedGainDb, bypassMagnitudes)
            : null;
        var upper = bandIndex < EqualizerFrequenciesHz.Length - 1
            ? MeasureFrequencyDelta(bandIndex, bandIndex + 1, requestedGainDb, bypassMagnitudes)
            : null;

        return new GraphicEqualizerBandResponse(
            bandIndex,
            EqualizerFrequenciesHz[bandIndex],
            requestedGainDb,
            center,
            lower,
            upper);
    }

    private static GraphicEqualizerFrequencyResponse MeasureFrequencyDelta(
        int drivenBandIndex,
        int measuredBandIndex,
        double requestedGainDb,
        IReadOnlyDictionary<double, double> bypassMagnitudes)
    {
        var measuredFrequencyHz = EqualizerFrequenciesHz[measuredBandIndex];
        var processed = ProcessTone(measuredFrequencyHz, settings =>
        {
            var gains = new double[EqualizerFrequenciesHz.Length];
            gains[drivenBandIndex] = requestedGainDb;
            settings.SetEqualizerGains(gains);
        });
        var processedMagnitude = CalculateToneMagnitude(processed, measuredFrequencyHz, MeasurementStartIndex);
        var bypassMagnitude = bypassMagnitudes[measuredFrequencyHz];
        var deltaDb = ToDecibels(processedMagnitude / bypassMagnitude);

        return new GraphicEqualizerFrequencyResponse(
            measuredFrequencyHz,
            bypassMagnitude,
            processedMagnitude,
            deltaDb);
    }

    private static double MeasureBypassMagnitude(double frequencyHz)
    {
        var bypass = ProcessTone(frequencyHz, _ => { });
        return CalculateToneMagnitude(bypass, frequencyHz, MeasurementStartIndex);
    }

    private static float[] ProcessTone(double frequencyHz, Action<VoiceProcessorSettings> configure)
    {
        var settings = CreateTransparentVoiceSettings();
        configure(settings);
        var processor = new VoiceSampleProcessor(settings, VerificationSampleRate);
        var samples = GenerateSine(frequencyHz, ToneAmplitude, ToneDurationSeconds);

        return processor.Process(samples);
    }

    private static VoiceProcessorSettings CreateTransparentVoiceSettings()
    {
        var settings = new VoiceProcessorSettings
        {
            InputTrimDb = 0,
            HighPassEnabled = false,
            LowPassEnabled = false,
            HumRemovalEnabled = false,
            NotchFilterEnabled = false,
            ParametricEqEnabled = false,
            ShelfEqEnabled = false,
            DePopperEnabled = false,
            NoiseGateEnabled = false,
            ExpanderEnabled = false,
            NoiseSuppressionEnabled = false,
            EchoReducerEnabled = false,
            CompressorEnabled = false,
            BreathReducerEnabled = false,
            DeEsserEnabled = false,
            PresenceEnhancerEnabled = false,
            SaturationEnabled = false,
            MakeupGainDb = 0,
            LimiterEnabled = false,
            LimiterSoftClipEnabled = false,
            LimiterLookaheadEnabled = false
        };
        settings.SetEqualizerGains(new double[EqualizerFrequenciesHz.Length]);
        return settings;
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
}
