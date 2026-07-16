using System.Globalization;
using System.Reflection;
using System.Text;

namespace JerichoDown.Modules.Audio.Dsp;

public sealed record DspVerificationReport(
    DateTimeOffset GeneratedAt,
    int SampleRate,
    string AssemblyName,
    string AssemblyVersion,
    IReadOnlyList<DspVerificationCheck> Checks)
{
    public int PassedCount => Checks.Count(check => check.Passed);

    public bool Passed => PassedCount == Checks.Count;

    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        "{0}/{1} checks passed",
        PassedCount,
        Checks.Count);
}

public sealed record DspVerificationCheck(
    string Effect,
    string Claim,
    string Measurement,
    string Requirement,
    double Ratio,
    bool Passed,
    string Details)
{
    public string Result => Passed ? "PASS" : "FAIL";
}

public static class DspVerificationReportGenerator
{
    private const int VerificationSampleRate = 48_000;
    private static readonly double[] EqualizerFrequenciesHz = GraphicEqualizerVerification.BandFrequenciesHz.ToArray();

    public static DspVerificationReport Run()
    {
        var checks = new List<DspVerificationCheck>();
        AddGraphicEqualizerChecks(checks);
        AddInputGainChecks(checks);
        AddHighPassChecks(checks);
        AddLowPassChecks(checks);
        AddHumRemovalChecks(checks);
        AddNotchFilterChecks(checks);
        AddParametricEqChecks(checks);
        AddShelfEqChecks(checks);
        AddDePopperChecks(checks);
        AddNoiseGateChecks(checks);
        AddExpanderChecks(checks);
        AddNoiseSuppressionChecks(checks);
        AddEchoReducerChecks(checks);
        AddCompressorChecks(checks);
        AddBreathReducerChecks(checks);
        AddDeEsserChecks(checks);
        AddPresenceEnhancerChecks(checks);
        AddSaturationChecks(checks);
        AddLimiterChecks(checks);
        AddFullChainSafetyCheck(checks);

        var assemblyName = typeof(DspVerificationReportGenerator).Assembly.GetName();
        return new DspVerificationReport(
            DateTimeOffset.Now,
            VerificationSampleRate,
            assemblyName.Name ?? "JerichoDown",
            assemblyName.Version?.ToString() ?? "unknown",
            checks);
    }

