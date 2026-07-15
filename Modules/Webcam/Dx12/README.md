# Webcam.Dx12 Module

Owns GPU camera preview and texture-native rendering.

Responsibilities:
- Render NV12 and BGRA camera preview frames through DX12.
- Own GPU denoise shader paths for preview.
- Manage texture-native camera stream preview.
- Report the active GPU preview path.
- Keep expensive preview work off the camera capture thread.

Current entry points:
- `Direct3D12DeviceManager.cs`
- `Direct3D12PreviewHost.cs`
- `Dx12Camera.cs`
- `Dx12CameraOptions.cs`
- `CameraPreviewFramePumps.cs`

Current legacy files:
- Texture-native preview portions of `Video/TextureNativeCameraRecorder.cs`

Temporary dependencies:
- `JerichoDown.Video.TextureNativeFrameInfo`, `TextureNativeFrameLease`, and texture-native recorder/session types until texture-native frame ownership moves into this module.

Do not put generic camera enumeration or session playback here.
