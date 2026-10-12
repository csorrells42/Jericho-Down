# Jericho Down Handoff

## Project

Path:

```text
Jericho-Down (use this checkout's root directory)
```

Repository:

```text
main; origin https://github.com/csorrells42/Jericho-Down.git
```

Jericho Down is the church/live-worship evolution of Podcast Workbench. Podcast Workbench is frozen separately as the stable general-purpose podcast/video/audio tool.

## Scope

Keep this repo focused on church production and creator tooling built from the current working foundation:

- Podcast/video recording workflow.
- Church/live worship recording workflow.
- Webcam preview and camera mode/control selection.
- Microphone picker and channel selection.
- Microphone DSP, EQ, presets, and output routing.
- Spectrum, waveform, and DX12 waterfall visualizations.
- Karaoke/worship-track playback, lyrics, queueing, and recording.
- Ten microphone/input strips plus a mixer-only computer-audio strip.
- MIDI utility, file playback and control mapping tools. Mic Compare was removed.

Do not add Automagination Amalgastation automation toolbox features here. Wi-Fi analyzer, IP scanner, port scanner, serial, Modbus, SQL, MQTT, and security-camera-only tooling belong in Automagination Amalgastation.

## Build

```powershell
# Run from the repository root.
dotnet build .\JerichoDown.slnx -c Release
dotnet run --project .\tests\JerichoDown.Tests -c Release --no-build
```

Current validation:

- Build succeeds with zero warnings and zero errors.
- Test harness passes: 188 tests as of the second review on 2026-10-12, including a 280-case DSP rate/toggle safety matrix.
- Detailed findings, checkpoint commits, hardware evidence and remaining limitations are in `RELEASE_CHECK.md`.

## Run

```powershell
.\bin\Release\net10.0-windows\JerichoDown.exe
```

## Notes

- Project identity and namespace are `JerichoDown`.
- Verify `git status` and `git remote -v` before committing or pushing; origin is Jericho-Down, not Podcast Workbench.
- Podcast Workbench was tagged at `podcast-workbench-v1-stable` before this clone was created.
- User settings normally live in `%LOCALAPPDATA%\JerichoDown`; verification hosts use isolated temporary profiles. Do not overwrite the user's saved input/device choices during tests.
- The user no longer has the AG06. Keep ASIO support, but do not require or repeatedly probe that missing device. Hardware smoke excludes ASIO unless `--asio` is specified.
- Dependencies are local to this repo under `dependencies`.
- The app should not depend on sibling folders or parent-level shared dependency folders.

## New Chat Prompt

```text
We are working only on Jericho Down.
Use the current Jericho-Down Git checkout and verify its remote before pushing.
Jericho Down is the church/live-worship evolution of Podcast Workbench. Podcast Workbench is frozen separately at tag podcast-workbench-v1-stable.
This repo starts from the stable Podcast Workbench codebase and is now renamed to JerichoDown: project file, solution, namespaces, app title, tests, README, and verifier script.
Current validation: Release build has 0 warnings/errors, 188 regression tests pass (including 280 DSP safety cases), and WPF/available Windows audio/camera/software-MIDI smoke passes. See RELEASE_CHECK.md for specifics and unverified hardware/visual/model-execution checks.
Keep generated bin/obj output out of commits and preserve the user's saved profile.
Scope: church/live worship production, recording, camera preview, microphone DSP/EQ, spectrum/waveform/DX12 waterfall visualization, karaoke/worship lyrics and recording, MIDI utilities, processed output routing and multi-input mixing. Mic Compare was removed.
Do not add Automagination Amalgastation features here. Wi-Fi, IP scanning, port scanning, serial, Modbus, SQL, MQTT, and automation toolbox work belong in the separate AutomaginationAmalgastation repo.
Start by reading HANDOFF.md, checking git status, and building JerichoDown.slnx.
```
