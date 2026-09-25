using System.IO;

namespace JerichoDown.Modules.SessionPlayback;

public static class SessionPlaybackAudioResolver
{
    public static string ResolveAudioPlaybackPath(string videoPath)
    {
        var directory = Path.GetDirectoryName(videoPath);
        var fileName = Path.GetFileName(videoPath);
        if (!string.IsNullOrWhiteSpace(directory)
            && fileName.StartsWith("video_", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            var number = fileName.Substring(
                "video_".Length,
                fileName.Length - "video_".Length - ".mp4".Length);
            if (number.Length > 0 && number.All(char.IsDigit))
            {
                foreach (var audioFileName in new[] { $"mix_{number}.wav", $"raw_backup_{number}.wav" })
                {
                    var audioPath = Path.Combine(directory, audioFileName);
                    if (File.Exists(audioPath))
                    {
                        return audioPath;
                    }
                }
            }
        }

        return videoPath;
    }
}
