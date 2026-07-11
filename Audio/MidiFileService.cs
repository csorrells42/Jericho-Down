using NAudio.Midi;

namespace JerichoDown.Audio;

public static class MidiFileService
{
    public static MidiFileSummary ReadSummary(string filePath, bool strictChecking = false)
    {
        var file = new MidiFile(filePath, strictChecking);
        return new MidiFileSummary(
            filePath,
            System.IO.Path.GetFileName(filePath),
            file.FileFormat,
            file.Tracks,
            file.DeltaTicksPerQuarterNote,
            CountEvents(file.Events));
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

    private static int CountEvents(MidiEventCollection events)
    {
        var count = 0;
        for (var track = 0; track < events.Tracks; track++)
        {
            count += events[track].Count;
        }

        return count;
    }
}

public sealed record MidiFileSummary(
    string FilePath,
    string FileName,
    int FileFormat,
    int Tracks,
    int DeltaTicksPerQuarterNote,
    int EventCount)
{
    public string DisplayText => $"{FileName} | format {FileFormat} | tracks {Tracks} | {DeltaTicksPerQuarterNote} ticks/qn | {EventCount} events";
}
