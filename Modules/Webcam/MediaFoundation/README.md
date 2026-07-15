# Webcam.MediaFoundation Module

Owns Media Foundation camera and video writer code.

Responsibilities:
- Enumerate Media Foundation cameras and modes.
- Create camera source readers.
- Prefer NV12 where possible so DX12 preview shaders can render efficiently.
- Write MP4 video with stable sample timing.
- Provide CPU preview fallback frames when texture-native preview is unavailable.

Current legacy files:
- `Video/MediaFoundationCameraEnumerator.cs`
- `Video/MediaFoundationCameraModeService.cs`
- `Video/MediaFoundationCameraDeviceFactory.cs`
- `Video/MediaFoundationCameraPreviewService.cs`
- `Video/MediaFoundationVideoRecorder.cs`
- `Video/MediaFoundationInterop.cs`
- `Video/MediaFoundationGuids.cs`

Session file playback belongs in `SessionPlayback`, even when it uses Media Foundation.
