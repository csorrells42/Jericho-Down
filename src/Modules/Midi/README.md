# Midi Module

Owns MIDI features.

Responsibilities:
- MIDI input/output device catalog.
- MIDI file loading and tempo-aware playback plans.
- MIDI monitoring and output port handling.
- MIDI control mapping rules and trigger state.
- SoundFont sample preview support.

Current entry points:
- `MidiDeviceCatalog.cs`
- `MidiFileService.cs`
- `MidiHexParser.cs`
- `MidiInputMonitor.cs`
- `MidiMessageSnapshot.cs`
- `MidiOutputPort.cs`
- `MidiSequenceService.cs`
- `MidiControlMappingRule.cs`
- `MidiControlMappingTriggerState.cs`
- `SoundFontLibrary.cs`

The MIDI tab is opt-in and should stay hidden unless the user enables it from the File menu.
