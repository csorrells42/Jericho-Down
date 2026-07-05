# Podcast Workbench

Podcast Workbench is a standalone WPF podcast recording, camera preview, microphone DSP, and voice analysis tool split out of Automagination Amalgastation.

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

## Build

```powershell
dotnet restore PodcastWorkbench.csproj
dotnet build PodcastWorkbench.csproj
```

## Tests

```powershell
dotnet run --project tests\PodcastWorkbench.Tests\PodcastWorkbench.Tests.csproj
```

## Verification

```powershell
.\tools\VerifyPodcastWorkbench.ps1
.\tools\VerifyPodcastWorkbench.ps1 -LiveCamera
.\tools\VerifyPodcastWorkbench.ps1 -TextureDiagnostic
```

The default verifier builds the solution, runs the test harness, and lists available cameras. `-LiveCamera` briefly opens the configured real and virtual cameras through the DX12 preview host. `-TextureDiagnostic` probes the texture-native path without making that experimental path a required pass condition.

## Camera Probe

```powershell
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --list
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --texture --dx12-preview --source mf --mode 3840x2160@24 --camera "your camera"
dotnet run --project tools\CameraPreviewProbe\CameraPreviewProbe.csproj -- --dx12-preview --source directshow --camera "virtual"
```
