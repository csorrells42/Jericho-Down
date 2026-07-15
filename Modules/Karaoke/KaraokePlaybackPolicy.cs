using System.IO;
using System.Runtime.InteropServices;

namespace JerichoDown.Modules.Karaoke;

public static class KaraokePlaybackPolicy
{
    private static readonly string[] SupportedTrackExtensionsCore =
    [
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".wma",
        ".flac",
        ".aiff",
        ".aif"
    ];

    public static IReadOnlyList<string> SupportedTrackExtensions { get; } =
        Array.AsReadOnly(SupportedTrackExtensionsCore);

    public static TimeSpan ReplayFromEndTolerance { get; } = TimeSpan.FromMilliseconds(250);

    public static string CreateOpenFileFilter()
    {
        return $"Audio files|{string.Join(';', SupportedTrackExtensionsCore.Select(extension => $"*{extension}"))}|All files|*.*";
    }

    public static bool IsSupportedTrackFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return SupportedTrackExtensionsCore.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static TimeSpan ResolvePlaybackStartPosition(double requestedSeconds, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var clampedSeconds = Math.Clamp(requestedSeconds, 0d, duration.TotalSeconds);
        var position = TimeSpan.FromSeconds(clampedSeconds);
        return ShouldRestartFromEnd(position, duration)
            ? TimeSpan.Zero
            : position;
    }

    public static bool ShouldRestartFromEnd(TimeSpan position, TimeSpan duration)
    {
        return duration > TimeSpan.Zero
            && position >= TimeSpan.Zero
            && duration - position <= ReplayFromEndTolerance;
    }

    public static bool ShouldTryMediaFallbackAfterSampleReaderFailure(string? path, Exception exception)
    {
        return IsSupportedTrackFile(path)
            && IsSampleReaderCodecFailure(exception);
    }

    public static bool IsSampleReaderCodecFailure(Exception exception)
    {
        return exception is NotSupportedException
            || exception is COMException
            || (exception is InvalidCastException
                && exception.Message.Contains("COM object", StringComparison.OrdinalIgnoreCase))
            || exception.Message.Contains("IMFSourceReader", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("MediaFoundation", StringComparison.OrdinalIgnoreCase)
            || (exception.InnerException is not null && IsSampleReaderCodecFailure(exception.InnerException));
    }
}
