# Karaoke Module

Owns worship track playback, lyric display, queueing, vocal recording, and lyric-generation support.

Current legacy code is mostly in `EqualizerWindow.xaml.cs` and `Audio` helper classes while migration is in progress.

Responsibilities:
- Track loading and playback fallback.
- Enhanced LRC parsing and display timing.
- Queue and browser behavior for worship tracks.
- Vocal recording and replay.
- Demucs/WhisperX integration helpers where they support worship lyrics.

Do not put podcast session playback or general mic DSP here.
