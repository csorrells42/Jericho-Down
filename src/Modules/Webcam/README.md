# Webcam Module

Owns reusable webcam discovery, preview startup policy, camera controls, recording policy, profiles, frame/color helpers, and user-facing camera status text.

Standalone entry point:
- `WebcamModule.cs` is the facade another WPF program should start from. It exposes camera discovery, mode/control services, Media Foundation and DirectShow preview services, the DX12 preview host, and the high-level `Dx12Camera` starter.

Important submodules:
- `MediaFoundation`: Windows Media Foundation camera source readers and video writing.
- `DirectShow`: DirectShow fallback camera preview and controls.
- `Dx12`: GPU preview, texture-native stream, and GPU denoise.
- `Dx11Bridge`: bridge path for shared D3D11/D3D12 texture interop.

Current entry points:
- `WebcamModule.cs`
- `CameraDevice.cs`
- `CameraDeviceCatalog.cs`
- `CameraVideoMode.cs`
- `CameraFrame.cs`
- `CameraControlKind.cs`
- `CameraControlItem.cs`
- `CameraControlText.cs`
- `CameraSourceSelection.cs`
- `CameraProfile.cs`
- `CameraProfileStore.cs`
- `CameraStatusText.cs`
- `TextureNativePreviewPolicy.cs`
- `VideoRecordingPolicy.cs`
- `VideoFrameDenoiser.cs`
- `VideoFrameColorSettings.cs`
- `WebcamAtomicFile.cs`

GPU camera rendering, texture-native recording, and preview probes live in `Dx12`. Keep generic camera vocabulary and user-facing camera status helpers here.

Do not put Jericho Down window/menu/tab wiring here. Do not put session playback here unless it is live camera preview or camera recording.
