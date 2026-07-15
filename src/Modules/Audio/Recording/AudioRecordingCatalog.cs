using System.IO;

namespace JerichoDown.Modules.Audio.Recording;

public static class AudioRecordingCatalog
{
    public static readonly string[] SupportedRecordingExtensions =
    [
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".mp4",
        ".flac",
        ".aiff",
        ".aif",
        ".wma"
    ];

    public static string CreateRecordingFileName(
        DateTime timestamp,
        ProcessedRecordingSource source,
        int channelNumber)
    {
        var sourceSlug = source switch
        {
            ProcessedRecordingSource.SelectedMicProcessed => $"mic{Math.Max(1, channelNumber)}_processed",
            ProcessedRecordingSource.SelectedMicRawBackup => $"mic{Math.Max(1, channelNumber)}_raw_backup",
            _ => "program_mix"
        };

        return $"jericho_{sourceSlug}_{timestamp:yyyy-MM-dd_HH-mm-ss}.wav";
    }

    public static string CreateRecordingFilePath(
        string recordingFolder,
        DateTime timestamp,
        ProcessedRecordingSource source,
        int channelNumber)
    {
        Directory.CreateDirectory(recordingFolder);
        var baseName = Path.GetFileNameWithoutExtension(CreateRecordingFileName(
            timestamp,
            source,
            channelNumber));
        var path = Path.Combine(recordingFolder, $"{baseName}.wav");
        for (var attempt = 1; File.Exists(path) && attempt < 100; attempt++)
        {
            path = Path.Combine(recordingFolder, $"{baseName}_{attempt:00}.wav");
        }

        return path;
    }

    public static IReadOnlyList<AudioRecordingFileItem> EnumerateRecordingFiles(
        string recordingFolder,
        int maximumAnalyzedRows,
        TimeSpan eagerAnalysisDuration)
    {
        return Directory.Exists(recordingFolder)
            ? Directory.EnumerateFiles(recordingFolder)
                .Where(IsSupportedRecordingFile)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select((file, index) => CreateFileItem(file, index < maximumAnalyzedRows, eagerAnalysisDuration))
                .ToList()
            : [];
    }

    public static AudioRecordingFileItem CreateFileItem(FileInfo file, bool analyze, TimeSpan eagerAnalysisDuration)
    {
        AudioFileAnalysis? analysis = null;
        var details = $"{file.LastWriteTime:g}    {FormatFileSize(file.Length)}";
        if (analyze && AudioFileAnalyzer.TryAnalyze(file.FullName, out var fileAnalysis, out _, eagerAnalysisDuration))
        {
            analysis = fileAnalysis;
            details = $"{details}    {fileAnalysis.BrowserSummary}";
        }

        return new AudioRecordingFileItem(file.FullName, file.Name, details, analysis);
    }

    public static bool IsSupportedRecordingFile(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedRecordingExtensions.Any(supportedExtension =>
            supportedExtension.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatFileSize(long byteCount)
    {
        if (byteCount < 1024)
        {
            return $"{byteCount} B";
        }

        var kib = byteCount / 1024d;
        if (kib < 1024d)
        {
            return $"{kib:0.0} KB";
        }

        return $"{kib / 1024d:0.0} MB";
    }
}

public sealed record AudioRecordingFileItem(string Path, string Name, string Details, AudioFileAnalysis? Analysis);
