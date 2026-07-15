# SessionPlayback Module

Owns playback of saved podcast session files.

Responsibilities:
- Play saved `video_###.mp4` files through the DX12 preview host.
- Pace video defensively when hardware-camera MP4 timestamps are missing, sparse, or implausibly tiny.
- Resolve session sidecar audio beside the MP4, preferring `mix_###.wav` and falling back to `raw_backup_###.wav`.
- Keep Windows media fallback available when DX12 playback is unavailable.

Current entry points:
- `MediaFoundationFilePlaybackService.cs`
- Session playback methods in `EqualizerWindow.xaml.cs` until the UI controller is split out.

Dependencies:
- `JerichoDown.Video` for Media Foundation interop.
- `JerichoDown.Modules.Webcam` for camera frame payloads.
- NAudio routing from AppShell for sidecar WAV output.

Do not put live camera preview, camera recording, or karaoke track playback here.
