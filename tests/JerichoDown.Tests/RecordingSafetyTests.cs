using JerichoDown.Audio;

internal static class RecordingSafetyTests
{
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
