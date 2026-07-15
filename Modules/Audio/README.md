# Audio Module

Owns audio capture, playback output routing, recording, loopback, synchronization, and shared NAudio helper code.

Most entry points still live in the legacy `Audio` folder while migration is in progress. Driver wrappers and sync helpers now live under this module.

Important submodules:
- `Asio`: ASIO-specific driver startup, callback testing, and diagnostics.
- `Capture`: process-loopback and signal-generator capture source implementations.
- `CoreAudio`: Windows CoreAudio session enumeration, session control, and device notifications.
- `Devices`: input/output device vocabulary, selected-channel modes, and output route policy.
- `Dsp`: voice processor, EQ, verification, and signal shaping.
- `Recording`: recording source selection, audio-file analysis, sample conversion, and compressed export.
- `Sync`: delay lines, auxiliary latency buffers, and NAudio-backed sample-rate conversion.

Do not put camera preview, video recording, karaoke lyric display, or WPF tab orchestration here.
