namespace PodcastWorkbench.Video;

public sealed class TextureNativePreviewFailureCache
{
    private readonly Dictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetFailure(CameraDevice camera, CameraVideoMode mode, out string reason)
    {
        return _failures.TryGetValue(CreateKey(camera, mode), out reason!);
    }

    public void RememberFailure(CameraDevice camera, CameraVideoMode mode, string reason)
    {
        _failures[CreateKey(camera, mode)] = string.IsNullOrWhiteSpace(reason)
            ? "previous shared texture preview attempt failed"
            : reason;
    }

    public void ForgetFailure(CameraDevice camera, CameraVideoMode mode)
    {
        _failures.Remove(CreateKey(camera, mode));
    }

    private static string CreateKey(CameraDevice camera, CameraVideoMode mode)
    {
        var cameraKey = string.IsNullOrWhiteSpace(camera.DevicePath)
            ? $"{camera.Source}|{camera.Name}"
            : $"{camera.Source}|{camera.DevicePath}";
        var modeKey = mode.IsAuto
            ? "auto"
            : $"{mode.Width}x{mode.Height}@{mode.FramesPerSecond:0.###}";
        return $"{cameraKey}|{modeKey}";
    }
}
