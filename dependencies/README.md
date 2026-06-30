# Dependencies

This folder contains runtime tools that Podcast Workbench loads relative to the executable folder, so the app does not depend on machine-wide PATH configuration.

## FFmpeg

- Runtime path beside the app: `dependencies/ffmpeg/win-x64/ffmpeg.exe`
- Used for: camera preview and future podcast audio/video recording/export.

The app project copies this folder beside the executable during normal builds and publish operations.

Before publishing this project publicly, verify the exact FFmpeg build license and include the corresponding FFmpeg license notices/source-offer requirements.
