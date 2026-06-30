# Podcast Workbench

Podcast Workbench is a standalone WPF podcast recording, camera preview, microphone DSP, and voice analysis tool split out of Automagination Amalgastation.

## Current Features

- Live microphone picker.
- Full-screen spectrum analyzer.
- Blue live trace and gold smoothed trace.
- 20-band visual EQ aligned to the analyzer frequency range.
- Preset buttons: Flat, Podcast Clean, Warm Radio, Noisy Room, Bright Headset.
- High-pass filter, noise gate, compressor, makeup gain, and limiter controls.

## Current Limitation

The processing chain currently affects the analyzer path. It does not yet publish processed audio as a Windows virtual microphone. The next step is output routing through a virtual audio cable/device.

## Build

```powershell
dotnet restore PodcastWorkbench.csproj
dotnet build PodcastWorkbench.csproj
```
