# Jericho Down

Jericho Down is a standalone WPF church/live-worship production tool for recording, camera preview, microphone DSP, EQ, spectrum/waveform visualization, karaoke/worship lyrics, MIDI utilities, and processed output routing.

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
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --texture --dx12-preview --source mf --mode 3840x2160@24 --camera "your camera"
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --dx12-preview --source directshow --camera "virtual"
```
