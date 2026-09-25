# Jericho Down Source

This folder contains source code for the Jericho Down application.

Responsibilities:

App-specific WPF files and app helpers live directly in `src`:
- `App.xaml` and `App.xaml.cs`
- `EqualizerWindow.xaml` and `EqualizerWindow.xaml.cs`
- `AppStateStore.cs`
- `AppStoragePaths.cs`
- `AtomicFile.cs`
- `PathSafety.cs`
- `FileBrowserWatcher.cs`

Reusable production code lives under `src/Modules`.

Do not create a module for app-specific WPF command wiring. If code is specific to this program's window, menu, tabs, settings, or command wiring, keep it directly in `src`. If code should be reusable in another app, put it under a focused module in `src/Modules`.
