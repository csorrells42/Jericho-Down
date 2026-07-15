# AppShell Module

Owns the WPF shell: main window orchestration, top-level menus, tab selection, persisted app state wiring, and UI command handlers.

Current entry points:
- `EqualizerWindow.xaml`
- `EqualizerWindow.xaml.cs`
- `App.xaml`
- `App.xaml.cs`
- `AppStateStore.cs`
- `AppStoragePaths.cs`
- `AtomicFile.cs`
- `PathSafety.cs`
- `FileBrowserWatcher.cs`

Responsibilities:
- Startup/shutdown crash markers and diagnostics log rotation.
- Per-user settings storage under LocalAppData.
- Atomic writes for settings, profile, cache, metadata, and report files.
- Path-bounded delete/open-location helpers for recording and session browsers.
- Recording/session folder watcher refresh policy.
- Main window menu, tab, and command orchestration while feature behavior continues moving into focused modules.

Do not put camera frame processing, DSP math, ASIO driver handling, or DX12 rendering here. AppShell may coordinate those modules, but module code should own the behavior and diagnostics.
