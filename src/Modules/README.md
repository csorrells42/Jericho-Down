# Jericho Down Modules

This folder is the long-term ownership map for reusable Jericho Down modules. A module should make it obvious what a piece of code owns, what it depends on, and where future work belongs.

`src/Modules` is the reuse boundary: code here should be written so the webcam, ASIO, mixer, MIDI, and DirectX rendering pieces can eventually be lifted into another app with minimal Jericho Down-specific code attached.

Application-specific WPF windows, menus, settings, tabs, and command wiring belong directly in `src`, not in this module tree.

The first migration passes keep everything inside the main WPF project so behavior stays stable. Move code here in small slices, build, run the test harness, then commit before moving the next boundary.

## Rules

- Prefer small moves with no behavior change.
- Move one small ownership boundary at a time.
- Keep app-specific window code thin; module code should own reusable behavior and diagnostics.
- Document the module before moving significant code into it.
- Avoid cross-module shortcuts. If a module needs another module, name that dependency in its README.
- Tests should describe the ownership boundary when possible.

## Module Map

- `Audio`: capture, output routing, recording, loopback, synchronization, and NAudio helpers.
- `Audio/Asio`: ASIO capture, callback probing, STA dispatcher, and ASIO diagnostics.
- `Audio/Dsp`: voice processing, EQ, presets, DSP verification, and signal analysis.
- `Audio/Live`: live microphone service orchestration, capture startup, processed monitoring, recording routing, spectrum publishing, and live output coordination.
- `Mixer`: live program bus, channel strips, pan, gain, mute, solo, delay, and metering.
- `Webcam`: camera device vocabulary, controls, preview policy, recording policy, frame/color helpers, and camera status text.
- `Webcam/MediaFoundation`: Media Foundation camera enumeration, modes, source readers, and video writing.
- `Webcam/DirectShow`: DirectShow camera enumeration, controls, and fallback preview capture.
- `Webcam/Dx12`: DX12 preview host, texture-native camera stream, GPU denoise, and GPU preview diagnostics.
- `Webcam/Dx11Bridge`: D3D11 bridge code used when camera frames need to cross into DX12 rendering.
- `DirectX12Viewport`: reusable WPF child-window viewport plumbing for DX12 swap-chain renderers.
- `SessionPlayback`: playback and cataloging of saved podcast sessions, including MP4 video pacing, sidecar WAV audio selection, session folder naming, and recording set numbering.
- `Karaoke`: worship track playback, lyrics, queueing, vocal recording, and lyric generation support.
- `Midi`: MIDI device catalog, file playback planning, monitoring, output, and control mappings.
- `Help`: About, verification, and tab-specific guide assets.
- `Visualization`: spectrum, waveform, waterfall, and non-camera graph ownership.
- `Visualization/Dx12`: DX12 audio graph rendering and retained graph history.

The DX12 code is currently split by use case:
- `DirectX12Viewport` owns reusable WPF/DX12 viewport hosting.
- `Webcam/Dx12` owns camera/video preview surfaces and GPU camera processing.
- `Visualization/Dx12` owns audio waveform, spectrum, waterfall, and retained graph rendering.

Keep visual content separate from the reusable viewport: camera frames and audio graphs can use the same viewport host without sharing renderer logic.

## Migration Status

