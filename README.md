# Jericho Down

Jericho Down is a standalone WPF church/live-worship production tool for recording, camera preview, microphone DSP, EQ, spectrum/waveform visualization, karaoke/worship lyrics, mic comparison, and processed output routing.

It is offered freely for Christian churches, worship teams, and ministries.

## Current Features

- Live microphone picker.
- Spectrum, waveform, 3D waveform, and mic-compare views.
- 20-band visual EQ aligned to the analyzer frequency range.
- Preset buttons including Flat, Podcast Clean, Warm Radio, Deep Warm, Noisy Room, and Bright Headset.
- High-pass filter, noise gate, compressor, makeup gain, and limiter controls.
- Processed output routing to speakers, headphones, or a virtual cable device.
- Standalone processed audio recording with saved-file browsing and playback.
- Camera preview with Media Foundation NV12 DX12, DirectShow BGRA DX12, and texture-native DX12 paths.
- Podcast session recording with video, processed mix WAV, optional raw backup WAV, session metadata, and a saved-video browser.
- Camera profiles, Windows camera controls, video grain reduction, color polish, and preview/record parity reporting.

## Module Map

Jericho Down source code lives under `src/`. Reusable production pieces are organized as self-documenting modules under [src/Modules](src/Modules/README.md). Each module README records what the module owns, what it must not own, and which code is meant to be reusable outside this app.

Migration rule: move one small ownership boundary at a time, preserve behavior, build, run the test harness, then commit before the next pass.

## Build

```powershell
dotnet restore JerichoDown.csproj
dotnet build JerichoDown.csproj
```

## Tests

```powershell
dotnet run --project tests\JerichoDown.Tests\JerichoDown.Tests.csproj
```

## Verification

```powershell
.\tools\VerifyJerichoDown.ps1
.\tools\VerifyJerichoDown.ps1 -LiveCamera
.\tools\VerifyJerichoDown.ps1 -TextureDiagnostic
```

The default verifier builds the solution, runs the test harness, and lists available cameras. `-LiveCamera` briefly opens the configured real and virtual cameras through the DX12 preview host. `-TextureDiagnostic` probes the texture-native path without making that experimental path a required pass condition.

## License

Jericho Down is released under the MIT License. See [LICENSE](LICENSE).

## Camera Probe

```powershell
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --list
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --texture --dx12-preview --source mf --mode 3840x2160@24 --camera "your camera"
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --dx12-preview --source directshow --camera "virtual"
```
