# Karaoke Module

Owns worship track playback, lyric display, queueing, vocal recording, and lyric-generation support.

Current UI orchestration is mostly in `src/EqualizerWindow.xaml.cs` while karaoke behavior is split into focused helpers.

Current entry points:
- `KaraokeTrackAudioReader.cs`
- `KaraokePlaybackPolicy.cs`

Responsibilities:
- Track loading and playback fallback.
- Supported backing-track extension policy.
- Restart-from-end and codec-failure fallback decisions.
- NAudio-backed sample-reader playback for supported backing-track formats.
- M4A/MP4 duration probing when normal sample readers cannot decode metadata.
- Tempo-rate sample provider and stereo vocal-reduction sample provider.
- Enhanced LRC parsing and display timing.
- Queue and browser behavior for worship tracks.
- Vocal recording and replay.
- Demucs/WhisperX integration helpers where they support worship lyrics.

Do not put podcast session playback or general mic DSP here.
