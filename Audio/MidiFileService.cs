using NAudio.Midi;

namespace JerichoDown.Audio;

public static class MidiFileService
{
    public static MidiFileSummary ReadSummary(string filePath, bool strictChecking = false)
    {
        var file = new MidiFile(filePath, strictChecking);
        var trackSummaries = ReadTrackSummaries(file);
        return new MidiFileSummary(
            filePath,
            System.IO.Path.GetFileName(filePath),
            file.FileFormat,
            file.Tracks,
            file.DeltaTicksPerQuarterNote,
            trackSummaries.Sum(track => track.EventCount),
            trackSummaries);
    }

    public static void Export(string filePath, MidiEventCollection events)
    {
        MidiFile.Export(filePath, events);
    }

    public static void ExportCopy(string sourceFilePath, string destinationFilePath, bool strictChecking = false)
    {
        var file = new MidiFile(sourceFilePath, strictChecking);
        Export(destinationFilePath, file.Events);
    }

    private static IReadOnlyList<MidiTrackSummary> ReadTrackSummaries(MidiFile file)
    {
        var summaries = new List<MidiTrackSummary>();
        for (var track = 0; track < file.Events.Tracks; track++)
        {
            var events = file.Events[track];
            var noteEvents = 0;
            var controlEvents = 0;
            var patchEvents = 0;
            long lastAbsoluteTime = 0;
            string? firstEventName = null;

            foreach (var midiEvent in events)
            {
                if (firstEventName is null)
                {
                    firstEventName = midiEvent.CommandCode.ToString();
                }

                lastAbsoluteTime = Math.Max(lastAbsoluteTime, midiEvent.AbsoluteTime);
                switch (midiEvent.CommandCode)
                {
                    case MidiCommandCode.NoteOn:
                    case MidiCommandCode.NoteOff:
                        noteEvents++;
                        break;
                    case MidiCommandCode.ControlChange:
                        controlEvents++;
                        break;
                    case MidiCommandCode.PatchChange:
                        patchEvents++;
                        break;
                }
            }

            summaries.Add(new MidiTrackSummary(
                track + 1,
                events.Count,
                lastAbsoluteTime,
                noteEvents,
                controlEvents,
                patchEvents,
                firstEventName ?? "Empty"));
        }

        return summaries;
    }
}

public sealed record MidiFileSummary(
    string FilePath,
    string FileName,
    int FileFormat,
    int Tracks,
    int DeltaTicksPerQuarterNote,
    int EventCount,
    IReadOnlyList<MidiTrackSummary> TrackSummaries)
{
    public string DisplayText => $"{FileName} | format {FileFormat} | tracks {Tracks} | {DeltaTicksPerQuarterNote} ticks/qn | {EventCount} events";
}

public sealed record MidiTrackSummary(
    int TrackNumber,
    int EventCount,
    long LastAbsoluteTime,
    int NoteEventCount,
    int ControlEventCount,
    int PatchEventCount,
    string FirstEventName)
{
    public string DisplayName => $"Track {TrackNumber}: {EventCount} events";

    public string Details => $"{NoteEventCount} notes | {ControlEventCount} controls | {PatchEventCount} patches | end {LastAbsoluteTime} ticks | starts {FirstEventName}";
}
