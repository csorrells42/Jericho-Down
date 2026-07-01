# Podcast Workbench Handoff

## Project

Path:

```text
C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\PodcastWorkbench
```

Repository:

```text
main @ a9b47f0
```

This is the podcast, camera, microphone, EQ, DSP, and voice-analysis app. It is intentionally separate from Automagination Amalgastation.

## Scope

Keep this repo focused on:

- Podcast/video recording workflow.
- Webcam preview and camera mode/control selection.
- Microphone picker and channel selection.
- Voice spectrum and waveform views.
- EQ sliders and voice presets.
- DSP controls such as high-pass, de-popper, noise gate, noise suppression, echo reducer, compressor, de-esser, limiter, and makeup gain.
- Mic compare and mic analysis tools.
- Processed output routing experiments.

Do not add automation toolbox features here. Wi-Fi analyzer, IP scanner, port scanner, serial, Modbus, SQL, MQTT, and security-camera-only tooling belong in Automagination Amalgastation.

## Build

```powershell
cd "C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\PodcastWorkbench"
dotnet build .\PodcastWorkbench.csproj
```

Current build status:

- Builds successfully with zero warnings.

## Run

```powershell
cd "C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\PodcastWorkbench\bin\Debug\net10.0-windows"
.\PodcastWorkbench.exe
```

## Notes

- Dependencies are local to this repo under `dependencies`.
- The app should not depend on sibling folders or parent-level shared dependency folders.
- Namespace and project identity are `PodcastWorkbench`.

## New Chat Prompt

```text
We are working only on Podcast Workbench.
Repo path: C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\PodcastWorkbench
This is the podcast/camera/microphone app: recording, webcam preview, microphone DSP, EQ, spectrum/waveform display, mic compare, and processed output.
Do not add Automagination Amalgastation features here. Wi-Fi, IP scanning, port scanning, serial, Modbus, SQL, MQTT, and automation toolbox work belong in the separate AutomaginationAmalgastation repo.
Start by reading HANDOFF.md, checking git status, and building PodcastWorkbench.csproj.
```