    public static string CreateMarkdownReport(DspVerificationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Jericho Down DSP Verification");
        builder.AppendLine();
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Generated: {0:yyyy-MM-dd HH:mm:ss zzz}", report.GeneratedAt));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Build: {0} {1}", report.AssemblyName, report.AssemblyVersion));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Sample rate: {0:N0} Hz", report.SampleRate));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Result: {0} ({1})", report.Passed ? "PASS" : "FAIL", report.Summary));
        builder.AppendLine();
        builder.AppendLine("## Method");
        builder.AppendLine();
        builder.AppendLine("Jericho Down generated known tone, noise, transient, and composite test signals, ran them through the currently compiled voice DSP processor, and compared processed output against bypassed output after filter and envelope settling time. These checks prove the measured behavior of the custom Jericho EQ/DSP code in this build, including both intended action and nearby voice-content preservation where practical.");
        builder.AppendLine();
        builder.AppendLine("NAudio-branded effects are intentionally outside this custom verification report because those implementations are supplied by NAudio. This report verifies Jericho Down's own DSP code and the app-level settings that drive it.");
        builder.AppendLine();
        builder.AppendLine("## Checks");
        builder.AppendLine();
        builder.AppendLine("| Effect | Claim | Measurement | Requirement | Result | Details |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var check in report.Checks)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdownCell(check.Effect));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownCell(check.Claim));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownCell(check.Measurement));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownCell(check.Requirement));
            builder.Append(" | ");
            builder.Append(check.Result);
            builder.Append(" | ");
            builder.Append(EscapeMarkdownCell(check.Details));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Customer Note");
        builder.AppendLine();
        builder.AppendLine("A passing report means these DSP features are not only labeled in the interface; they were exercised with known input signals and measured against documented expectations in this exact application build.");
        return builder.ToString();
    }

    private static void AddGraphicEqualizerChecks(ICollection<DspVerificationCheck> checks)
    {
        AddGraphicEqualizerResponseChecks(
            checks,
            GraphicEqualizerVerification.MeasureAllBands(GraphicEqualizerVerification.BoostGainDb));
        AddGraphicEqualizerResponseChecks(
            checks,
            GraphicEqualizerVerification.MeasureAllBands(GraphicEqualizerVerification.CutGainDb));
        AddGraphicEqualizerCurveCheck(checks);
    }

    private static void AddGraphicEqualizerResponseChecks(
        ICollection<DspVerificationCheck> checks,
        IEnumerable<GraphicEqualizerBandResponse> measurements)
    {
        foreach (var measurement in measurements)
        {
            checks.Add(new DspVerificationCheck(
                "Graphic EQ",
                $"{GraphicEqualizerResponse.FormatDeltaDb(measurement.RequestedGainDb)} on the {measurement.BandLabel} slider measures center and adjacent response",
                measurement.MeasurementSummary,
                "center response within 2.0 dB of requested gain; adjacent responses measured and not stronger than center response; measured center matches modeled response",
                measurement.Center.DeltaDb,
                measurement.Passed,
                measurement.Details));
        }
    }

    private static void AddGraphicEqualizerCurveCheck(ICollection<DspVerificationCheck> checks)
    {
        var gains = GraphicEqualizerSettings.Default.CreateFlatGains();
        for (var i = 0; i < Math.Min(6, gains.Length); i++)
        {
            gains[i] = GraphicEqualizerSettings.Default.MaximumGainDb;
        }

        var response = GraphicEqualizerVerification.MeasureCurve(gains);
        var lowAverageDb = response.Bands.Take(6).Average(band => band.DeltaDb);
        var highAverageDb = response.Bands.Skip(12).Average(band => band.DeltaDb);
        var lowLiftDb = lowAverageDb - highAverageDb;
        var passed = response.Passed && lowLiftDb >= 8d;

        checks.Add(new DspVerificationCheck(
            "Graphic EQ",
            "Stacked adjacent low-frequency sliders produce a measured low-end EQ curve",
            $"low average {GraphicEqualizerResponse.FormatDeltaDb(lowAverageDb)}; high average {GraphicEqualizerResponse.FormatDeltaDb(highAverageDb)}; lift {GraphicEqualizerResponse.FormatDeltaDb(lowLiftDb)}",
            "six lowest sliders at maximum must produce at least +8 dB more low-band than high-band energy and match modeled response",
            lowLiftDb,
            passed,
            response.Summary));
    }

    private static void AddInputGainChecks(ICollection<DspVerificationCheck> checks)
    {
        var tone = GenerateSine(1_000d, 0.08d, 1.5d);
        var bypass = Process(tone, _ => { });
        var trimmed = Process(tone, settings => settings.InputTrimDb = 6d);
        var madeUp = Process(tone, settings => settings.MakeupGainDb = 6d);

        const int start = VerificationSampleRate / 2;
        var bypassRms = CalculateTailRms(bypass, start);
        var trimmedRms = CalculateTailRms(trimmed, start);
        var madeUpRms = CalculateTailRms(madeUp, start);

        checks.Add(MinRatioCheck(
            "Input trim",
            "+6 dB input trim raises the signal before DSP",
            Ratio(trimmedRms, bypassRms),
            1.78d,
            RmsDetails(bypassRms, trimmedRms)));
        checks.Add(MinRatioCheck(
            "Makeup gain",
            "+6 dB makeup gain raises the signal after DSP",
            Ratio(madeUpRms, bypassRms),
            1.78d,
            RmsDetails(bypassRms, madeUpRms)));
    }

    private static void AddHighPassChecks(ICollection<DspVerificationCheck> checks)
    {
        var rumbleTone = GenerateSine(40d, 0.5d, 2.0d);
        var voiceTone = GenerateSine(1_000d, 0.5d, 2.0d);
        var rumbleBypass = Process(rumbleTone, _ => { });
        var rumbleFiltered = Process(rumbleTone, ConfigureHighPass);
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceFiltered = Process(voiceTone, ConfigureHighPass);

        const int start = VerificationSampleRate;
        var rumbleBypassRms = CalculateTailRms(rumbleBypass, start);
        var rumbleFilteredRms = CalculateTailRms(rumbleFiltered, start);
        var voiceBypassRms = CalculateTailRms(voiceBypass, start);
        var voiceFilteredRms = CalculateTailRms(voiceFiltered, start);

        checks.Add(MaxRatioCheck(
            "High-pass filter",
            "40 Hz rumble is reduced by the 120 Hz high-pass filter",
            Ratio(rumbleFilteredRms, rumbleBypassRms),
            0.55d,
            RmsDetails(rumbleBypassRms, rumbleFilteredRms)));
        checks.Add(MinRatioCheck(
            "High-pass filter",
            "1 kHz voice-band energy stays audible",
            Ratio(voiceFilteredRms, voiceBypassRms),
            0.82d,
            RmsDetails(voiceBypassRms, voiceFilteredRms)));
    }

    private static void AddLowPassChecks(ICollection<DspVerificationCheck> checks)
    {
        var highTone = GenerateSine(12_000d, 0.5d, 1.0d);
        var voiceTone = GenerateSine(1_000d, 0.5d, 1.0d);
        var highBypass = Process(highTone, _ => { });
        var highFiltered = Process(highTone, settings =>
        {
            settings.LowPassEnabled = true;
            settings.LowPassFrequencyHz = 4_000;
        });
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceFiltered = Process(voiceTone, settings =>
        {
            settings.LowPassEnabled = true;
            settings.LowPassFrequencyHz = 4_000;
        });

        var highBypassRms = CalculateTailRms(highBypass, VerificationSampleRate / 2);
        var highFilteredRms = CalculateTailRms(highFiltered, VerificationSampleRate / 2);
        var voiceBypassRms = CalculateTailRms(voiceBypass, VerificationSampleRate / 2);
        var voiceFilteredRms = CalculateTailRms(voiceFiltered, VerificationSampleRate / 2);

        checks.Add(MaxRatioCheck(
            "Low-pass filter",
            "12 kHz hiss is reduced by the 4 kHz low-pass filter",
            Ratio(highFilteredRms, highBypassRms),
            0.35d,
            RmsDetails(highBypassRms, highFilteredRms)));
        checks.Add(MinRatioCheck(
            "Low-pass filter",
            "1 kHz voice-band energy stays audible",
            Ratio(voiceFilteredRms, voiceBypassRms),
            0.70d,
            RmsDetails(voiceBypassRms, voiceFilteredRms)));
    }

    private static void AddHumRemovalChecks(ICollection<DspVerificationCheck> checks)
    {
        var humTone = GenerateSine(60d, 0.5d, 2.0d);
        var harmonicTone = GenerateSine(120d, 0.5d, 2.0d);
        var voiceTone = GenerateSine(1_000d, 0.5d, 2.0d);
        var humBypass = Process(humTone, _ => { });
        var humFiltered = Process(humTone, settings =>
        {
            settings.HumRemovalEnabled = true;
            settings.HumRemovalFrequencyHz = 60;
        });
        var harmonicBypass = Process(harmonicTone, _ => { });
        var harmonicFiltered = Process(harmonicTone, settings =>
        {
            settings.HumRemovalEnabled = true;
            settings.HumRemovalFrequencyHz = 60;
        });
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceFiltered = Process(voiceTone, settings =>
        {
            settings.HumRemovalEnabled = true;
            settings.HumRemovalFrequencyHz = 60;
        });

        const int start = VerificationSampleRate;
        var humBypassRms = CalculateTailRms(humBypass, start);
        var humFilteredRms = CalculateTailRms(humFiltered, start);
        var harmonicBypassRms = CalculateTailRms(harmonicBypass, start);
        var harmonicFilteredRms = CalculateTailRms(harmonicFiltered, start);
        var voiceBypassRms = CalculateTailRms(voiceBypass, start);
        var voiceFilteredRms = CalculateTailRms(voiceFiltered, start);

        checks.Add(MaxRatioCheck(
            "Hum removal",
            "60 Hz mains hum is notched down",
            Ratio(humFilteredRms, humBypassRms),
            0.45d,
            RmsDetails(humBypassRms, humFilteredRms)));
        checks.Add(MaxRatioCheck(
            "Hum removal",
            "120 Hz second harmonic is reduced",
            Ratio(harmonicFilteredRms, harmonicBypassRms),
            0.65d,
            RmsDetails(harmonicBypassRms, harmonicFilteredRms)));
        checks.Add(MinRatioCheck(
            "Hum removal",
            "1 kHz voice-band energy is preserved",
            Ratio(voiceFilteredRms, voiceBypassRms),
            0.85d,
            RmsDetails(voiceBypassRms, voiceFilteredRms)));
    }

    private static void AddNotchFilterChecks(ICollection<DspVerificationCheck> checks)
    {
        var ringingTone = GenerateSine(2_800d, 0.5d, 2.0d);
        var nearbyTone = GenerateSine(2_200d, 0.5d, 2.0d);
        var bypassRing = Process(ringingTone, _ => { });
        var notchedRing = Process(ringingTone, settings =>
        {
            settings.NotchFilterEnabled = true;
            settings.NotchFilterFrequencyHz = 2_800;
            settings.NotchFilterDepthDb = 30;
            settings.NotchFilterQ = 18;
        });
        var bypassNearby = Process(nearbyTone, _ => { });
        var notchedNearby = Process(nearbyTone, settings =>
        {
            settings.NotchFilterEnabled = true;
            settings.NotchFilterFrequencyHz = 2_800;
            settings.NotchFilterDepthDb = 30;
            settings.NotchFilterQ = 18;
        });

        const int start = VerificationSampleRate;
        var bypassRingRms = CalculateTailRms(bypassRing, start);
        var notchedRingRms = CalculateTailRms(notchedRing, start);
        var bypassNearbyRms = CalculateTailRms(bypassNearby, start);
        var notchedNearbyRms = CalculateTailRms(notchedNearby, start);

        checks.Add(MaxRatioCheck(
            "Notch filter",
            "Selected 2.8 kHz ring is cut",
            Ratio(notchedRingRms, bypassRingRms),
            0.42d,
            RmsDetails(bypassRingRms, notchedRingRms)));
        checks.Add(MinRatioCheck(
            "Notch filter",
            "Nearby 2.2 kHz content mostly remains",
            Ratio(notchedNearbyRms, bypassNearbyRms),
            0.74d,
            RmsDetails(bypassNearbyRms, notchedNearbyRms)));
    }

    private static void AddParametricEqChecks(ICollection<DspVerificationCheck> checks)
    {
        var centerTone = GenerateSine(1_000d, 0.35d, 2.0d);
        var offBandTone = GenerateSine(4_000d, 0.35d, 2.0d);
        var centerBypass = Process(centerTone, _ => { });
        var centerBoosted = Process(centerTone, settings =>
        {
            settings.ParametricEqEnabled = true;
            settings.ParametricEqFrequencyHz = 1_000;
            settings.ParametricEqGainDb = 6;
            settings.ParametricEqQ = 2.2;
        });
        var centerCut = Process(centerTone, settings =>
        {
            settings.ParametricEqEnabled = true;
            settings.ParametricEqFrequencyHz = 1_000;
            settings.ParametricEqGainDb = -9;
            settings.ParametricEqQ = 2.2;
        });
        var offBypass = Process(offBandTone, _ => { });
        var offBoosted = Process(offBandTone, settings =>
        {
            settings.ParametricEqEnabled = true;
            settings.ParametricEqFrequencyHz = 1_000;
            settings.ParametricEqGainDb = 6;
            settings.ParametricEqQ = 2.2;
        });

        const int start = VerificationSampleRate;
        var centerBypassRms = CalculateTailRms(centerBypass, start);
        var centerBoostedRms = CalculateTailRms(centerBoosted, start);
        var centerCutRms = CalculateTailRms(centerCut, start);
        var offBypassRms = CalculateTailRms(offBypass, start);
        var offBoostedRms = CalculateTailRms(offBoosted, start);

        checks.Add(MinRatioCheck(
            "Parametric EQ",
            "+6 dB at 1 kHz raises the selected frequency",
            Ratio(centerBoostedRms, centerBypassRms),
            1.65d,
            RmsDetails(centerBypassRms, centerBoostedRms)));
        checks.Add(MaxRatioCheck(
            "Parametric EQ",
            "-9 dB at 1 kHz lowers the selected frequency",
            Ratio(centerCutRms, centerBypassRms),
            0.52d,
            RmsDetails(centerBypassRms, centerCutRms)));
        checks.Add(RangeRatioCheck(
            "Parametric EQ",
            "4 kHz off-band content stays mostly unchanged",
            Ratio(offBoostedRms, offBypassRms),
            0.86d,
            1.14d,
            RmsDetails(offBypassRms, offBoostedRms)));
    }

    private static void AddShelfEqChecks(ICollection<DspVerificationCheck> checks)
    {
        var lowTone = GenerateSine(120d, 0.35d, 2.0d);
        var midTone = GenerateSine(1_000d, 0.35d, 2.0d);
        var highTone = GenerateSine(10_000d, 0.35d, 2.0d);
        var lowBypass = Process(lowTone, _ => { });
        var lowShaped = Process(lowTone, ConfigureShelfEq);
        var midBypass = Process(midTone, _ => { });
        var midShaped = Process(midTone, ConfigureShelfEq);
        var highBypass = Process(highTone, _ => { });
        var highShaped = Process(highTone, ConfigureShelfEq);

        const int start = VerificationSampleRate;
        var lowBypassRms = CalculateTailRms(lowBypass, start);
        var lowShapedRms = CalculateTailRms(lowShaped, start);
        var midBypassRms = CalculateTailRms(midBypass, start);
        var midShapedRms = CalculateTailRms(midShaped, start);
        var highBypassRms = CalculateTailRms(highBypass, start);
        var highShapedRms = CalculateTailRms(highShaped, start);

        checks.Add(MinRatioCheck(
            "Shelf EQ",
            "+6 dB low shelf raises 120 Hz body",
            Ratio(lowShapedRms, lowBypassRms),
            1.45d,
            RmsDetails(lowBypassRms, lowShapedRms)));
        checks.Add(MaxRatioCheck(
            "Shelf EQ",
            "-7 dB high shelf tames 10 kHz air",
            Ratio(highShapedRms, highBypassRms),
            0.70d,
            RmsDetails(highBypassRms, highShapedRms)));
        checks.Add(RangeRatioCheck(
            "Shelf EQ",
            "1 kHz mids remain stable between shelves",
            Ratio(midShapedRms, midBypassRms),
            0.82d,
            1.22d,
            RmsDetails(midBypassRms, midShapedRms)));
    }

    private static void AddDePopperChecks(ICollection<DspVerificationCheck> checks)
    {
        var popTone = GenerateSine(110d, 0.50d, 2.0d);
        var voiceTone = GenerateSine(1_000d, 0.30d, 2.0d);
        var popBypass = Process(popTone, _ => { });
        var popReduced = Process(popTone, ConfigureDePopper);
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceReduced = Process(voiceTone, ConfigureDePopper);

        const int start = VerificationSampleRate;
        var popBypassRms = CalculateTailRms(popBypass, start);
        var popReducedRms = CalculateTailRms(popReduced, start);
        var voiceBypassRms = CalculateTailRms(voiceBypass, start);
        var voiceReducedRms = CalculateTailRms(voiceReduced, start);

        checks.Add(MaxRatioCheck(
            "De-popper",
            "Low-frequency plosive energy is reduced",
            Ratio(popReducedRms, popBypassRms),
            0.86d,
            RmsDetails(popBypassRms, popReducedRms)));
        checks.Add(MinRatioCheck(
            "De-popper",
            "1 kHz voice tone stays mostly intact",
            Ratio(voiceReducedRms, voiceBypassRms),
            0.82d,
            RmsDetails(voiceBypassRms, voiceReducedRms)));
    }

    private static void AddNoiseGateChecks(ICollection<DspVerificationCheck> checks)
    {
        var quietTone = GenerateSine(1_000d, 0.003d, 2.0d);
        var loudTone = GenerateSine(1_000d, 0.20d, 2.0d);
        var quietBypass = Process(quietTone, _ => { });
        var quietGated = Process(quietTone, ConfigureNoiseGate);
        var loudBypass = Process(loudTone, _ => { });
        var loudGated = Process(loudTone, ConfigureNoiseGate);

        const int start = VerificationSampleRate;
        var quietBypassRms = CalculateTailRms(quietBypass, start);
        var quietGatedRms = CalculateTailRms(quietGated, start);
        var loudBypassRms = CalculateTailRms(loudBypass, start);
        var loudGatedRms = CalculateTailRms(loudGated, start);

        checks.Add(MaxRatioCheck(
            "Noise gate",
            "Quiet below-threshold noise is turned down",
            Ratio(quietGatedRms, quietBypassRms),
            0.62d,
            RmsDetails(quietBypassRms, quietGatedRms)));
        checks.Add(MinRatioCheck(
            "Noise gate",
            "Normal speaking level remains open",
            Ratio(loudGatedRms, loudBypassRms),
            0.86d,
            RmsDetails(loudBypassRms, loudGatedRms)));
    }

    private static void AddExpanderChecks(ICollection<DspVerificationCheck> checks)
    {
        var quietTone = GenerateSine(1_000d, 0.004d, 2.0d);
        var loudTone = GenerateSine(1_000d, 0.18d, 2.0d);
        var quietBypass = Process(quietTone, _ => { });
        var quietExpanded = Process(quietTone, ConfigureExpander);
        var loudBypass = Process(loudTone, _ => { });
        var loudExpanded = Process(loudTone, ConfigureExpander);

        const int start = VerificationSampleRate;
        var quietBypassRms = CalculateTailRms(quietBypass, start);
        var quietExpandedRms = CalculateTailRms(quietExpanded, start);
        var loudBypassRms = CalculateTailRms(loudBypass, start);
        var loudExpandedRms = CalculateTailRms(loudExpanded, start);

        checks.Add(MaxRatioCheck(
            "Expander",
            "Low-level signal below the threshold is pushed down",
            Ratio(quietExpandedRms, quietBypassRms),
            0.70d,
            RmsDetails(quietBypassRms, quietExpandedRms)));
        checks.Add(MinRatioCheck(
            "Expander",
            "Normal speaking level is mostly preserved",
            Ratio(loudExpandedRms, loudBypassRms),
            0.86d,
            RmsDetails(loudBypassRms, loudExpandedRms)));
    }

    private static void AddNoiseSuppressionChecks(ICollection<DspVerificationCheck> checks)
    {
        var noise = GenerateNoise(0.003d, 3.0d);
        var voiceTone = GenerateSine(1_000d, 0.20d, 2.0d);
        var noiseBypass = Process(noise, _ => { });
        var noiseReduced = Process(noise, ConfigureAggressiveNoiseSuppression);
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceReduced = Process(voiceTone, ConfigureNormalNoiseSuppression);

        var noiseStart = VerificationSampleRate * 2;
        const int voiceStart = VerificationSampleRate;
        var noiseBypassRms = CalculateTailRms(noiseBypass, noiseStart);
        var noiseReducedRms = CalculateTailRms(noiseReduced, noiseStart);
        var voiceBypassRms = CalculateTailRms(voiceBypass, voiceStart);
        var voiceReducedRms = CalculateTailRms(voiceReduced, voiceStart);

        checks.Add(MaxRatioCheck(
            "Noise suppression",
            "Steady low-level noise is reduced with a strong cleanup setting",
            Ratio(noiseReducedRms, noiseBypassRms),
            0.78d,
            RmsDetails(noiseBypassRms, noiseReducedRms)));
        checks.Add(MinRatioCheck(
            "Noise suppression",
            "Strong voice-band tone is preserved with a normal monitoring setting",
            Ratio(voiceReducedRms, voiceBypassRms),
            0.82d,
            RmsDetails(voiceBypassRms, voiceReducedRms)));
    }

    private static void AddEchoReducerChecks(ICollection<DspVerificationCheck> checks)
    {
        var echoTail = GenerateEchoTail();
        var bypass = Process(echoTail, _ => { });
        var reduced = Process(echoTail, ConfigureEchoReducer);

        var directStart = 0;
        var directLength = (int)(VerificationSampleRate * 0.018d);
        var tailStart = (int)(VerificationSampleRate * 0.035d);
        var tailLength = (int)(VerificationSampleRate * 0.20d);
        var bypassDirectRms = CalculateSegmentRms(bypass, directStart, directLength);
        var reducedDirectRms = CalculateSegmentRms(reduced, directStart, directLength);
        var bypassTailRms = CalculateSegmentRms(bypass, tailStart, tailLength);
        var reducedTailRms = CalculateSegmentRms(reduced, tailStart, tailLength);

        checks.Add(MaxRatioCheck(
            "Echo reducer",
            "Low tail after a loud sound is reduced",
            Ratio(reducedTailRms, bypassTailRms),
            0.92d,
            RmsDetails(bypassTailRms, reducedTailRms)));
        checks.Add(MinRatioCheck(
            "Echo reducer",
            "The direct initial sound remains audible",
            Ratio(reducedDirectRms, bypassDirectRms),
            0.78d,
            RmsDetails(bypassDirectRms, reducedDirectRms)));
    }

    private static void AddCompressorChecks(ICollection<DspVerificationCheck> checks)
    {
        var loudTone = GenerateSine(1_000d, 0.75d, 2.0d);
        var quietTone = GenerateSine(1_000d, 0.035d, 2.0d);
        var loudBypass = Process(loudTone, _ => { });
        var loudCompressed = Process(loudTone, ConfigureCompressor);
        var quietBypass = Process(quietTone, _ => { });
        var quietCompressed = Process(quietTone, ConfigureCompressor);

        const int start = VerificationSampleRate;
        var loudBypassRms = CalculateTailRms(loudBypass, start);
        var loudCompressedRms = CalculateTailRms(loudCompressed, start);
        var quietBypassRms = CalculateTailRms(quietBypass, start);
        var quietCompressedRms = CalculateTailRms(quietCompressed, start);

        checks.Add(MaxRatioCheck(
            "Compressor",
            "Loud signal above threshold is reduced",
            Ratio(loudCompressedRms, loudBypassRms),
            0.82d,
            RmsDetails(loudBypassRms, loudCompressedRms)));
        checks.Add(MinRatioCheck(
            "Compressor",
            "Quiet signal below threshold is preserved",
            Ratio(quietCompressedRms, quietBypassRms),
            0.84d,
            RmsDetails(quietBypassRms, quietCompressedRms)));
    }

    private static void AddBreathReducerChecks(ICollection<DspVerificationCheck> checks)
    {
        var breathTone = GenerateSine(6_500d, 0.24d, 2.0d);
        var voiceTone = GenerateSine(1_000d, 0.35d, 2.0d);
        var breathBypass = Process(breathTone, _ => { });
        var breathReduced = Process(breathTone, ConfigureBreathReducer);
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceReduced = Process(voiceTone, ConfigureBreathReducer);

        const int start = VerificationSampleRate;
        var breathBypassRms = CalculateTailRms(breathBypass, start);
        var breathReducedRms = CalculateTailRms(breathReduced, start);
        var voiceBypassRms = CalculateTailRms(voiceBypass, start);
        var voiceReducedRms = CalculateTailRms(voiceReduced, start);

        checks.Add(MaxRatioCheck(
            "Breath reducer",
            "Airy breath-band noise is lowered",
            Ratio(breathReducedRms, breathBypassRms),
            0.72d,
            RmsDetails(breathBypassRms, breathReducedRms)));
        checks.Add(MinRatioCheck(
            "Breath reducer",
            "Normal 1 kHz voice tone is preserved",
            Ratio(voiceReducedRms, voiceBypassRms),
            0.86d,
            RmsDetails(voiceBypassRms, voiceReducedRms)));
    }

    private static void AddDeEsserChecks(ICollection<DspVerificationCheck> checks)
    {
        var sibilanceTone = GenerateSine(7_000d, 0.35d, 2.0d);
        var voiceTone = GenerateSine(1_000d, 0.35d, 2.0d);
        var sibilanceBypass = Process(sibilanceTone, _ => { });
        var sibilanceReduced = Process(sibilanceTone, ConfigureDeEsser);
        var voiceBypass = Process(voiceTone, _ => { });
        var voiceReduced = Process(voiceTone, ConfigureDeEsser);

        const int start = VerificationSampleRate;
        var sibilanceBypassRms = CalculateTailRms(sibilanceBypass, start);
        var sibilanceReducedRms = CalculateTailRms(sibilanceReduced, start);
        var voiceBypassRms = CalculateTailRms(voiceBypass, start);
        var voiceReducedRms = CalculateTailRms(voiceReduced, start);

        checks.Add(MaxRatioCheck(
            "De-esser",
            "7 kHz sibilance is reduced",
            Ratio(sibilanceReducedRms, sibilanceBypassRms),
            0.78d,
            RmsDetails(sibilanceBypassRms, sibilanceReducedRms)));
        checks.Add(MinRatioCheck(
            "De-esser",
            "1 kHz voice tone is preserved",
            Ratio(voiceReducedRms, voiceBypassRms),
            0.86d,
            RmsDetails(voiceBypassRms, voiceReducedRms)));
    }

    private static void AddPresenceEnhancerChecks(ICollection<DspVerificationCheck> checks)
    {
        var presenceTone = GenerateSine(3_000d, 0.20d, 2.0d);
        var lowTone = GenerateSine(300d, 0.20d, 2.0d);
        var presenceBypass = Process(presenceTone, _ => { });
        var presenceEnhanced = Process(presenceTone, ConfigurePresenceEnhancer);
        var lowBypass = Process(lowTone, _ => { });
        var lowEnhanced = Process(lowTone, ConfigurePresenceEnhancer);

        const int start = VerificationSampleRate;
        var presenceBypassRms = CalculateTailRms(presenceBypass, start);
        var presenceEnhancedRms = CalculateTailRms(presenceEnhanced, start);
        var lowBypassRms = CalculateTailRms(lowBypass, start);
        var lowEnhancedRms = CalculateTailRms(lowEnhanced, start);

        checks.Add(MinRatioCheck(
            "Presence enhancer",
            "3 kHz presence band is lifted",
            Ratio(presenceEnhancedRms, presenceBypassRms),
            1.10d,
            RmsDetails(presenceBypassRms, presenceEnhancedRms)));
        checks.Add(RangeRatioCheck(
            "Presence enhancer",
            "300 Hz low body stays mostly unchanged",
            Ratio(lowEnhancedRms, lowBypassRms),
            0.80d,
            1.22d,
            RmsDetails(lowBypassRms, lowEnhancedRms)));
    }

    private static void AddSaturationChecks(ICollection<DspVerificationCheck> checks)
    {
        const double fundamentalHz = 1_000d;
        var tone = GenerateSine(fundamentalHz, 0.45d, 2.0d);
        var bypass = Process(tone, _ => { });
        var warmed = Process(tone, ConfigureSaturation);

        const int start = VerificationSampleRate;
        var bypassFundamental = CalculateToneMagnitude(bypass, fundamentalHz, start);
        var warmedFundamental = CalculateToneMagnitude(warmed, fundamentalHz, start);
        var bypassThirdHarmonic = CalculateToneMagnitude(bypass, fundamentalHz * 3d, start);
        var warmedThirdHarmonic = CalculateToneMagnitude(warmed, fundamentalHz * 3d, start);
        var warmedPeak = CalculateTailPeak(warmed, start);

        checks.Add(MinRatioCheck(
            "Saturation",
            "Warmth control adds musical third harmonic",
            Ratio(warmedThirdHarmonic, Math.Max(bypassThirdHarmonic, 0.000001d)),
            5.0d,
            MagnitudeDetails(bypassThirdHarmonic, warmedThirdHarmonic)));
        checks.Add(MinRatioCheck(
            "Saturation",
            "The main 1 kHz voice tone remains present",
            Ratio(warmedFundamental, bypassFundamental),
            0.45d,
            MagnitudeDetails(bypassFundamental, warmedFundamental)));
        checks.Add(MaxValueCheck(
            "Saturation",
            "Warmth stage stays below clipping before the limiter",
            warmedPeak,
            0.98d,
            PeakDetails(warmedPeak)));
    }

    private static void AddLimiterChecks(ICollection<DspVerificationCheck> checks)
    {
        var loudTone = GenerateSine(1_000d, 0.95d, 2.0d);
        var normalTone = GenerateSine(1_000d, 0.10d, 2.0d);
        var loudBypass = Process(loudTone, settings => settings.MakeupGainDb = 12d);
        var loudLimited = Process(loudTone, settings =>
        {
            settings.MakeupGainDb = 12d;
            ConfigureLimiter(settings);
        });
        var normalBypass = Process(normalTone, _ => { });
        var normalLimited = Process(normalTone, ConfigureLimiter);

        const int start = VerificationSampleRate;
        var loudBypassPeak = CalculateTailPeak(loudBypass, start);
        var loudLimitedPeak = CalculateTailPeak(loudLimited, start);
        var normalBypassRms = CalculateTailRms(normalBypass, start);
        var normalLimitedRms = CalculateTailRms(normalLimited, start);

        checks.Add(MaxValueCheck(
            "Limiter",
            "A loud boosted signal is held near the -6 dB ceiling",
            loudLimitedPeak,
            0.58d,
            PeakDetails(loudLimitedPeak) + "; bypass peak " + FormatLinear(loudBypassPeak)));
        checks.Add(MinRatioCheck(
            "Limiter",
            "A normal signal below the ceiling is preserved",
            Ratio(normalLimitedRms, normalBypassRms),
            0.88d,
            RmsDetails(normalBypassRms, normalLimitedRms)));
    }

    private static void AddFullChainSafetyCheck(ICollection<DspVerificationCheck> checks)
    {
        var source = GenerateCompositeStressSignal();
        var processed = Process(source, settings =>
        {
            settings.InputTrimDb = 3d;
            ConfigureHighPass(settings);
            settings.LowPassEnabled = true;
            settings.LowPassFrequencyHz = 12_000d;
            settings.HumRemovalEnabled = true;
            settings.HumRemovalFrequencyHz = 60d;
            settings.NotchFilterEnabled = true;
            settings.NotchFilterFrequencyHz = 2_800d;
            settings.NotchFilterDepthDb = 12d;
            settings.NotchFilterQ = 16d;
            var gains = new double[EqualizerFrequenciesHz.Length];
            gains[3] = -3d;
            gains[7] = 2d;
            gains[10] = 1.5d;
            gains[16] = -2d;
            settings.SetEqualizerGains(gains);
            settings.ParametricEqEnabled = true;
            settings.ParametricEqFrequencyHz = 1_000d;
            settings.ParametricEqGainDb = 3d;
            settings.ParametricEqQ = 1.6d;
            ConfigureShelfEq(settings);
            ConfigureDePopper(settings);
            ConfigureNoiseGate(settings);
            ConfigureExpander(settings);
            ConfigureNormalNoiseSuppression(settings);
            ConfigureEchoReducer(settings);
            ConfigureCompressor(settings);
            ConfigureBreathReducer(settings);
            ConfigureDeEsser(settings);
            ConfigurePresenceEnhancer(settings);
            ConfigureSaturation(settings);
            settings.MakeupGainDb = 2d;
            ConfigureLimiter(settings);
        });

        var finite = processed.All(float.IsFinite);
        var peak = CalculateTailPeak(processed, 0);
        checks.Add(BooleanCheck(
            "Full custom DSP chain",
            "Combined custom DSP chain produces finite, safe audio",
            finite && peak <= 1.0d,
            "finite and peak " + FormatLinear(peak),
            "finite and peak <= 1.000",
            peak,
            "processed samples " + processed.Length.ToString(CultureInfo.InvariantCulture)));
    }

    private static void ConfigureHighPass(VoiceProcessorSettings settings)
    {
        settings.HighPassEnabled = true;
        settings.HighPassFrequencyHz = 120d;
    }

    private static void ConfigureShelfEq(VoiceProcessorSettings settings)
    {
        settings.ShelfEqEnabled = true;
        settings.LowShelfFrequencyHz = 180;
        settings.LowShelfGainDb = 6;
        settings.HighShelfFrequencyHz = 7_500;
        settings.HighShelfGainDb = -7;
    }

    private static void ConfigureDePopper(VoiceProcessorSettings settings)
    {
        settings.DePopperEnabled = true;
        settings.DePopperAmountDb = 12d;
        settings.DePopperFrequencyHz = 180d;
        settings.DePopperThresholdDb = -44d;
    }

    private static void ConfigureNoiseGate(VoiceProcessorSettings settings)
    {
        settings.NoiseGateEnabled = true;
        settings.NoiseGateThresholdDb = -42d;
        settings.NoiseGateAttackMs = 4d;
        settings.NoiseGateHoldMs = 20d;
        settings.NoiseGateReleaseMs = 120d;
        settings.NoiseGateRangeDb = 36d;
    }

    private static void ConfigureExpander(VoiceProcessorSettings settings)
    {
        settings.ExpanderEnabled = true;
        settings.ExpanderThresholdDb = -35d;
        settings.ExpanderRatio = 4d;
        settings.ExpanderRangeDb = 24d;
        settings.ExpanderAttackMs = 5d;
        settings.ExpanderHoldMs = 15d;
        settings.ExpanderReleaseMs = 160d;
    }

    private static void ConfigureAggressiveNoiseSuppression(VoiceProcessorSettings settings)
    {
        settings.NoiseSuppressionEnabled = true;
        settings.NoiseSuppressionAmountDb = 12d;
        settings.NoiseSuppressionSensitivity = 1d;
    }

    private static void ConfigureNormalNoiseSuppression(VoiceProcessorSettings settings)
    {
        settings.NoiseSuppressionEnabled = true;
        settings.NoiseSuppressionAmountDb = 6d;
        settings.NoiseSuppressionSensitivity = 10d;
    }

    private static void ConfigureEchoReducer(VoiceProcessorSettings settings)
    {
        settings.EchoReducerEnabled = true;
        settings.EchoReducerAmountDb = 12d;
        settings.EchoReducerSensitivity = 10d;
    }

    private static void ConfigureCompressor(VoiceProcessorSettings settings)
    {
        settings.CompressorEnabled = true;
        settings.CompressorThresholdDb = -24d;
        settings.CompressorRatio = 6d;
        settings.CompressorAttackMs = 3d;
        settings.CompressorReleaseMs = 90d;
        settings.CompressorKneeDb = 4d;
    }

    private static void ConfigureBreathReducer(VoiceProcessorSettings settings)
    {
        settings.BreathReducerEnabled = true;
        settings.BreathReducerAmountDb = 18d;
        settings.BreathReducerSensitivity = 10d;
    }

    private static void ConfigureDeEsser(VoiceProcessorSettings settings)
    {
        settings.DeEsserEnabled = true;
        settings.DeEsserAmountDb = 12d;
        settings.DeEsserFrequencyHz = 6_500d;
        settings.DeEsserThresholdDb = -48d;
        settings.DeEsserRangeDb = 18d;
    }

    private static void ConfigurePresenceEnhancer(VoiceProcessorSettings settings)
    {
        settings.PresenceEnhancerEnabled = true;
        settings.PresenceEnhancerAmountDb = 6d;
        settings.PresenceEnhancerFrequencyHz = 3_000d;
        settings.PresenceEnhancerWidthHz = 2_000d;
        settings.DeEsserEnabled = false;
    }

    private static void ConfigureSaturation(VoiceProcessorSettings settings)
    {
        settings.SaturationEnabled = true;
        settings.SaturationAmount = 7d;
    }

    private static void ConfigureLimiter(VoiceProcessorSettings settings)
    {
        settings.LimiterEnabled = true;
        settings.LimiterCeilingDb = -6d;
        settings.LimiterSoftClipEnabled = true;
        settings.LimiterSoftClipDriveDb = 2d;
        settings.LimiterLookaheadEnabled = true;
        settings.LimiterLookaheadMs = 3d;
        settings.LimiterReleaseMs = 60d;
    }

    private static DspVerificationCheck MaxRatioCheck(
        string effect,
        string claim,
        double ratio,
        double maximumRatio,
        string details)
    {
        return new DspVerificationCheck(
            effect,
            claim,
            FormatRatio(ratio),
            "<= " + FormatRatio(maximumRatio),
            ratio,
            ratio <= maximumRatio,
            details);
    }

    private static DspVerificationCheck MinRatioCheck(
        string effect,
        string claim,
        double ratio,
        double minimumRatio,
        string details)
    {
        return new DspVerificationCheck(
            effect,
            claim,
            FormatRatio(ratio),
            ">= " + FormatRatio(minimumRatio),
            ratio,
            ratio >= minimumRatio,
            details);
    }

    private static DspVerificationCheck RangeRatioCheck(
        string effect,
        string claim,
        double ratio,
        double minimumRatio,
        double maximumRatio,
        string details)
    {
        return new DspVerificationCheck(
            effect,
            claim,
            FormatRatio(ratio),
            string.Format(CultureInfo.InvariantCulture, "{0} to {1}", FormatRatio(minimumRatio), FormatRatio(maximumRatio)),
            ratio,
            ratio >= minimumRatio && ratio <= maximumRatio,
            details);
    }

    private static DspVerificationCheck MaxValueCheck(
        string effect,
        string claim,
        double value,
        double maximumValue,
        string details)
    {
        return new DspVerificationCheck(
            effect,
            claim,
            FormatLinear(value),
            "<= " + FormatLinear(maximumValue),
            value,
            value <= maximumValue,
            details);
    }

    private static DspVerificationCheck BooleanCheck(
        string effect,
        string claim,
        bool passed,
        string measurement,
        string requirement,
        double value,
        string details)
    {
        return new DspVerificationCheck(
            effect,
            claim,
            measurement,
            requirement,
            value,
            passed,
            details);
    }

    private static float[] Process(float[] samples, Action<VoiceProcessorSettings> configure)
    {
        var settings = CreateTransparentVoiceSettings();
        configure(settings);
        var processor = new VoiceSampleProcessor(settings, VerificationSampleRate);
        return processor.Process(samples);
    }

    private static VoiceProcessorSettings CreateTransparentVoiceSettings()
    {
        return new VoiceProcessorSettings
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
    }

    private static float[] GenerateSine(double frequencyHz, double amplitude, double durationSeconds)
    {
        var sampleCount = Math.Max(1, (int)(VerificationSampleRate * durationSeconds));
        var samples = new float[sampleCount];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(Math.Sin(2d * Math.PI * frequencyHz * i / VerificationSampleRate) * amplitude);
        }

        return samples;
    }

    private static float[] GenerateNoise(double amplitude, double durationSeconds)
    {
        var sampleCount = Math.Max(1, (int)(VerificationSampleRate * durationSeconds));
        var samples = new float[sampleCount];
        var state = 0x1234ABCDu;
        for (var i = 0; i < samples.Length; i++)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            var value = ((state >> 8) / (double)0xFFFFFF) * 2d - 1d;
            samples[i] = (float)(value * amplitude);
        }

        return samples;
    }

    private static float[] GenerateEchoTail()
    {
        var samples = new float[VerificationSampleRate];
        for (var i = 0; i < samples.Length; i++)
        {
            var time = i / (double)VerificationSampleRate;
            var amplitude = time < 0.025d
                ? 0.85d
                : 0.08d * Math.Exp(-(time - 0.025d) * 4.0d);
            samples[i] = (float)(Math.Sin(2d * Math.PI * 1_000d * time) * amplitude);
        }

        return samples;
    }

    private static float[] GenerateCompositeStressSignal()
    {
        var samples = new float[VerificationSampleRate * 2];
        var noise = GenerateNoise(0.018d, 2.0d);
        for (var i = 0; i < samples.Length; i++)
        {
            var time = i / (double)VerificationSampleRate;
            var pulsePosition = i % (VerificationSampleRate / 5);
            var pulse = pulsePosition < 220
                ? 0.45d * Math.Exp(-pulsePosition / 80d)
                : 0d;
            var value =
                Math.Sin(2d * Math.PI * 140d * time) * 0.16d
                + Math.Sin(2d * Math.PI * 1_000d * time) * 0.24d
                + Math.Sin(2d * Math.PI * 6_400d * time) * 0.10d
                + pulse
                + noise[i];
            samples[i] = (float)Math.Clamp(value, -1d, 1d);
        }

        return samples;
    }

    private static double CalculateTailRms(IReadOnlyList<float> samples, int startIndex)
    {
        return CalculateSegmentRms(samples, startIndex, samples.Count - Math.Clamp(startIndex, 0, samples.Count));
    }

    private static double CalculateSegmentRms(IReadOnlyList<float> samples, int startIndex, int length)
    {
        var start = Math.Clamp(startIndex, 0, Math.Max(0, samples.Count - 1));
        var end = Math.Clamp(start + Math.Max(0, length), start, samples.Count);
        var sum = 0d;
        var count = 0;
        for (var i = start; i < end; i++)
        {
            var value = samples[i];
            sum += value * value;
            count++;
        }

        return count == 0 ? 0d : Math.Sqrt(sum / count);
    }

    private static double CalculateTailPeak(IReadOnlyList<float> samples, int startIndex)
    {
        var start = Math.Clamp(startIndex, 0, Math.Max(0, samples.Count - 1));
        var peak = 0d;
        for (var i = start; i < samples.Count; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }

    private static double CalculateToneMagnitude(IReadOnlyList<float> samples, double frequencyHz, int startIndex)
    {
        var start = Math.Clamp(startIndex, 0, Math.Max(0, samples.Count - 1));
        var count = samples.Count - start;
        if (count <= 0)
        {
            return 0d;
        }

        var sine = 0d;
        var cosine = 0d;
        var angular = 2d * Math.PI * frequencyHz / VerificationSampleRate;
        for (var i = start; i < samples.Count; i++)
        {
            var phase = angular * (i - start);
            var sample = samples[i];
            sine += sample * Math.Sin(phase);
            cosine += sample * Math.Cos(phase);
        }

        return 2d * Math.Sqrt(sine * sine + cosine * cosine) / count;
    }

    private static double Ratio(double processedRms, double bypassRms)
    {
        return bypassRms <= 1e-12d ? 0d : processedRms / bypassRms;
    }

    private static string RmsDetails(double bypassRms, double processedRms)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "bypass RMS {0:0.000000}, processed RMS {1:0.000000}",
            bypassRms,
            processedRms);
    }

    private static string MagnitudeDetails(double bypassMagnitude, double processedMagnitude)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "bypass magnitude {0:0.000000}, processed magnitude {1:0.000000}",
            bypassMagnitude,
            processedMagnitude);
    }

    private static string PeakDetails(double peak)
    {
        return string.Format(CultureInfo.InvariantCulture, "processed peak {0:0.000000}", peak);
    }

    private static string FormatRatio(double ratio)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:0.000}x ({1})", ratio, FormatDb(RatioToDb(ratio)));
    }

    private static string FormatLinear(double value)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:0.000}", value);
    }

    private static double RatioToDb(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio <= 1e-12d)
        {
            return -120d;
        }

        return 20d * Math.Log10(ratio);
    }

    private static string FormatDb(double db)
    {
        return db >= 0d
            ? string.Format(CultureInfo.InvariantCulture, "+{0:0.0} dB", db)
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0} dB", db);
    }

    private static string EscapeMarkdownCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }
}
