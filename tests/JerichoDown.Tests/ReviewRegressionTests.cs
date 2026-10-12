using JerichoDown.Modules.Audio.Dsp;
using JerichoDown.Modules.Mixer;
using JerichoDown.Modules.Midi;
using JerichoDown.Modules.Audio.Devices;
using JerichoDown.Modules.Audio.Live;
using NAudio.Midi;
using NAudio.Wave;
using JerichoDown.Modules.SessionPlayback;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;

internal static class ReviewRegressionTests
{
    public static void RecordingDeletionRejectsJunctionAncestors()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"jericho-junction-{Guid.NewGuid():N}");
        var root = Path.Combine(folder, "recordings");
        var outside = Path.Combine(folder, "outside");
        var junction = Path.Combine(root, "linked");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(outside, "nested"));
        File.WriteAllText(Path.Combine(outside, "nested", "clip.wav"), "preserve");
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("Could not create the isolated test junction.");
            var safety = typeof(VoiceSampleProcessor).Assembly.GetType("JerichoDown.PathSafety")!;
            var fileCheck = safety.GetMethod("IsRegularFileUnderFolder")!;
            var directoryCheck = safety.GetMethod("IsDirectoryUnderFolder")!;
            if ((bool)fileCheck.Invoke(null, [Path.Combine(junction, "nested", "clip.wav"), root, new[] { ".wav" }])!
                || (bool)directoryCheck.Invoke(null, [Path.Combine(junction, "nested"), root, false])!)
                throw new InvalidOperationException("Deletion validation accepted a path through an external junction.");
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(folder, recursive: true);
        }
    }

    public static void SessionCatalogRetainsEverySegmentMetadata()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"jericho-segments-{Guid.NewGuid():N}");
        var session = Path.Combine(folder, "Podcast_2026-10-12_10-00-00");
        Directory.CreateDirectory(session);
        try
        {
            for (var set = 1; set <= 2; set++)
            {
                var name = $"video_{set:000}";
                File.WriteAllBytes(Path.Combine(session, name + ".mp4"), [0]);
                File.WriteAllText(Path.Combine(session, name + ".json"), JsonSerializer.Serialize(new
                    { video = name + ".mp4", setNumber = set, duration = "00:00:05", camera = "Test camera" }));
            }
            File.Copy(Path.Combine(session, "video_002.json"), Path.Combine(session, "session.json"));
            var recordings = SessionRecordingCatalog.EnumerateSessionRecordings(folder);
            if (recordings.Count != 2 || recordings.Any(item => !item.Details.Contains("Test camera") || !item.Details.Contains("00:00:05")))
                throw new InvalidOperationException("An earlier recording segment lost its metadata.");
            File.Delete(Path.Combine(session, "video_002.json"));
            if (!SessionRecordingCatalog.EnumerateSessionRecordings(folder)
                .Single(item => item.Path.EndsWith("video_002.mp4")).Details.Contains("Test camera"))
                throw new InvalidOperationException("Legacy session metadata is no longer readable.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    public static void MidiFailedExportPreservesDestination()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jericho-midi-{Guid.NewGuid():N}.mid");
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(path, original);
        try
        {
            var events = new MidiEventCollection(1, 480);
            events.AddEvent(new FailingExportEvent(), 0);
            try { MidiFileService.Export(path, events); }
            catch (IOException) { }
            if (!File.ReadAllBytes(path).SequenceEqual(original))
                throw new InvalidOperationException("A failed MIDI export destroyed the existing destination.");
        }
        finally { File.Delete(path); }
    }

    public static void PlaybackRetainsStoppingWorker()
    {
        using var service = new MediaFoundationFilePlaybackService();
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var taskField = typeof(MediaFoundationFilePlaybackService).GetField("_playbackTask", flags)!;
        typeof(MediaFoundationFilePlaybackService).GetField("_cancellation", flags)!
            .SetValue(service, new CancellationTokenSource());
        taskField.SetValue(service, worker.Task);
        try
        {
            service.Stop();
            if (!ReferenceEquals(taskField.GetValue(service), worker.Task))
                throw new InvalidOperationException("Stop forgot an unfinished playback worker, allowing overlapping sessions.");
        }
        finally { worker.SetResult(); }
    }

    private sealed class FailingExportEvent : MidiEvent
    {
        public FailingExportEvent() : base(0, 1, MidiCommandCode.NoteOn) { }
        public override void Export(ref long absoluteTime, BinaryWriter writer)
            => throw new IOException("Injected export failure.");
    }

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
