# Jericho Down

Jericho Down is a standalone C# / WPF production tool for churches, worship teams, podcasters, and live-event operators. It brings microphone processing, camera preview, recording, lyrics, and routed output into one practical Windows desktop application.

## Why it matters

This is real-time application work, not a mockup: the software processes live audio, controls cameras, records sessions, and gives operators immediate feedback through purpose-built visualizations and controls.

- **Audio pipeline:** 20-band EQ, high-pass filtering, noise gate, compressor, limiter, spectrum/waveform views, and processed routing.
- **Video pipeline:** Media Foundation and DirectShow camera paths, DirectX 12 preview, camera controls, denoising, and color correction.
- **Production workflow:** live mic selection, lyric timing, session metadata, raw-backup audio, and saved media browsing.
- **Built to validate:** focused test harnesses and a PowerShell verifier for builds, tests, connected cameras, and optional live-camera diagnostics.

## Engineering highlights

| Area | Implementation |
| --- | --- |
| Desktop application | C#, .NET, WPF, XAML |
| Audio | Low-latency DSP, EQ, dynamics processing, recording, virtual-device routing |
| Video | Media Foundation, DirectShow, DirectX 12, camera controls, real-time image processing |
| Reliability | Modular boundaries, test harness, hardware-aware verification scripts |

## What I can build from this foundation

Custom Windows media tools, podcast and livestream workflows, camera-control applications, audio-processing utilities, and hardware-adjacent desktop software.

It is offered freely for Christian churches, worship teams, and ministries.

## Current Features

- Live microphone picker.
- Spectrum, waveform, and 3D waveform views.
- Ten mono/stereo mixer inputs, computer/app loopback, WASAPI and ASIO routing.
- 20-band visual EQ aligned to the analyzer frequency range.
- Preset buttons including Flat, Podcast Clean, Warm Radio, Deep Warm, Noisy Room, and Bright Headset.
- High-pass filter, noise gate, compressor, makeup gain, and limiter controls.
- Custom DSP, NAudio BiQuad filters, and NAudio effects grouped by family.
- MIDI input monitoring, output messages, file playback, mixer mappings, and SoundFont sample inspection.
- Processed output routing to speakers, headphones, or a virtual cable device.
- Standalone processed audio recording with saved-file browsing and playback.
- Camera preview with Media Foundation NV12 DX12, DirectShow BGRA DX12, and texture-native DX12 paths.
- Podcast session recording with video, optional processed mix WAV, raw input backup WAV, session metadata, and a saved-video browser. Audio is saved alongside the video, not embedded in the MP4.
- Camera profiles, Windows camera controls, video grain reduction, color polish, and preview/record parity reporting.

## Module Map

Jericho Down source code lives under `src/`. Reusable production pieces are organized as self-documenting modules under [src/Modules](src/Modules/README.md). Each module README records what the module owns, what it must not own, and which code is meant to be reusable outside this app.

Migration rule: move one small ownership boundary at a time, preserve behavior, build, run the test harness, then commit before the next pass.

## Build

```powershell
dotnet restore JerichoDown.csproj
dotnet build JerichoDown.slnx -c Release
```

## Tests

```powershell
dotnet run --project tests\JerichoDown.Tests\JerichoDown.Tests.csproj -c Release
```

## Verification

```powershell
.\tools\VerifyJerichoDown.ps1
.\tools\VerifyJerichoDown.ps1 -UiSmoke -AudioHardware
.\tools\VerifyJerichoDown.ps1 -LiveCamera
.\tools\VerifyJerichoDown.ps1 -TextureDiagnostic
dotnet run --project tools\ReleaseSmokeProbe\ReleaseSmokeProbe.csproj -c Release -- --video
```

The default verifier builds Release, runs the test harness, and lists available cameras. `-UiSmoke` exercises the actual WPF tabs, mixer selection/mute, recording controls, and shutdown with an isolated temporary profile. It does not change your saved settings. `-AudioHardware` opens available inputs and briefly plays a generated test tone through the default output; unavailable ASIO drivers are reported separately and still require a connected-device test. `-LiveCamera` briefly opens the configured real and virtual cameras through the DX12 preview host. `-TextureDiagnostic` probes the texture-native path without making that experimental path a required pass condition.

## License

Jericho Down is released under the MIT License. See [LICENSE](LICENSE).

## Camera Probe

```powershell
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --list
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --module-sample --camera "your camera"
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --module-sample --stress-count 5 --camera "your camera"
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --texture --dx12-preview --source mf --mode 3840x2160@24 --camera "your camera"
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --dx12-preview --source directshow --camera "virtual"
```
