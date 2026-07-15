using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JerichoDown.Modules.SessionPlayback;

public static class SessionRecordingCatalog
{
    private static readonly Regex PodcastSessionFolderRegex = new(@"^Podcast_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex NumberedRecordingFileRegex = new(@"^(?:video|mix|raw_backup)_(?<number>\d{3,})\.(?:mp4|wav)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SessionRecordingTarget CreateRecordingTarget(string outputFolder)
    {
        var sessionFolder = ResolvePodcastSessionFolderForRecording(outputFolder);
        Directory.CreateDirectory(sessionFolder);
        var setNumber = GetNextRecordingSetNumber(sessionFolder);
        return new SessionRecordingTarget(sessionFolder, setNumber);
    }

    public static string ResolvePodcastSessionFolderForRecording(string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);

        if (IsPodcastSessionFolder(outputFolder))
        {
            return outputFolder;
        }

        var now = DateTime.Now;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var timestamp = attempt == 0
                ? now
                : now.AddSeconds(attempt);
            var sessionFolder = Path.Combine(outputFolder, CreatePodcastSessionFolderName(timestamp));
            if (Directory.Exists(sessionFolder))
            {
                continue;
            }

            Directory.CreateDirectory(sessionFolder);
            return sessionFolder;
        }

        var fallbackFolder = Path.Combine(outputFolder, $"Podcast_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallbackFolder);
        return fallbackFolder;
    }

    public static string ResolvePodcastSessionFolderForPreview(string outputFolder)
    {
        return IsPodcastSessionFolder(outputFolder)
            ? outputFolder
            : Path.Combine(outputFolder, CreatePodcastSessionFolderName(DateTime.Now));
    }

    public static string CreatePodcastSessionFolderName(DateTime timestamp)
    {
        return $"Podcast_{timestamp:yyyy-MM-dd_HH-mm-ss}";
    }

    public static bool IsPodcastSessionFolder(string folder)
    {
        return PodcastSessionFolderRegex.IsMatch(Path.GetFileName(folder.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)));
    }

    public static int GetNextRecordingSetNumber(string sessionFolder)
    {
        if (!Directory.Exists(sessionFolder))
        {
            return 1;
        }

        var highest = Directory.EnumerateFiles(sessionFolder)
            .Select(file => NumberedRecordingFileRegex.Match(Path.GetFileName(file)))
            .Where(match => match.Success)
            .Select(match => int.TryParse(match.Groups["number"].Value, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return highest + 1;
    }

    public static string FormatRecordingSetNumber(int setNumber)
    {
        return setNumber.ToString("000");
    }

    public static string FormatRecordingFileSet(int setNumber)
    {
        var number = FormatRecordingSetNumber(setNumber);
        return $"video_{number}.mp4{Environment.NewLine}mix_{number}.wav{Environment.NewLine}raw_backup_{number}.wav{Environment.NewLine}session.json";
    }

    public static IReadOnlyList<SessionRecordingItem> EnumerateSessionRecordings(string outputFolder)
    {
        if (!Directory.Exists(outputFolder))
        {
            return [];
        }

        IEnumerable<string> folders = IsPodcastSessionFolder(outputFolder)
            ? [outputFolder]
            : Directory.EnumerateDirectories(outputFolder)
                .Where(IsPodcastSessionFolder);
        var items = new List<SessionRecordingItem>();
        foreach (var folder in folders)
        {
            foreach (var path in Directory.EnumerateFiles(folder, "video_*.mp4"))
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    continue;
                }

                var metadata = ReadSessionMetadata(folder, file.Name);
                var sessionName = Path.GetFileName(folder);
                var displayName = metadata.SetNumber > 0
                    ? $"{sessionName}  set {FormatRecordingSetNumber(metadata.SetNumber)}"
                    : $"{sessionName}  {file.Name}";
                var cameraText = string.IsNullOrWhiteSpace(metadata.Camera) ? "Camera unknown" : metadata.Camera;
                var durationText = string.IsNullOrWhiteSpace(metadata.Duration) ? "Duration unknown" : metadata.Duration;
                var details = $"{file.LastWriteTime:g}    {FormatFileSize(file.Length)}    {durationText}    {cameraText}";
                items.Add(new SessionRecordingItem(
                    file.FullName,
                    folder,
                    displayName,
                    details,
                    file.LastWriteTimeUtc));
            }
        }

        return items;
    }

    public static bool IsSessionBrowserPath(string path)
    {
        if (Directory.Exists(path))
        {
            return IsPodcastSessionFolder(path);
        }

        return Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("session.json", StringComparison.OrdinalIgnoreCase);
    }

    private static SessionMetadataSummary ReadSessionMetadata(string folder, string videoFileName)
    {
        var metadataPath = Path.Combine(folder, "session.json");
        if (!File.Exists(metadataPath))
        {
            return new SessionMetadataSummary(0, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            var video = TryGetJsonString(root, "video");
            if (!string.IsNullOrWhiteSpace(video)
                && !video.Equals(videoFileName, StringComparison.OrdinalIgnoreCase))
            {
                return new SessionMetadataSummary(0, null, null);
            }

            var setNumber = root.TryGetProperty("setNumber", out var setProperty)
                && setProperty.TryGetInt32(out var number)
                    ? number
                    : 0;
            return new SessionMetadataSummary(
                setNumber,
                TryGetJsonString(root, "camera"),
                TryGetJsonString(root, "duration"));
        }
        catch
        {
            return new SessionMetadataSummary(0, null, null);
        }
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string FormatFileSize(long byteCount)
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

    private sealed record SessionMetadataSummary(int SetNumber, string? Camera, string? Duration);
}

public sealed record SessionRecordingTarget(string SessionFolder, int SetNumber);

public sealed record SessionRecordingItem(string Path, string SessionFolder, string Name, string Details, DateTime LastWriteTimeUtc)
{
    public override string ToString() => Name;
}
