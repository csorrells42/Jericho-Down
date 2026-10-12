using JerichoDown.Modules.Audio.Dsp;
using JerichoDown.Modules.Mixer;
using JerichoDown.Modules.Midi;
using JerichoDown.Modules.Audio.Devices;
using JerichoDown.Modules.Audio.Live;
using NAudio.Midi;
using NAudio.Wave;

internal static class ReviewRegressionTests
{
    public static void ShelfExtremesRemainAudible()
    {
        foreach (var lowShelf in new[] { true, false })
        foreach (var gain in new[] { -24d, 24d })
        {
            var settings = new VoiceProcessorSettings
            {
                NAudioLowShelfEnabled = lowShelf,
                NAudioHighShelfEnabled = !lowShelf,
                NAudioLowShelfGainDb = gain,
                NAudioHighShelfGainDb = gain,
                NAudioLowShelfSlope = 4,
                NAudioHighShelfSlope = 4
            };
            var rack = new NAudioBiQuadFilterRack(48000);
            rack.UpdateFromSettings(settings);
            var output = Enumerable.Range(0, 4096)
                .Select(i => rack.Transform(0.01 * Math.Sin(2 * Math.PI * 1000 * i / 48000))).ToArray();
            if (output.Any(sample => !double.IsFinite(sample)) || output.Max(Math.Abs) < 0.00001)
                throw new InvalidOperationException($"Shelf at {gain} dB silenced the signal (low shelf: {lowShelf}).");
        }
    }

    public static void MidiBankPrecedesPatch()
    {
        var events = new MidiEventCollection(1, 480);
        events.AddEvent(new ControlChangeEvent(0, 1, MidiController.BankSelect, 1), 0);
        events.AddEvent(new ControlChangeEvent(0, 1, MidiController.BankSelectLsb, 2), 0);
        events.AddEvent(new PatchChangeEvent(0, 1, 7), 0);
        var plan = MidiSequenceService.CreatePlaybackPlan(events, "bank.mid");
        if ((plan.Events[0].RawMessage & 0xF0) != 0xB0 || (plan.Events[2].RawMessage & 0xF0) != 0xC0)
            throw new InvalidOperationException("Bank selection was reordered after the patch change.");
    }

    public static void SessionWritesRecoverableHeaders()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"jericho-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using var service = new MicrophoneSpectrumService();
            service.Start(AudioInputDevice.CreateStereoTestTone());
            var mix = Path.Combine(folder, "mix.wav");
            var raw = Path.Combine(folder, "raw.wav");
            service.StartSessionAudioRecording(mix, raw);
            Thread.Sleep(2400);
            foreach (var path in new[] { mix, raw })
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new WaveFileReader(stream);
                if (reader.TotalTime.TotalSeconds < 1)
                    throw new InvalidOperationException("Active session WAV has no recoverable audio header.");
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

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
