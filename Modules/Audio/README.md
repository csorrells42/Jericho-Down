# Audio Module

Owns audio capture, playback output routing, recording, loopback, synchronization, and shared NAudio helper code.

Current entry points still live in the legacy `Audio` folder while migration is in progress.

Important submodules:
- `Asio`: ASIO-specific driver startup, callback testing, and diagnostics.
- `Dsp`: voice processor, EQ, verification, and signal shaping.

Do not put camera preview, video recording, karaoke lyric display, or WPF tab orchestration here.
