using System.IO;
using System.Text;

namespace JerichoDown.Modules.AppShell;

internal static class AtomicFile
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var targetDirectory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
        var tempPath = Path.Combine(targetDirectory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(targetDirectory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.bak");
        try
        {
            File.WriteAllText(tempPath, contents, Utf8NoBom);
            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        catch
        {
            TryDelete(tempPath);
            TryDelete(backupPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
