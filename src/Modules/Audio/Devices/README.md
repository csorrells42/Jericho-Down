# Audio.Devices Module

Owns audio device vocabulary, selected-channel modes, and output route policy.

Current entry points:
- `AudioInputDevice.cs`
- `AudioOutputDevice.cs`
- `AudioDeviceFormat.cs`
- `InputChannelMode.cs`
- `PrimaryCaptureSelector.cs`
- `ProcessedOutputRoutePlanner.cs`
- `WasapiOutputSettings.cs`

Consumed by:
- `src/EqualizerWindow.xaml.cs` for device selection and persisted UI state.
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` for capture and output routing.

Do not put live capture threads, CoreAudio session enumeration, ASIO driver startup, or recording export here.
