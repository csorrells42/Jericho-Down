using JerichoDown.Audio;

internal static class RecordingSafetyTests
{
    public static void SessionAudioPausesAndFinalizesOnStop()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"jericho-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var mix = Path.Combine(folder, "mix.wav");
        var raw = Path.Combine(folder, "raw.wav");
        try
        {
            using var service = new MicrophoneSpectrumService();
            service.Start(AudioInputDevice.CreateStereoTestTone());
            service.StartSessionAudioRecording(mix, raw);
            Thread.Sleep(150);
            service.PauseSessionAudioRecording();
            Thread.Sleep(400);
            service.ResumeSessionAudioRecording();
            Thread.Sleep(150);
            service.Stop();
            if (service.IsSessionAudioRecording)
                throw new InvalidOperationException("Capture stop left the session recorder running.");
            using var mixReader = new NAudio.Wave.AudioFileReader(mix);
            using var rawReader = new NAudio.Wave.AudioFileReader(raw);
            if (mixReader.TotalTime.TotalSeconds < 0.1 || mixReader.TotalTime.TotalSeconds > 0.55)
                throw new InvalidOperationException("Session recording did not exclude its pause interval.");
            if (Math.Abs(mixReader.TotalTime.TotalSeconds - rawReader.TotalTime.TotalSeconds) > 0.001)
                throw new InvalidOperationException("Processed and raw session audio have different durations.");
            var samples = new float[48000];
            var count = mixReader.Read(samples, 0, samples.Length);
            if (!samples.Take(count).Any(sample => Math.Abs(sample) > 0.001f))
                throw new InvalidOperationException("Session mix did not contain live generated audio.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    public static void NaturalSessionWritesRawOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jericho-natural-{Guid.NewGuid():N}.wav");
        try
        {
            using (var service = new MicrophoneSpectrumService())
            {
                service.Start(AudioInputDevice.CreateStereoTestTone());
                service.StartSessionAudioRecording(null, path);
                Thread.Sleep(150);
            }
            using var reader = new NAudio.Wave.AudioFileReader(path);
            if (reader.TotalTime <= TimeSpan.Zero || reader.WaveFormat.Channels != 2)
                throw new InvalidOperationException("Natural session audio was not finalized on disposal.");
        }
        finally { File.Delete(path); }
    }

    public static void RecordingPreservesExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jericho-existing-{Guid.NewGuid():N}.wav");
        byte[] original = [1, 2, 3, 4, 5];
        File.WriteAllBytes(path, original);
        try
        {
            using var service = new MicrophoneSpectrumService();
            try
            {
                service.StartProcessedAudioRecording(path);
                throw new InvalidOperationException("Recording accepted an existing destination.");
            }
            catch (IOException) { }
            if (!File.ReadAllBytes(path).SequenceEqual(original))
                throw new InvalidOperationException("Recording modified the existing file.");
        }
        finally { File.Delete(path); }
    }

    public static void ExportProtectsNormalizedSourcePath()
    {
        var source = Path.Combine(Path.GetTempPath(), $"jericho-export-{Guid.NewGuid():N}.mp3");
        byte[] original = [1, 2, 3, 4, 5];
        File.WriteAllBytes(source, original);
        try
        {
            try
            {
                AudioRecordingExporter.Export(source, Path.ChangeExtension(source, ".wav"), AudioRecordingExportFormat.Mp3);
                throw new InvalidOperationException("Export accepted its source as the normalized destination.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("original recording", StringComparison.Ordinal)) { }
            if (!File.ReadAllBytes(source).SequenceEqual(original))
                throw new InvalidOperationException("Export modified the original file.");
        }
        finally { File.Delete(source); }
    }
}
