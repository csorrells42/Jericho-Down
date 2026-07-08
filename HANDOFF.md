# Jericho Down Handoff

## Project

Path:

```text
C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\JerichoDown
```

Repository:

```text
main, separate local repo cloned from Podcast Workbench stable point podcast-workbench-v1-stable.
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
- Mic compare and mic analysis tools.
- Future multi-mic/per-mic DSP experiments.

Do not add Automagination Amalgastation automation toolbox features here. Wi-Fi analyzer, IP scanner, port scanner, serial, Modbus, SQL, MQTT, and security-camera-only tooling belong in Automagination Amalgastation.

## Build

```powershell
cd "C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\JerichoDown"
dotnet build .\JerichoDown.slnx
```

Current validation:

- Build succeeds with zero warnings and zero errors.
- Test harness passes: 28 tests.

## Run

```powershell
cd "C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\JerichoDown\bin\Debug\net10.0-windows"
.\JerichoDown.exe
```

## Notes

- Project identity and namespace are `JerichoDown`.
- The new repo has no `origin` remote, so it cannot accidentally push back to Podcast Workbench.
- Podcast Workbench was tagged at `podcast-workbench-v1-stable` before this clone was created.
- Local debug settings and karaoke AI tools were copied into `bin\Debug\net10.0-windows\settings` so the new app can run with the same local tool cache immediately. Those generated output files are not committed.
- Dependencies are local to this repo under `dependencies`.
- The app should not depend on sibling folders or parent-level shared dependency folders.

## New Chat Prompt

```text
We are working only on Jericho Down.
Repo path: C:\Users\clsor\Documents\Codex\2026-06-15\i-want-to-build-a-new\JerichoDown
Jericho Down is the church/live-worship evolution of Podcast Workbench. Podcast Workbench is frozen separately at tag podcast-workbench-v1-stable.
This repo starts from the stable Podcast Workbench codebase and is now renamed to JerichoDown: project file, solution, namespaces, app title, tests, README, and verifier script.
Current validation at handoff: dotnet build .\JerichoDown.slnx passes with 0 warnings/0 errors; dotnet run --project .\tests\JerichoDown.Tests\JerichoDown.Tests.csproj passes 28 tests.
Local debug settings and karaoke AI tools were copied into bin\Debug\net10.0-windows\settings, but generated bin/obj output is not committed.
Scope: church/live worship production, recording, camera preview, microphone DSP/EQ, spectrum/waveform/DX12 waterfall visualization, karaoke/worship lyrics and recording, mic compare, processed output routing, and future multi-mic/per-mic DSP.
Do not add Automagination Amalgastation features here. Wi-Fi, IP scanning, port scanning, serial, Modbus, SQL, MQTT, and automation toolbox work belong in the separate AutomaginationAmalgastation repo.
Start by reading HANDOFF.md, checking git status, and building JerichoDown.slnx.
```
