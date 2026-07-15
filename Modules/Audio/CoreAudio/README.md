# Audio.CoreAudio Module

Owns Windows CoreAudio session and endpoint-notification helpers.

Current entry points:
- `CoreAudioSessionCatalog.cs`
- `AudioDeviceNotificationWatcher.cs`

Consumed by:
- `EqualizerWindow.xaml.cs` for device-change refreshes and app-session controls.
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` for app-audio loopback source discovery and session control.

Do not put ASIO startup, device vocabulary, recording export, or mixer policy here.
