# Release Check - 2026-09-24

Status: software checks pass; live Yamaha AG06/ASIO verification remains open.

## Confirmed And Fixed

- Disposed microphone services could reopen capture. Start/stop/recovery now serialize, pending recovery is invalidated by stopping, and disposal prevents reopening.
- A restart could open a new stream before the old driver finished stopping. It now waits for release and reports a timeout without overlapping streams.
- WAV recording could truncate an existing file. Recording destinations now use exclusive creation; rapid karaoke recordings choose distinct names.
- Export normalized the destination extension after checking whether it matched the source. The final destination is now checked, and replacement no longer deletes the old export before moving the completed file.
- Podcast Record started video but never created the advertised session audio. It now saves the optional processed program mix plus a raw primary-input backup, pauses/resumes them with video, writes their names in metadata, and finalizes on stop/close or capture interruption.

Podcast audio is saved as separate WAV files alongside the MP4. It is not muxed into the video. The raw backup preserves the primary capture device's channels; it is not a multitrack backup of every auxiliary input.

## Verification Evidence

- Release solution build: 0 warnings, 0 errors.
- Regression harness: 133 tests passed, including reproduced failures before their fixes, live generated audio recording, pause behavior, and stop/disposal finalization.
- WPF smoke: actual main window, all five tabs twice, selection/mute on all 11 mixer strips, EQ loopback exclusion, recording/pause/resume/stop and decoding the saved WAV, karaoke playback/recording chain, clean shutdown marker, and no binding errors.
- Podcast smoke: connected HD Webcam, video MP4, audible mix and raw WAVs, metadata, pause/resume, and capture interruption handling.
- DX12 camera probe: HD Webcam at 1280x720, 63 frames received and 62 rendered during the initial probe.
- Live Windows input capture: Intel microphone array, MSI Sound Tune microphone, and MSI system virtual line passed via WASAPI.
- Default speaker route opened and played generated audio.
- Release publish succeeded; output includes JerichoDown.exe, runtime metadata, dependencies, and the About asset.
- Probe processes exited. Tests used isolated temporary settings and did not replace the user's saved Yamaha selection.

## Open Gate

The installed Yamaha Steinberg USB ASIO driver reports "Device could not be opened." The user's saved primary input is that ASIO driver. Reconnect/power the AG06, or resolve any driver ownership problem, then test input channels, mixer routing/recording, restart, and shutdown with that hardware before claiming full ASIO release readiness.

The Windows computer-use helper could not start because of a sandbox ACL error. The WPF probe executes real control events and checks bindings, but a screenshot-based visual review was not completed. MIDI message/mapping/file logic is covered by tests; external MIDI hardware was not exercised.

## Repeatable Commands

```powershell
.\tools\VerifyJerichoDown.ps1 -UiSmoke -AudioHardware
dotnet run --project tools\ReleaseSmokeProbe\ReleaseSmokeProbe.csproj -c Release -- --video
dotnet publish JerichoDown.csproj -c Release --no-restore -o output\release-check
```

The hardware probe reports unavailable ASIO drivers separately; a successful process exit alone does not certify ASIO hardware. Smoke profiles and recordings are left under the printed temporary path for inspection.
