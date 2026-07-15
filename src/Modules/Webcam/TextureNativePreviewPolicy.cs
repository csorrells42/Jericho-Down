namespace JerichoDown.Modules.Webcam;

public static class TextureNativePreviewPolicy
{
    private static readonly Dictionary<string, string> PreviewFailures = new(StringComparer.OrdinalIgnoreCase);

    public static bool ShouldUseSharedTextureCameraStream(bool safeStartDx12Disabled)
    {
        if (safeStartDx12Disabled)
        {
            return false;
        }

        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_SHARED_TEXTURE_CAMERA");
        return string.Equals(value, "force", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "preview", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetPreviewFailure(
        CameraDevice camera,
        CameraVideoMode mode,
        out string reason)
    {
        return PreviewFailures.TryGetValue(CreatePreviewFailureKey(camera, mode), out reason!);
    }

    public static void RememberPreviewFailure(
        CameraDevice camera,
        CameraVideoMode mode,
        string reason)
    {
        PreviewFailures[CreatePreviewFailureKey(camera, mode)] = string.IsNullOrWhiteSpace(reason)
            ? "previous shared texture preview attempt failed"
            : reason;
    }

    public static void ForgetPreviewFailure(CameraDevice camera, CameraVideoMode mode)
    {
        PreviewFailures.Remove(CreatePreviewFailureKey(camera, mode));
    }

    private static string CreatePreviewFailureKey(CameraDevice camera, CameraVideoMode mode)
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
