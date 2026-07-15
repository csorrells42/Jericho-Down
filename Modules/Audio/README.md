# Audio Module

Owns audio capture, playback output routing, recording, loopback, synchronization, and shared NAudio helper code.

Most entry points still live in the legacy `Audio` folder while migration is in progress. Driver wrappers and sync helpers now live under this module.

Important submodules:
- `Asio`: ASIO-specific driver startup, callback testing, and diagnostics.
- `Dsp`: voice processor, EQ, verification, and signal shaping.
- `Sync`: delay lines, auxiliary latency buffers, and NAudio-backed sample-rate conversion.

Do not put camera preview, video recording, karaoke lyric display, or WPF tab orchestration here.
