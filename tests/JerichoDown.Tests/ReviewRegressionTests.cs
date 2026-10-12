using JerichoDown.Modules.Audio.Dsp;
using JerichoDown.Modules.Mixer;

internal static class ReviewRegressionTests
{
    public static void VoiceProcessingSupportsLowSampleRates()
    {
        foreach (var rate in new[] { 8000, 11025, 12000, 16000, 44100, 48000, 96000 })
        {
            var processor = new VoiceSampleProcessor(new VoiceProcessorSettings(), rate);
            var input = Enumerable.Range(0, 256).Select(i => (float)(0.1 * Math.Sin(2 * Math.PI * 400 * i / rate))).ToArray();
            var output = processor.Process(input);
            if (output.Length != input.Length || output.Any(sample => !float.IsFinite(sample)))
                throw new InvalidOperationException($"DSP failed at {rate} Hz.");
        }
    }

    public static void MixerLimiterHonorsCeiling()
    {
        foreach (var ceilingDb in new[] { -24d, -12d, -6d, -1d, 0d })
        {
            var processor = new MixBusProcessor();
            float[] input = [0.8f, -0.8f, 0.05f, -0.05f];
            var output = new float[input.Length];
            processor.Process(input, output, new MixBusSettings(200, false, true, ceilingDb));
            var ceiling = Math.Pow(10, ceilingDb / 20);
            if (output.Any(sample => !float.IsFinite(sample) || Math.Abs(sample) > ceiling + 0.000001))
                throw new InvalidOperationException($"Mixer exceeded its {ceilingDb} dB ceiling: {output.Max(Math.Abs)}.");
        }
    }

    public static void MixerRejectsNonFiniteCeiling()
    {
        var processor = new MixBusProcessor();
        var output = new float[2];
        processor.Process(new float[] { 0.5f, -0.5f }, output,
            new MixBusSettings(100, false, true, double.NaN));
        if (output.Any(sample => !float.IsFinite(sample)))
            throw new InvalidOperationException("Invalid limiter settings poisoned mixer output.");
    }
}
