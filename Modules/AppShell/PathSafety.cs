using System.Diagnostics;
using System.IO;

namespace JerichoDown.Modules.AppShell;

internal static class PathSafety
{
    public static bool IsRegularFileUnderFolder(string? path, string? rootFolder, params string[] allowedExtensions)
    {
        if (!TryGetSafeFullPath(path, out var fullPath)
            || !TryGetSafeFullPath(rootFolder, out var fullRoot)
            || !File.Exists(fullPath)
            || !IsSameOrDescendant(fullPath, fullRoot)
            || HasReparsePoint(fullPath))
        {
            return false;
        }

        return allowedExtensions.Length == 0
            || allowedExtensions.Any(extension => Path.GetExtension(fullPath).Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDirectoryUnderFolder(string? path, string? rootFolder, bool allowRoot = true)
    {
        if (!TryGetSafeFullPath(path, out var fullPath)
            || !TryGetSafeFullPath(rootFolder, out var fullRoot)
            || !Directory.Exists(fullPath)
            || HasReparsePoint(fullPath))
        {
            return false;
        }

        return allowRoot
            ? IsSameOrDescendant(fullPath, fullRoot)
            : IsDescendant(fullPath, fullRoot);
    }

    public static void RevealFileInExplorer(string path)
    {
        StartExplorer($"/select,{Path.GetFullPath(path)}");
    }

    public static void OpenFolderInExplorer(string folder)
    {
        StartExplorer(Path.GetFullPath(folder));
    }

    private static void StartExplorer(string argument)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
    }

    private static bool TryGetSafeFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.IsNullOrWhiteSpace(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        return candidatePath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            || IsDescendant(candidatePath, rootPath);
    }

    private static bool IsDescendant(string candidatePath, string rootPath)
    {
        var rootWithSeparator = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }
}
