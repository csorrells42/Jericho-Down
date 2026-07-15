using System.IO;
using System.Text.Json;
using JerichoDown.Modules.Webcam;

namespace JerichoDown.Video;

internal static class CameraProfileStore
{
    public static CameraProfile Capture(
        string name,
        CameraDevice? camera,
        CameraVideoMode mode,
        bool cameraEnabled,
        bool denoiseEnabled,
        double denoiseStrength,
        bool colorPolishEnabled,
        VideoFrameColorSettings colorSettings)
    {
        return new CameraProfile
        {
            Name = name,
            CameraName = camera?.Name,
            CameraSource = camera?.Source,
            CameraDevicePath = camera?.DevicePath,
            CameraEnabled = cameraEnabled,
            ModeLabel = mode.Label,
            ModeWidth = mode.Width,
            ModeHeight = mode.Height,
            ModeFramesPerSecond = mode.FramesPerSecond,
            ModeInputFormat = mode.InputFormat,
            DenoiseEnabled = denoiseEnabled,
            DenoiseStrength = denoiseStrength,
            ColorPolishEnabled = colorPolishEnabled,
            Exposure = colorSettings.Exposure,
            Contrast = colorSettings.Contrast,
            Saturation = colorSettings.Saturation,
            Warmth = colorSettings.Warmth
        };
    }

    public static void Save(
        string profileFolder,
        string name,
        CameraProfile profile,
        JsonSerializerOptions jsonOptions)
    {
        Directory.CreateDirectory(profileFolder);
        var json = JsonSerializer.Serialize(profile, jsonOptions);
        JerichoDown.AtomicFile.WriteAllText(GetPath(profileFolder, name), json);
    }

    public static CameraProfile? Load(
        string profileFolder,
        string name,
        JsonSerializerOptions jsonOptions)
    {
        var path = GetPath(profileFolder, name);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CameraProfile>(json, jsonOptions);
    }

    public static CameraDevice? FindCamera(IEnumerable<CameraDevice> cameras, CameraProfile profile)
    {
        return CameraSourceSelection.FindCamera(
            cameras.ToList(),
            profile.CameraDevicePath,
            profile.CameraSource,
            profile.CameraName);
    }

    public static string GetPath(string profileFolder, string name)
    {
        return Path.Combine(profileFolder, $"{SanitizeFileName(name)}.json");
    }

    public static string ReadName(string path, JsonSerializerOptions jsonOptions)
    {
        try
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<CameraProfile>(json, jsonOptions);
            if (!string.IsNullOrWhiteSpace(profile?.Name))
            {
                return profile.Name.Trim();
            }
        }
        catch
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());

        sanitized = sanitized.Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "CameraProfile";
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80].Trim();
    }
}
