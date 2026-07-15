# AppShell Module

Owns the WPF shell: main window orchestration, top-level menus, tab selection, persisted app state wiring, and UI command handlers.

Current entry points:
- `EqualizerWindow.xaml`
- `EqualizerWindow.xaml.cs`
- `App.xaml`
- `AppStateStore.cs`

Do not put camera frame processing, DSP math, ASIO driver handling, or DX12 rendering here. AppShell may coordinate those modules, but module code should own the behavior and diagnostics.
