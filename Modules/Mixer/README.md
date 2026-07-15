# Mixer Module

Owns the live program mix.

Responsibilities:
- Mixer channel gain, pan, mute, solo, delay, and meter behavior.
- Program bus construction and live output feed.
- CoreAudio app mix capture routing where it feeds the program bus.
- Recording source selection for selected mics and program mix.

Current legacy files live mostly in `Audio`.

Do not put low-level ASIO driver startup, camera preview, or karaoke lyrics here.
