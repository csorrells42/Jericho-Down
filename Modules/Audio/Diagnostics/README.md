# Audio.Diagnostics Module

Owns user-facing audio diagnostic reports.

Current entry points:
- `AudioDeviceDiagnostics.cs`

Consumed by:
- `Modules/AppShell/EqualizerWindow.xaml.cs` for the Help/File diagnostics popup and ASIO no-callback reports.

Do not put live capture startup, device enumeration, recording export, or mixer policy here.
