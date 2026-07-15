# Webcam Module

Owns camera vocabulary, preview startup policy, camera controls, recording policy, frame/color helpers, and user-facing camera status text.

Important submodules:
- `MediaFoundation`: Windows Media Foundation camera source readers and video writing.
- `DirectShow`: DirectShow fallback camera preview and controls.
- `Dx12`: GPU preview, texture-native stream, and GPU denoise.
- `Dx11Bridge`: bridge path for shared D3D11/D3D12 texture interop.

Current entry points:
- `CameraDevice.cs`
- `CameraVideoMode.cs`
- `CameraFrame.cs`
- `CameraControlKind.cs`
- `CameraControlItem.cs`
- `CameraStatusText.cs`
- `VideoRecordingPolicy.cs`
- `VideoFrameColorSettings.cs`

Most camera capture, control, and rendering code still lives in `Video` while migration is in progress.

Do not put session playback here unless it is live camera preview or camera recording.