- `Help` owns `AboutView` and `VerificationView`.
- `Karaoke` owns `KaraokePlaybackPolicy`, `KaraokeTrackAudioReader`, `KaraokeRateSampleProvider`, and `KaraokeVocalReductionSampleProvider`.
- `SessionPlayback` owns `MediaFoundationFilePlaybackService`, `SessionPlaybackAudioResolver`, and `SessionRecordingCatalog`.
- `Webcam` owns `CameraStatusText` and `VideoRecordingPolicy`, plus `VideoFrameColorSettings`.
- `Webcam` owns `VideoFrameDenoiser` for CPU BGRA temporal denoise used by preview/recording fallbacks.
- `Webcam` owns `CameraDevice`, `CameraVideoMode`, `CameraFrame`, `CameraControlKind`, and `CameraControlItem`.
- `Webcam` owns `CameraDeviceCatalog`, `CameraControlText`, `CameraProfile`, and `CameraProfileStore`.
- `Webcam` owns `CameraSourceSelection` and `TextureNativePreviewPolicy`; they route camera choices and recording policy into the provider modules.
- `Webcam/MediaFoundation` owns `MediaFoundationGuids` and `MediaFoundationInterop`.
- `Webcam/MediaFoundation` owns `MediaFoundationCameraEnumerator`, `MediaFoundationCameraModeService`, `MediaFoundationCameraDeviceFactory`, `MediaFoundationVideoRecorder`, and `MediaFoundationCameraPreviewService`.
- `Webcam/DirectShow` owns `DirectShowCameraEnumerator`, `DirectShowCameraControlService`, and `DirectShowCameraPreviewService`.
- `Webcam/Dx11Bridge` owns `Direct3D11DeviceManager` and `Direct3D11SharedTextureBridge`.
- `Webcam/Dx12` owns `Direct3D12DeviceManager`, `ITextureNativeDeviceManager`, `Direct3D12PreviewHost`, `Dx12Camera`, `Dx12CameraOptions`, `CameraPreviewFramePumps`, `TextureNativeCameraRecorder`, and `TextureNativeCameraProbe`.
- `DirectX12Viewport` owns `DirectX12ViewportHost`, the reusable WPF child-window host used by DX12 renderers.
- `Visualization` owns `SpectrumAnalyzer`, `SpectrumFrame`, `SpectrumFrameRouter`, and `FeedbackDangerDetector`.
- `Visualization/Dx12` owns `Direct3D12AudioGraphHost` and `Direct3D12AudioGraphMode`.
- `Audio/Asio` owns `AsioInputCapture`, `AsioCallbackProbe`, `AsioOutputPlayer`, and `StaThreadDispatcher`.
- `Audio/Capture` owns `ProcessLoopbackCapture` and `SignalGeneratorCapture`.
- `Audio/CoreAudio` owns `CoreAudioSessionCatalog` and `AudioDeviceNotificationWatcher`.
- `Audio/Devices` owns `AudioInputDevice`, `AudioOutputDevice`, `AudioDeviceFormat`, `InputChannelMode`, `PrimaryCaptureSelector`, `ProcessedOutputRoutePlanner`, and `WasapiOutputSettings`.
- `Audio/Diagnostics` owns `AudioDeviceDiagnostics`.
- `Audio/Dsp` owns `DspVerificationReportGenerator`, `VoiceProcessorSettings`, `BuiltInVoicePresetCatalog`, `VoiceProcessingTelemetry`, `EqualizerBand`, `VoiceSampleProcessor`, `VoiceProcessorSampleProvider`, `StereoVoiceProcessorSampleProvider`, and NAudio DSP effect wrappers.
- `Audio/Live` owns `MicrophoneSpectrumService`.
- `Audio/Recording` owns `AudioRecordingCatalog`, `ProcessedRecordingSource`, `ProcessedAudioSampleConverter`, `AudioFileAnalyzer`, and `AudioRecordingExporter`.
- `Audio/Sync` owns `AudioDelayLine`, `AudioStereoDelayLine`, `AudioSyncBuffer`, and `NAudioSampleRateConverter`.
- `Mixer` owns `MixBusProcessor`, `LiveProgramMixBus`, live block sample providers, audibility gating, pan/balance sample providers, and `NaudioPeakMeterSampleProvider`.
- `Midi` owns `MidiDeviceCatalog`, `MidiFileService`, `MidiHexParser`, `MidiInputMonitor`, `MidiMessageSnapshot`, `MidiOutputPort`, `MidiSequenceService`, MIDI control mappings, and `SoundFontLibrary`.
- The former legacy audio helpers now live under documented Audio submodules; the former camera/video and visualization DX12 code now live under modules.
