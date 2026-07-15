# Audio.Recording Module

Owns audio recording source selection, saved-file analysis, sample conversion, and compressed export.

Current entry points:
- `ProcessedRecordingSource.cs`
- `ProcessedAudioSampleConverter.cs`
- `AudioFileAnalyzer.cs`
- `AudioRecordingExporter.cs`

Consumed by:
- `JerichoDown.Modules.Audio.Live.MicrophoneSpectrumService` when writing processed program or selected-mic recordings.
- `EqualizerWindow.xaml.cs` when browsing and exporting saved recordings.

Do not put live capture startup, device enumeration, mixer policy, or karaoke lyric handling here.
