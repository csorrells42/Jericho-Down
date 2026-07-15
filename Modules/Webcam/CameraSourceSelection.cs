using JerichoDown.Modules.Webcam.DirectShow;
using JerichoDown.Modules.Webcam.Dx12;
using JerichoDown.Modules.Webcam.MediaFoundation;

namespace JerichoDown.Modules.Webcam;

internal static class CameraSourceSelection
{
    internal static IReadOnlyList<CameraDevice> GetCameras()
    {
        return CameraDeviceCatalog.MergeDevices(
            MediaFoundationCameraEnumerator.GetVideoInputDevices(),
            DirectShowCameraEnumerator.GetVideoInputDevices());
    }

    internal static CameraDevice? GetDefaultCamera()
    {
        return GetCameras().FirstOrDefault();
    }

    internal static CameraDevice RequireDefaultCamera()
    {
        return GetDefaultCamera()
            ?? throw new InvalidOperationException("No camera devices were found.");
    }

    internal static CameraDevice? FindCamera(
        IReadOnlyList<CameraDevice> cameras,
        string? devicePath,
        string? source,
        string? name)
    {
        if (!string.IsNullOrWhiteSpace(devicePath))
        {
            var pathMatch = cameras.FirstOrDefault(camera =>
                camera.EnumerateSourceDevices().Any(sourceDevice =>
                    sourceDevice.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase)
                    && sourceDevice.Source.Equals(source ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
            if (pathMatch is not null)
            {
                return pathMatch;
            }
        }

        return string.IsNullOrWhiteSpace(name)
            ? null
            : cameras.FirstOrDefault(camera =>
                camera.EnumerateSourceDevices().Any(sourceDevice =>
                    sourceDevice.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && sourceDevice.Source.Equals(source ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                ?? cameras.FirstOrDefault(camera =>
                    camera.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsDirectShowCamera(CameraDevice camera)
    {
        return string.Equals(camera.Source, "DirectShow", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSelectedDirectShowCamera(bool isDirectShowPreviewActive, CameraDevice? selectedCamera)
    {
        return isDirectShowPreviewActive
            || selectedCamera is not null && IsDirectShowCamera(selectedCamera);
    }

    internal static bool IsCpuCameraPreviewOwningCamera(
        bool isCameraEnabled,
        Dx12Camera? activeCamera,
        bool mediaFoundationPreviewIsRunning,
        bool directShowPreviewIsRunning,
        bool isDirectShowPreviewActive)
    {
        return isCameraEnabled
            && activeCamera?.IsTextureNative != true
            && (mediaFoundationPreviewIsRunning || directShowPreviewIsRunning || isDirectShowPreviewActive);
    }

    internal static bool TryGetDirectShowFallbackCamera(CameraDevice primaryCamera, out CameraDevice? directShowFallback)
    {
        directShowFallback = primaryCamera.FallbackDevice;
        return directShowFallback is not null && IsDirectShowCamera(directShowFallback);
    }
}
