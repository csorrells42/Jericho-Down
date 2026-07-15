using NAudio.Midi;
using System.IO;
using System.Reflection;

namespace JerichoDown.Modules.Midi;

public static class MidiSequenceService
{
    private const double DefaultTempoMicrosecondsPerQuarterNote = 500_000d;
    private static readonly FieldInfo? SysexEventDataField = typeof(SysexEvent).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic);

    public static MidiSequencePlaybackPlan CreatePlaybackPlan(string filePath, bool strictChecking = false)
    {
        var file = new MidiFile(filePath, strictChecking);
        return CreatePlaybackPlan(file.Events, Path.GetFileName(filePath));
    }

    public static MidiSequencePlaybackPlan CreatePlaybackPlan(MidiEventCollection events, string fileName)
    {
        var playbackEvents = CreatePlaybackEvents(events, events.DeltaTicksPerQuarterNote);
        var duration = playbackEvents.Count == 0
            ? TimeSpan.Zero
            : playbackEvents[^1].Offset;
        return new MidiSequencePlaybackPlan(fileName, duration, playbackEvents);
    }

    public static IReadOnlyList<MidiSequencePlaybackEvent> CreatePlaybackEvents(MidiEventCollection events, int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote <= 0)
        {
            ticksPerQuarterNote = 480;
        }

        var orderedEvents = Enumerable.Range(0, events.Tracks)
            .SelectMany(track => events[track])
            .OrderBy(midiEvent => midiEvent.AbsoluteTime)
            .ThenBy(GetPlaybackSortOrder)
            .ToArray();
        var playbackEvents = new List<MidiSequencePlaybackEvent>();
        var tempoMicrosecondsPerQuarterNote = DefaultTempoMicrosecondsPerQuarterNote;
        var elapsedMicroseconds = 0d;
        long previousAbsoluteTicks = 0;

        foreach (var midiEvent in orderedEvents)
        {
            var absoluteTicks = Math.Max(previousAbsoluteTicks, midiEvent.AbsoluteTime);
            var elapsedTicks = absoluteTicks - previousAbsoluteTicks;
            elapsedMicroseconds += elapsedTicks * tempoMicrosecondsPerQuarterNote / ticksPerQuarterNote;
            previousAbsoluteTicks = absoluteTicks;

            if (midiEvent is TempoEvent tempoEvent)
            {
                tempoMicrosecondsPerQuarterNote = Math.Max(1, tempoEvent.MicrosecondsPerQuarterNote);
                continue;
            }

            if (midiEvent is SysexEvent sysexEvent && TryGetSysexBytes(sysexEvent, out var sysexBytes))
            {
                playbackEvents.Add(new MidiSequencePlaybackEvent(
                    TimeSpan.FromTicks((long)Math.Round(elapsedMicroseconds * TimeSpan.TicksPerMillisecond / 1000d)),
                    0,
                    "System Exclusive",
                    0,
                    sysexBytes));
                continue;
            }

            if (!TryCreateShortMessage(midiEvent, out var rawMessage))
            {
                continue;
            }

            playbackEvents.Add(new MidiSequencePlaybackEvent(
                TimeSpan.FromTicks((long)Math.Round(elapsedMicroseconds * TimeSpan.TicksPerMillisecond / 1000d)),
                rawMessage,
                midiEvent.CommandCode.ToString(),
                midiEvent.Channel));
        }

        return playbackEvents;
    }

    private static int GetPlaybackSortOrder(MidiEvent midiEvent)
    {
        return midiEvent switch
        {
            TempoEvent => 0,
            PatchChangeEvent => 1,
            ControlChangeEvent => 2,
            _ when MidiEvent.IsNoteOff(midiEvent) => 3,
            _ when MidiEvent.IsNoteOn(midiEvent) => 4,
            _ => 5
        };
    }

    private static bool TryCreateShortMessage(MidiEvent midiEvent, out int rawMessage)
    {
        rawMessage = 0;
        switch (midiEvent.CommandCode)
        {
            case MidiCommandCode.NoteOff:
            case MidiCommandCode.NoteOn:
            case MidiCommandCode.KeyAfterTouch:
            case MidiCommandCode.ControlChange:
            case MidiCommandCode.PatchChange:
            case MidiCommandCode.ChannelAfterTouch:
            case MidiCommandCode.PitchWheelChange:
                try
                {
                    rawMessage = midiEvent.GetAsShortMessage();
                    return true;
                }
                catch
                {
                    return false;
                }
            default:
                return false;
        }
    }

    private static bool TryGetSysexBytes(SysexEvent sysexEvent, out byte[] bytes)
    {
        bytes = [];
        if (SysexEventDataField?.GetValue(sysexEvent) is not byte[] data || data.Length == 0)
        {
            return false;
        }

        bytes = data.ToArray();
        return true;
    }
}

public sealed record MidiSequencePlaybackPlan(
    string FileName,
    TimeSpan Duration,
    IReadOnlyList<MidiSequencePlaybackEvent> Events)
{
    public string DisplayText => $"{FileName} | {Events.Count} playable events | {FormatDuration(Duration)}";

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1d
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }
}

public sealed record MidiSequencePlaybackEvent(
    TimeSpan Offset,
    int RawMessage,
    string MessageType,
    int Channel,
    byte[]? SysexBytes = null)
{
    public string DisplayName => $"{Offset:mm\\:ss\\.fff} {MessageType}";

    public string Details => SysexBytes is { Length: > 0 }
        ? $"{SysexBytes.Length} bytes | {MidiHexParser.ToHex(SysexBytes)}"
        : $"Raw {MidiMessageSnapshot.FormatRawMessage(RawMessage)} | channel {Channel}";
}
