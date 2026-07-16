using System.Globalization;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed record GraphicEqualizerFrequencyResponse(
    double FrequencyHz,
    double BypassMagnitude,
    double ProcessedMagnitude,
    double DeltaDb,
    double ModeledDeltaDb)
{
    public double ModelErrorDb => DeltaDb - ModeledDeltaDb;

    public bool IsFinite =>
        double.IsFinite(FrequencyHz) &&
        double.IsFinite(BypassMagnitude) &&
        double.IsFinite(ProcessedMagnitude) &&
        double.IsFinite(DeltaDb) &&
        double.IsFinite(ModeledDeltaDb) &&
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
    private const double ModelToleranceDb = 0.35d;

    public string BandLabel => GraphicEqualizerResponse.FormatFrequency(CenterFrequencyHz);

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

    public bool CenterMatchesModeledResponse => Math.Abs(Center.ModelErrorDb) <= ModelToleranceDb;

    public bool AdjacentMeasurementsAreFinite => AdjacentResponses.All(response => response.IsFinite);

    public double StrongestAdjacentMagnitudeDb => AdjacentResponses.Count == 0
        ? 0d
        : AdjacentResponses.Max(response => Math.Abs(response.DeltaDb));

    public bool CenterIsStrongestMeasuredResponse =>
        StrongestAdjacentMagnitudeDb <= Math.Abs(Center.DeltaDb) + AdjacentStrengthToleranceDb;

    public bool Passed =>
        Center.IsFinite &&
        CenterTracksRequestedGain &&
        CenterMatchesModeledResponse &&
        AdjacentMeasurementsAreFinite &&
        CenterIsStrongestMeasuredResponse;

    public string MeasurementSummary
    {
        get
        {
            var lower = LowerAdjacent is null
                ? "lower none"
                : $"lower {GraphicEqualizerResponse.FormatFrequency(LowerAdjacent.FrequencyHz)} {GraphicEqualizerResponse.FormatDeltaDb(LowerAdjacent.DeltaDb)}";
            var upper = UpperAdjacent is null
                ? "upper none"
                : $"upper {GraphicEqualizerResponse.FormatFrequency(UpperAdjacent.FrequencyHz)} {GraphicEqualizerResponse.FormatDeltaDb(UpperAdjacent.DeltaDb)}";

            return string.Format(
                CultureInfo.InvariantCulture,
                "center {0} {1}; {2}; {3}",
                GraphicEqualizerResponse.FormatFrequency(CenterFrequencyHz),
                GraphicEqualizerResponse.FormatDeltaDb(Center.DeltaDb),
                lower,
                upper);
        }
    }

    public string Details => string.Format(
        CultureInfo.InvariantCulture,
        "Requested {0} on {1} slider. Center error {2}; model {3}; measured-model error {4}; strongest adjacent {5}; bypass magnitude {6:0.000000}; processed magnitude {7:0.000000}.",
        GraphicEqualizerResponse.FormatDeltaDb(RequestedGainDb),
        BandLabel,
        GraphicEqualizerResponse.FormatDeltaDb(Center.DeltaDb - RequestedGainDb),
        GraphicEqualizerResponse.FormatDeltaDb(Center.ModeledDeltaDb),
        GraphicEqualizerResponse.FormatDeltaDb(Center.ModelErrorDb),
        GraphicEqualizerResponse.FormatDeltaDb(StrongestAdjacentMagnitudeDb),
        Center.BypassMagnitude,
        Center.ProcessedMagnitude);
}

public sealed record GraphicEqualizerResponse(
    int SampleRate,
    double RequestedGainDb,
    IReadOnlyList<GraphicEqualizerBandResponse> Bands)
{
    public int BandCount => Bands.Count;

    public bool Passed => Bands.All(band => band.Passed);

    public double MaximumCenterErrorDb => Bands.Count == 0
        ? 0d
        : Bands.Max(band => Math.Abs(band.Center.DeltaDb - band.RequestedGainDb));

    public double MaximumMeasuredModelErrorDb => Bands.Count == 0
        ? 0d
        : Bands.Max(band => Math.Abs(band.Center.ModelErrorDb));

    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        "{0}/{1} bands passed; max center error {2}; max measured-model error {3}",
        Bands.Count(band => band.Passed),
        Bands.Count,
        FormatDeltaDb(MaximumCenterErrorDb),
        FormatDeltaDb(MaximumMeasuredModelErrorDb));

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
}

public sealed record GraphicEqualizerCurveBandResponse(
    int BandIndex,
    double FrequencyHz,
    double RequestedGainDb,
    double BypassMagnitude,
    double ProcessedMagnitude,
    double DeltaDb,
    double ModeledDeltaDb)
{
    private const double ModelToleranceDb = 0.45d;

    public double ModelErrorDb => DeltaDb - ModeledDeltaDb;

    public bool IsFinite =>
        double.IsFinite(FrequencyHz) &&
        double.IsFinite(RequestedGainDb) &&
        double.IsFinite(BypassMagnitude) &&
        double.IsFinite(ProcessedMagnitude) &&
        double.IsFinite(DeltaDb) &&
        double.IsFinite(ModeledDeltaDb) &&
        BypassMagnitude > 0d &&
        ProcessedMagnitude > 0d;

    public bool Passed => IsFinite && Math.Abs(ModelErrorDb) <= ModelToleranceDb;

    public string BandLabel => GraphicEqualizerResponse.FormatFrequency(FrequencyHz);

    public string MeasurementSummary => string.Format(
        CultureInfo.InvariantCulture,
        "{0} slider {1}; measured {2}; modeled {3}; error {4}",
        BandLabel,
        GraphicEqualizerResponse.FormatDeltaDb(RequestedGainDb),
        GraphicEqualizerResponse.FormatDeltaDb(DeltaDb),
        GraphicEqualizerResponse.FormatDeltaDb(ModeledDeltaDb),
        GraphicEqualizerResponse.FormatDeltaDb(ModelErrorDb));
}

public sealed record GraphicEqualizerCurveResponse(
    int SampleRate,
    IReadOnlyList<double> RequestedGainsDb,
    IReadOnlyList<GraphicEqualizerCurveBandResponse> Bands)
{
    public bool Passed => Bands.All(band => band.Passed);

    public double MaximumMeasuredModelErrorDb => Bands.Count == 0
        ? 0d
        : Bands.Max(band => Math.Abs(band.ModelErrorDb));

    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        "{0}/{1} curve points passed; max measured-model error {2}",
        Bands.Count(band => band.Passed),
        Bands.Count,
        GraphicEqualizerResponse.FormatDeltaDb(MaximumMeasuredModelErrorDb));
}
