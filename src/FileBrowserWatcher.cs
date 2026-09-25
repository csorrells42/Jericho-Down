using System.IO;

namespace JerichoDown;

public sealed class FileBrowserWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Func<string, bool> _isRelevantPath;
    private readonly Action _queueRefresh;

    private FileBrowserWatcher(
        FileSystemWatcher watcher,
        Func<string, bool> isRelevantPath,
        Action queueRefresh)
    {
        _watcher = watcher;
        _isRelevantPath = isRelevantPath;
        _queueRefresh = queueRefresh;

        _watcher.Created += FileChanged;
        _watcher.Deleted += FileChanged;
        _watcher.Renamed += FileRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    public static FileBrowserWatcher Start(
        string folder,
        bool includeSubdirectories,
        NotifyFilters notifyFilter,
        Func<string, bool> isRelevantPath,
        Action queueRefresh)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(isRelevantPath);
        ArgumentNullException.ThrowIfNull(queueRefresh);

        Directory.CreateDirectory(folder);
        var watcher = new FileSystemWatcher(folder)
        {
            Filter = "*.*",
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = notifyFilter
        };

        return new FileBrowserWatcher(watcher, isRelevantPath, queueRefresh);
    }

    public static bool ShouldRefresh(string? path, Func<string, bool> isRelevantPath)
    {
        ArgumentNullException.ThrowIfNull(isRelevantPath);
        return !string.IsNullOrWhiteSpace(path) && isRelevantPath(path);
    }

    public static bool ShouldRefreshRename(string? newPath, string? oldPath, Func<string, bool> isRelevantPath)
    {
        return ShouldRefresh(newPath, isRelevantPath) || ShouldRefresh(oldPath, isRelevantPath);
    }

    public void Dispose()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= FileChanged;
            _watcher.Deleted -= FileChanged;
            _watcher.Renamed -= FileRenamed;
            _watcher.Dispose();
        }
        catch
        {
        }
    }

    private void FileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldRefresh(e.FullPath, _isRelevantPath))
        {
            _queueRefresh();
        }
    }

    private void FileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldRefreshRename(e.FullPath, e.OldFullPath, _isRelevantPath))
        {
            _queueRefresh();
        }
    }
}
