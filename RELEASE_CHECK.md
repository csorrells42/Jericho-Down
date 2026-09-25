# Release Check - 2026-09-24

Status: automated software checks pass; release sign-off is blocked on connected Yamaha AG06/ASIO hardware and desktop visual verification.

## Confirmed And Fixed

- Disposed microphone services could reopen capture. Start/stop/recovery now serialize, pending recovery is invalidated by stopping, and disposal prevents reopening.
- A restart could open a new stream before the old driver finished stopping. It now waits for release and reports a timeout without overlapping streams.
- Repeated device-open failures now pause automatic recovery after three failed cycles. Window activation respects the retry limit; File > Refresh Audio Devices or an explicit input change allows recovery again. Signal rendering no longer overwrites the failure with a false Listening status.
- WAV recording could truncate an existing file. Recording destinations now use exclusive creation; rapid karaoke recordings choose distinct names.
- Export normalized the destination extension after checking whether it matched the source. The final destination is now checked, and replacement no longer deletes the old export before moving the completed file.
- Podcast Record started video but never created the advertised session audio. It now saves the optional processed program mix plus a raw primary-input backup, pauses/resumes and rotates them with video, writes their names in metadata, and finalizes on stop/close or capture interruption.

Podcast audio is saved as separate WAV files alongside the MP4. It is not muxed into the video. The raw backup preserves the primary capture device's channels; it is not a multitrack backup of every auxiliary input.

## Verification Evidence

- Release solution build: 0 warnings, 0 errors.
- Regression harness: 162 tests passed after merging the newer GitHub module, EQ, ASIO, and camera changes.
- WPF smoke: actual main window, MIDI opt-in, all five tabs twice, selection/mute on all 11 mixer strips, EQ loopback exclusion, recording/pause/resume/stop and decoding the saved WAV, karaoke playback/recording chain, bounded failed recovery and manual refresh, clean shutdown marker, and no binding or dispatcher errors.
- Podcast smoke: connected HD Webcam, video MP4, audible mix and raw WAVs, metadata, pause/resume, forced segment rotation with matching audio/video sets, and capture interruption handling.
- DX12 camera probe: HD Webcam at 1280x720, 63 frames received and 62 rendered during the initial probe.
- Live Windows input capture: Intel microphone array, MSI Sound Tune microphone, and MSI system virtual line passed via WASAPI.
- Default speaker route opened and played generated audio.
- Release publish succeeded; output includes JerichoDown.exe, runtime metadata, dependencies, and the About asset.
- Probe processes exited. Tests used isolated temporary settings and did not replace the user's saved Yamaha selection.
- Desktop launcher targets `bin/Release/net10.0-windows/JerichoDown.exe`, which was rebuilt with these fixes.
- Final artifact audit: Release and publish copies of `JerichoDown.exe` and `JerichoDown.dll` have matching SHA-256 hashes. All five bundled PDF guides, the About image, runtime metadata, and NAudio/DirectX dependencies are present in the publish output.

## Open Gate

The installed Yamaha Steinberg USB ASIO driver reports "Device could not be opened." The user's saved primary input is that ASIO driver. Reconnect/power the AG06, or resolve any driver ownership problem, then test input channels, mixer routing/recording, restart, and shutdown with that hardware before claiming full ASIO release readiness.

The final Windows PnP audit found no present Yamaha/Steinberg audio device. The two retained AG06/AG03 software-device records both report `Present=False`. This is evidence that the hardware is unavailable to Windows, not proof of an application ASIO defect. Do not replace the saved Yamaha selection or repeatedly reopen its driver to work around missing hardware.

The Windows computer-use helper could not start because of a sandbox ACL error. The WPF probe executes real control events and checks bindings, but a screenshot-based visual review was not completed. MIDI message/mapping/file logic is covered by tests; external MIDI hardware was not exercised.

The desktop helper was retried after resetting its session and again exited with `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`. No live helper or verification process is being waited on. Resume visual verification when that execution environment works; do not treat the in-process WPF checks as screenshot evidence.

## Repeatable Commands

```powershell
.\tools\VerifyJerichoDown.ps1 -UiSmoke -AudioHardware
dotnet run --project tools\ReleaseSmokeProbe\ReleaseSmokeProbe.csproj -c Release -- --video
dotnet publish JerichoDown.csproj -c Release --no-restore -o output\release-check
```

The hardware probe reports unavailable ASIO drivers separately; a successful process exit alone does not certify ASIO hardware. Smoke profiles and recordings are left under the printed temporary path for inspection.
