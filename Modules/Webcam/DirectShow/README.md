# Webcam.DirectShow Module

Owns DirectShow fallback camera handling.

Responsibilities:
- Enumerate DirectShow cameras.
- Read DirectShow camera controls.
- Capture BGRA preview frames when Media Foundation or texture-native preview is unavailable.
- Hand frames to DX12 preview when AppShell has a preview host.

Current entry points:
- `DirectShowCameraEnumerator.cs`
- `DirectShowCameraControlService.cs`
- `DirectShowCameraPreviewService.cs`

Temporary dependencies:
- `JerichoDown.Video.VideoFrameDenoiser` until denoise helpers migrate.

Do not add Media Foundation source reader code or DX12 shader code here.
