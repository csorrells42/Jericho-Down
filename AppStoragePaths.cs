using System.IO;

namespace PodcastWorkbench;

internal static class AppStoragePaths
{
    private const string SettingsFolderName = "settings";

    public static string SettingsFolder { get; } = Path.Combine(GetAppBaseFolder(), SettingsFolderName);

    public static string UserPresetFolder { get; } = Path.Combine(SettingsFolder, "Presets");

    public static string CameraProfileFolder { get; } = Path.Combine(SettingsFolder, "CameraProfiles");

    private static string GetAppBaseFolder()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(baseDirectory);
    }
}
