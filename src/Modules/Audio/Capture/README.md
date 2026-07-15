# Audio.Capture Module

Owns non-device-specific capture sources used by the live audio service.

Current entry points:
- `ProcessLoopbackCapture.cs`
- `SignalGeneratorCapture.cs`

Consumed by:
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` when starting app-audio loopback or stereo test-tone capture.

Do not put ASIO startup, device vocabulary, recording export, or mixer policy here.
