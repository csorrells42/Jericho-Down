# Webcam.Dx12 Module

Owns GPU camera preview and texture-native rendering.

Responsibilities:
- Render NV12 and BGRA camera preview frames through DX12.
- Own GPU denoise shader paths for preview.
- Manage texture-native camera stream preview.
- Report the active GPU preview path.
- Keep expensive preview work off the camera capture thread.

Current legacy files:
- `Video/Direct3D12PreviewHost.cs`
- `Video/Direct3D12DeviceManager.cs`
- `Video/Dx12Camera.cs`
- `Video/Dx12CameraOptions.cs`
- Texture-native preview portions of `Video/TextureNativeCameraRecorder.cs`

Do not put generic camera enumeration or session playback here.
