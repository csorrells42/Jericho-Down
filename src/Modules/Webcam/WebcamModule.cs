using System.Windows.Controls;
using JerichoDown.Modules.Webcam.DirectShow;
using JerichoDown.Modules.Webcam.Dx12;
using JerichoDown.Modules.Webcam.MediaFoundation;

namespace JerichoDown.Modules.Webcam;

public static class WebcamModule
{
    public static IReadOnlyList<CameraDevice> GetCameras()
    {
        return CameraSourceSelection.GetCameras();
    }

    public static CameraDevice? GetDefaultCamera()
    {
        return CameraSourceSelection.GetDefaultCamera();
    }

    public static CameraDevice RequireDefaultCamera()
    {
        return CameraSourceSelection.RequireDefaultCamera();
    }

    public static CameraDevice? FindCamera(
        IReadOnlyList<CameraDevice> cameras,
        string? devicePath,
        string? source,
        string? name)
    {
        return CameraSourceSelection.FindCamera(cameras, devicePath, source, name);
    }

    public static MediaFoundationCameraModeService CreateModeService()
    {
        return new MediaFoundationCameraModeService();
    }

    public static MediaFoundationCameraPreviewService CreateMediaFoundationPreviewService()
    {
        return new MediaFoundationCameraPreviewService();
    }

    public static DirectShowCameraPreviewService CreateDirectShowPreviewService()
    {
        return new DirectShowCameraPreviewService();
    }

    public static DirectShowCameraControlService CreateDirectShowControlService()
    {
        return new DirectShowCameraControlService();
    }

    public static Direct3D12PreviewHost CreateDirect3D12PreviewHost(IntPtr nativeD3D12Device = default)
    {
        return new Direct3D12PreviewHost(nativeD3D12Device);
    }

    public static Dx12Camera StartDx12Camera(Panel previewPanel, Dx12CameraOptions? options = null)
    {
        return new Dx12Camera(previewPanel, options);
    }

    public static Dx12Camera StartDx12Camera(CameraDevice camera, CameraVideoMode? mode, Panel previewPanel)
    {
        return new Dx12Camera(camera, mode, previewPanel);
    }
}
