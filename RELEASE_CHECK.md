# Code Review And Release Check - 2026-10-12

Status: repository-wide review and verified fixes completed; automated software and available Windows hardware checks pass. The user no longer owns the AG06, so that device is not a completion requirement. Physical ASIO/MIDI and screenshot verification remain unverified, not claimed as passed.

## October Review: Fixed Findings

The review covered audio capture/routing/recovery, custom and NAudio DSP, mixer providers, recording/export/path handling, MIDI, karaoke/file playback, camera/native resource lifetimes, graph shutdown, application persistence and WPF event wiring. It combined source inspection, targeted failure reproduction, the existing regression suite, and real-window/hardware probes. It is not a claim that every possible device, input file or concurrency interleaving has been certified.

| Priority | Defect and effect | Repair and evidence |
| --- | --- | --- |
| P1 | `VoiceSampleProcessor.UpdateBlockCoefficients` passed reversed clamp bounds at low input rates, throwing even with the de-esser disabled. | Bound both ends by the input rate. Regression covers 8, 11.025, 12, 16, 44.1, 48 and 96 kHz. |
| P1 | `NAudioBiQuadFilterRack.Slope` and the shelf sliders allowed slope 4 at +/-24 dB, producing invalid coefficients and silent output. | Restrict slope to the valid monotonic 0.1-1 range in DSP and UI. Both shelf types and gain extremes remain audible in regression tests. |
| P1 | `MixBusProcessor.ApplyLimiter` let output approach full scale regardless of the selected ceiling; a -24 dB setting produced approximately -0.6 dB peaks. | Enforce the actual ceiling and sanitize nonfinite ceiling settings. Tests cover -24 through 0 dB and NaN input. This is a safety clamp, not a new look-ahead limiter. |
| P1 | `PathSafety` checked only the final entry, so a regular recording beneath a parent junction could resolve outside the approved recording folder. | Check every component through the selected root and preserve volume-root normalization. A real isolated Windows junction reproduces and guards against the escape. This does not eliminate adversarial filesystem changes between validation and deletion. |
| P1 | `ProcessLoopbackCapture` created/used its COM audio client across incompatible apartments; the live per-app input failed with E_NOINTERFACE. | Activate and use the client on the dedicated MTA capture thread, declare an agile completion handler, and initialize a known shared format. Live capture of the probe's own generated audio now passes. |
| P2 | Loopback disposal from `DataAvailable` could release a buffer still in use; late activation could signal a disposed event; a restart could race old-client cleanup. | Keep native cleanup on the capture owner, serialize completion/disposal, and finish cleanup before allowing restart. Hardware tests cover stop/reopen and disposal inside the data callback; regression covers late completion. |
| P2 | `NAudioDmoEffectChain.Rebuild` constructed all nine native effects before checking whether they were enabled. Failed initialization/processing also had incomplete cleanup paths. | Instantiate only enabled effects, configure formats before allocation, release failed constructors and unsupported runtimes, and always attempt cleanup after processing errors. Existing DMO behavior tests pass. |
| P2 | `SessionAudioRecorder` left active WAV headers unwritten until stop, making an interrupted recording unreadable despite audio bytes being present. | Flush paired WAV headers every two seconds of active capture. Tests open both files while recording and verify readable audio. This limits loss; it does not guarantee recovery of the last unflushed block or a power-failed disk cache. |
| P2 | `MidiSequenceService` sorted simultaneous messages by type, moving instrument changes before their bank selection. | Preserve authored ordering at equal ticks. Regression verifies bank MSB/LSB precede the patch. |
| P2 | `MidiFileService.Export` truncated an existing destination before export could finish. | Stage to a sibling temporary file and replace only on success. An injected export failure now leaves the original byte-for-byte intact. |
| P2 | File playback and Media Foundation preview forgot workers after a stop timeout, allowing replacement sessions to share mutable state/native resources with an old worker. | Retain unfinished workers, reject overlapping starts, defer token/event disposal, check cancellation during startup, and prevent reopening disposed camera services. Regressions cover retained workers; real video playback stop/reopen passes. A hung third-party native call still cannot be forcibly made safe in-process. |
| P2 | Media Foundation recorder construction could leak its sink writer after setup failure; reader error/end-of-stream branches could omit sample release. | Release partially constructed writers and samples on those branches. Available-camera recording/playback tests pass; native allocation failures are not fault-injected. |
| P2 | Video segment rotation overwrote `session.json` and recorded cumulative rather than per-segment duration. | Write a sidecar per segment, keep legacy metadata compatibility, and track each segment's elapsed origin. Two-segment catalog and real recording tests verify metadata and duration. |

