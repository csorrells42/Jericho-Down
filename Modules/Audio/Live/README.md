# Audio.Live Module

Owns the live microphone service orchestration for Jericho Down.

Current entry points:
- `MicrophoneSpectrumService.cs`

Responsibilities:
- Device discovery and capture startup for selected live inputs.
- Processed monitoring and live output coordination.
- Processed recording routing for program mix and selected mic sources.
- Spectrum and meter publishing for the audio graphs.
- Glue code that coordinates ASIO, CoreAudio, device vocabulary, DSP, recording, sync, mixer, and visualization modules.

Key dependencies:
- `JerichoDown.Modules.Audio.Asio` for ASIO capture, callback probes, output, and STA dispatch.
- `JerichoDown.Modules.Audio.Capture` for loopback and test-tone capture sources.
- `JerichoDown.Modules.Audio.CoreAudio` for Windows audio sessions and endpoint notifications.
- `JerichoDown.Modules.Audio.Devices` for input/output vocabulary and routing policy.
- `JerichoDown.Modules.Audio.Dsp` for EQ, voice processing, and DSP telemetry.
- `JerichoDown.Modules.Audio.Recording` for recording source and export helpers.
- `JerichoDown.Modules.Audio.Sync` for delay, latency, and sample-rate alignment.
- `JerichoDown.Modules.Mixer` for the live program bus and strip processing.
- `JerichoDown.Modules.Visualization` for spectrum analysis and graph frames.

Do not put WPF tab UI, camera preview, karaoke lyric display, or low-level driver implementation here.
