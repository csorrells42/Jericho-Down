# Audio.Recording Module

Owns audio recording source selection, saved-file cataloging and analysis, sample conversion, and compressed export.

Current entry points:
- `AudioRecordingCatalog.cs`
- `ProcessedRecordingSource.cs`
- `ProcessedAudioSampleConverter.cs`
- `AudioFileAnalyzer.cs`
- `AudioRecordingExporter.cs`

Consumed by:
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` when writing processed program or selected-mic recordings.
- `src/EqualizerWindow.xaml.cs` when naming, browsing, validating, and exporting saved recordings.

Do not put live capture startup, device enumeration, mixer policy, or karaoke lyric handling here.