Checkpoint commits: `729c0b3`, `806e157`, `07ef13b`, `4ce86bc`, `9e80740`.

## October Verification

- Release solution build: zero warnings and zero errors; publish succeeded.
- Regression harness: **174 tests passed**, up from 162 at the start of this review.
- Real WPF smoke: five tabs, all 11 mixer strips, selection/mute, EQ loopback exclusion, recording transport, karaoke recording chain, failed-recovery retry bound, binding/dispatcher error checks and clean shutdown.
- Available-camera smoke: MP4 plus audible raw/mixed WAVs, pause/resume, segment rotation and per-segment metadata; recorded-video decode, pause/seek/resume, stop/reopen; capture interruption handling.
- Windows audio: Intel microphone array, MSI microphone, MSI virtual input and default speaker output passed. Per-app loopback captured this test process's generated audio, stopped, reopened and disposed safely from its callback.
- Build and published EXE/DLL SHA-256 hashes match. The review did not change the desktop shortcut or the user's saved input choices.
- Tests use isolated temporary profiles and recordings, not the user's live profile. Physical ASIO drivers are now excluded from the default hardware probe; add `--asio` explicitly when suitable hardware is available.
- Screenshot attempt: the desktop helper returned no usable application image and activation failed with `GetCursorPos failed: Access is denied (0x80070005)`. Do not confuse real WPF event tests with visual screenshot approval.

## Follow-up Engineering Work

These are recommendations, not additional claimed bug fixes:

- Split the roughly 17,000-line `EqualizerWindow.xaml.cs` by workflow ownership: recording/session orchestration, MIDI controller, karaoke controller, and device recovery. Preserve UI bindings and add behavioral tests around each extraction; avoid one large rewrite.
- Introduce explicit retired-processor ownership for live DSP graphs. DMO cleanup is now guarded, but abandoned `VoiceSampleProcessor` chains still rely on finalization; deterministic disposal must wait for in-flight audio callbacks rather than disposing a shared graph immediately.
- Continue replacing source-text contract assertions with executable behavior tests. The new regressions exercise actual processing, files, state and native capture, but some older tests only inspect source strings.
- Add long-duration recording/synchronization and hot-unplug tests on multiple PCs and GPUs. No physical ASIO or external MIDI device is available for this review, and native driver failure modes cannot be certified by software tests alone.

Interop references consulted while diagnosing loopback: [Microsoft's application loopback sample](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp) and [NAudio's activation callback](https://github.com/naudio/NAudio/blob/v2.3.0/NAudio.Wasapi/CoreAudioApi/ActivateAudioInterfaceCompletionHandler.cs).

## September Check (Historical)

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

## Historical Hardware And Desktop Limitations

At the September check, the installed Yamaha Steinberg USB ASIO driver reported "Device could not be opened." The saved primary input was that ASIO driver. The AG06 is no longer owned by the user; reconnecting it is not a requirement. Full physical ASIO certification still requires some compatible hardware.

The final Windows PnP audit found no present Yamaha/Steinberg audio device. The two retained AG06/AG03 software-device records both report `Present=False`. This is evidence that the hardware is unavailable to Windows, not proof of an application ASIO defect. Do not replace the saved Yamaha selection or repeatedly reopen its driver to work around missing hardware.

The Windows computer-use helper could not start because of a sandbox ACL error. The WPF probe executes real control events and checks bindings, but a screenshot-based visual review was not completed. MIDI message/mapping/file logic is covered by tests; external MIDI hardware was not exercised.

The desktop helper was retried after resetting its session and again exited with `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`. No live helper or verification process is being waited on. Resume visual verification when that execution environment works; do not treat the in-process WPF checks as screenshot evidence.

## Repeatable Commands

```powershell
.\tools\VerifyJerichoDown.ps1 -UiSmoke -AudioHardware
dotnet run --project tools\ReleaseSmokeProbe\ReleaseSmokeProbe.csproj -c Release -- --video
dotnet publish JerichoDown.csproj -c Release --no-restore -o output\release-check
```

The hardware probe excludes ASIO unless explicitly invoked with `--asio`; a successful process exit does not certify ASIO hardware. `--visual` keeps the isolated WPF window open for 60 seconds after its tests. Smoke profiles and recordings are left under the printed temporary path for inspection.
