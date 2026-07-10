using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace JerichoDown.Audio;

public enum AudioRecordingExportFormat
{
    Mp3,
    Aac,
    Wma
}

public sealed record AudioRecordingExportFormatInfo(
    AudioRecordingExportFormat Format,
    string DisplayName,
    string Extension,
    string SaveDialogFilter,
    int DefaultBitRate);

public static class AudioRecordingExporter
{
    public const int DefaultCompressedBitRate = 192_000;

    public static readonly IReadOnlyList<AudioRecordingExportFormatInfo> ExportFormats =
    [
        new(AudioRecordingExportFormat.Mp3, "MP3", ".mp3", "MP3 audio|*.mp3", DefaultCompressedBitRate),
        new(AudioRecordingExportFormat.Aac, "AAC", ".mp4", "AAC audio|*.mp4;*.aac", DefaultCompressedBitRate),
        new(AudioRecordingExportFormat.Wma, "WMA", ".wma", "Windows Media Audio|*.wma", DefaultCompressedBitRate)
    ];

    public static AudioRecordingExportFormatInfo GetFormatInfo(AudioRecordingExportFormat format)
    {
        return ExportFormats.First(info => info.Format == format);
    }

    public static bool TryGetFormatForExtension(string path, out AudioRecordingExportFormatInfo info)
    {
        var extension = Path.GetExtension(path);
        var match = ExportFormats.FirstOrDefault(candidate =>
            candidate.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)
            || (candidate.Format == AudioRecordingExportFormat.Aac && extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)));
        if (match is null)
        {
            info = null!;
            return false;
        }

        info = match;
        return true;
    }

    public static bool IsSupportedExportExtension(string path)
    {
        return TryGetFormatForExtension(path, out _);
    }

    public static string GetDefaultExportPath(string sourcePath, AudioRecordingExportFormat format)
    {
        var info = GetFormatInfo(format);
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, $"{fileName}_export{info.Extension}");
    }

    public static bool IsEncoderAvailable(AudioRecordingExportFormat format, WaveFormat? inputFormat = null, int? bitRate = null)
    {
        inputFormat ??= new WaveFormat(44_100, 16, 2);
        object? mediaType = null;
        try
        {
            mediaType = MediaFoundationEncoder.SelectMediaType(
                GetAudioSubtype(format),
                inputFormat,
                bitRate ?? GetFormatInfo(format).DefaultBitRate);
            return mediaType is not null;
        }
        catch
        {
            return false;
        }
        finally
        {
            (mediaType as IDisposable)?.Dispose();
        }
    }

    public static void Export(string sourcePath, string targetPath, AudioRecordingExportFormat format, int? bitRate = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Choose a saved recording to export.", sourcePath);
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Choose a valid export path.", nameof(targetPath));
        }

        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Export to a new file so the original recording stays intact.");
        }

        var info = GetFormatInfo(format);
        if (!IsSupportedExportExtension(targetPath))
        {
            targetPath = Path.ChangeExtension(targetPath, info.Extension);
        }

        var targetDirectory = Path.GetDirectoryName(targetPath) ?? ".";
        Directory.CreateDirectory(targetDirectory);
        var tempPath = Path.Combine(
            targetDirectory,
            $"{Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}{Path.GetExtension(targetPath)}");
        File.Delete(tempPath);
        try
        {
            using var reader = new AudioFileReader(sourcePath);
            var provider = reader.ToWaveProvider16();
            var exportBitRate = bitRate ?? info.DefaultBitRate;
            switch (format)
            {
                case AudioRecordingExportFormat.Mp3:
                    MediaFoundationEncoder.EncodeToMp3(provider, tempPath, exportBitRate);
                    break;
                case AudioRecordingExportFormat.Aac:
                    MediaFoundationEncoder.EncodeToAac(provider, tempPath, exportBitRate);
                    break;
                case AudioRecordingExportFormat.Wma:
                    MediaFoundationEncoder.EncodeToWma(provider, tempPath, exportBitRate);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.");
            }

            File.Delete(targetPath);
            File.Move(tempPath, targetPath);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private static Guid GetAudioSubtype(AudioRecordingExportFormat format)
    {
        return format switch
        {
            AudioRecordingExportFormat.Mp3 => AudioSubtypes.MFAudioFormat_MP3,
            AudioRecordingExportFormat.Aac => AudioSubtypes.MFAudioFormat_AAC,
            AudioRecordingExportFormat.Wma => AudioSubtypes.MFAudioFormat_WMAudioV9,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
        };
    }
}
