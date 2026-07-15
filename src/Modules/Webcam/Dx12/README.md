# Webcam.Dx12 Module

Owns GPU camera preview and texture-native rendering.

Responsibilities:
- Render NV12 and BGRA camera preview frames through DX12.
- Own GPU denoise and color-polish shader paths for preview.
- Manage texture-native camera stream preview.
- Report the active GPU preview path, render FPS, dropped frames, format, processing state, recording mode, and fallback reason.
- Keep expensive preview work off the camera capture thread.
- Use `DirectX12Viewport` for reusable WPF child-window hosting, while keeping camera-specific rendering here.

Current entry points:
- `Direct3D12DeviceManager.cs`
- `Direct3D12PreviewHost.cs`
- `Direct3D12PreviewDiagnostics.cs`
- `ICameraPreviewPresenter.cs`
- `Dx12Camera.cs`
- `Dx12CameraOptions.cs`
- `CameraPreviewFramePumps.cs`
- `TextureNativeCameraRecorder.cs`
- `TextureNativeCameraProbe.cs`

Drop-in boundary:
- Use `ICameraPreviewPresenter` when another program needs a camera preview surface without knowing whether the backing renderer is DX12, WPF, or a fallback.
- Use `Direct3D12PreviewDiagnostics.FormatStatusLine()` for a compact status overlay or log line.

Do not put generic camera enumeration or session playback here.
