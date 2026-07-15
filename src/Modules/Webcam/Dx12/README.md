# Webcam.Dx12 Module

Owns GPU camera preview and texture-native rendering.

Responsibilities:
- Render NV12 and BGRA camera preview frames through DX12.
- Own GPU denoise shader paths for preview.
- Manage texture-native camera stream preview.
- Report the active GPU preview path.
- Keep expensive preview work off the camera capture thread.
- Use `DirectX12Viewport` for reusable WPF child-window hosting, while keeping camera-specific rendering here.

Current entry points:
- `Direct3D12DeviceManager.cs`
- `Direct3D12PreviewHost.cs`
- `Dx12Camera.cs`
- `Dx12CameraOptions.cs`
- `CameraPreviewFramePumps.cs`
- `TextureNativeCameraRecorder.cs`
- `TextureNativeCameraProbe.cs`

Do not put generic camera enumeration or session playback here.
