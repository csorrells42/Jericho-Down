using System.IO;

namespace JerichoDown.Modules.AppShell;

internal static class AppStoragePaths
{
    private const string SettingsFolderName = "settings";
    private const string AppDataFolderName = "JerichoDown";

    static AppStoragePaths()
    {
        TryMigrateLegacySettingsFolder();
    }

    public static string SettingsFolder { get; } = GetAppDataFolder();

    public static string LegacySettingsFolder { get; } = Path.Combine(GetAppBaseFolder(), SettingsFolderName);

    public static string UserPresetFolder { get; } = Path.Combine(SettingsFolder, "Presets");

    public static string CameraProfileFolder { get; } = Path.Combine(SettingsFolder, "CameraProfiles");

    private static string GetAppDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(GetAppBaseFolder(), SettingsFolderName)
            : Path.Combine(localAppData, AppDataFolderName);
        return Path.GetFullPath(root);
    }

    private static string GetAppBaseFolder()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(baseDirectory);
    }

    private static void TryMigrateLegacySettingsFolder()
    {
        try
        {
            if (!Directory.Exists(LegacySettingsFolder))
            {
                return;
            }

            Directory.CreateDirectory(SettingsFolder);
            CopyLegacyDirectory(LegacySettingsFolder, SettingsFolder);
        }
        catch
        {
        }
    }

    private static void CopyLegacyDirectory(string sourceFolder, string targetFolder)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder))
        {
            var fileName = Path.GetFileName(sourceFile);
            if (fileName.Equals("diagnostics.log", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("run-state.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetFile = Path.Combine(targetFolder, fileName);
            if (!File.Exists(targetFile))
            {
                File.Copy(sourceFile, targetFile, overwrite: false);
            }
        }

        foreach (var sourceChild in Directory.EnumerateDirectories(sourceFolder))
        {
            if (new DirectoryInfo(sourceChild).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var childName = Path.GetFileName(sourceChild);
            if (childName.Equals("KaraokeAiWork", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetChild = Path.Combine(targetFolder, childName);
            Directory.CreateDirectory(targetChild);
            CopyLegacyDirectory(sourceChild, targetChild);
        }
    }
}
