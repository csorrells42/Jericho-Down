using System.Globalization;
using System.Reflection;
using System.Text;

namespace JerichoDown.Audio;

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

    public static DspVerificationReport Run()
    {
        var checks = new List<DspVerificationCheck>();
        AddLowPassChecks(checks);
        AddHumRemovalChecks(checks);
        AddNotchFilterChecks(checks);
        AddParametricEqChecks(checks);
        AddShelfEqChecks(checks);

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
        builder.AppendLine("Jericho Down generated known sine-wave test signals, ran them through the currently compiled voice DSP processor, and compared processed RMS level against a bypassed version after filter settling time. These checks prove the measured behavior of the custom EQ/DSP code in this build.");
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

    private static void ConfigureShelfEq(VoiceProcessorSettings settings)
    {
        settings.ShelfEqEnabled = true;
        settings.LowShelfFrequencyHz = 180;
        settings.LowShelfGainDb = 6;
        settings.HighShelfFrequencyHz = 7_500;
        settings.HighShelfGainDb = -7;
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

    private static double CalculateTailRms(IReadOnlyList<float> samples, int startIndex)
    {
        var start = Math.Clamp(startIndex, 0, Math.Max(0, samples.Count - 1));
        var sum = 0d;
        var count = 0;
        for (var i = start; i < samples.Count; i++)
        {
            var value = samples[i];
            sum += value * value;
            count++;
        }

        return count == 0 ? 0d : Math.Sqrt(sum / count);
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

    private static string FormatRatio(double ratio)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:0.000}x ({1})", ratio, FormatDb(RatioToDb(ratio)));
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
