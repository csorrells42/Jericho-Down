using System.Globalization;

namespace JerichoDown.Audio;

public sealed record MidiMessageSnapshot(
    DateTimeOffset ReceivedAt,
    string Direction,
    int Timestamp,
    int RawMessage,
    string MessageType,
    int? Channel,
    int? Data1,
    int? Data2,
    string Description,
    byte[]? SysexBytes = null)
{
    public string DisplayName => $"{ReceivedAt:HH:mm:ss.fff} {Direction} {Description}";

    public string Details
    {
        get
        {
            if (SysexBytes is { Length: > 0 })
            {
                return $"{SysexBytes.Length} bytes | {MidiHexParser.ToHex(SysexBytes)}";
            }

            return $"Raw {FormatRawMessage(RawMessage)} | data {Data1 ?? 0}, {Data2 ?? 0}";
        }
    }

    public static MidiMessageSnapshot FromRaw(int rawMessage, int timestamp, string direction = "In")
    {
        var status = rawMessage & 0xFF;
        var data1 = (rawMessage >> 8) & 0xFF;
        var data2 = (rawMessage >> 16) & 0xFF;
        var command = status & 0xF0;
        int? channel = command is >= 0x80 and <= 0xE0 ? (status & 0x0F) + 1 : null;
        var messageType = CommandName(command, status);
        var description = channel is null
            ? $"{messageType} {FormatRawMessage(rawMessage)}"
            : $"{messageType} ch {channel} d1 {data1} d2 {data2}";

        return new MidiMessageSnapshot(
            DateTimeOffset.Now,
            direction,
            timestamp,
            rawMessage,
            messageType,
            channel,
            data1,
            data2,
            description);
    }

    public static MidiMessageSnapshot FromSysex(byte[] bytes, int timestamp, string direction = "In")
    {
        return new MidiMessageSnapshot(
            DateTimeOffset.Now,
            direction,
            timestamp,
            0,
            "System Exclusive",
            null,
            null,
            null,
            $"System Exclusive ({bytes.Length} bytes)",
            bytes.ToArray());
    }

    public static string FormatRawMessage(int rawMessage)
    {
        var status = rawMessage & 0xFF;
        var data1 = (rawMessage >> 8) & 0xFF;
        var data2 = (rawMessage >> 16) & 0xFF;
        return string.Create(CultureInfo.InvariantCulture, $"{status:X2} {data1:X2} {data2:X2}");
    }

    private static string CommandName(int command, int status)
    {
        return command switch
        {
            0x80 => "Note Off",
            0x90 => "Note On",
            0xA0 => "Poly Aftertouch",
            0xB0 => "Control Change",
            0xC0 => "Patch Change",
            0xD0 => "Channel Aftertouch",
            0xE0 => "Pitch Wheel",
            _ => status switch
            {
                0xF0 => "System Exclusive",
                0xF1 => "MIDI Time Code",
                0xF2 => "Song Position",
                0xF3 => "Song Select",
                0xF6 => "Tune Request",
                0xF8 => "Timing Clock",
                0xFA => "Start",
                0xFB => "Continue",
                0xFC => "Stop",
                0xFE => "Active Sensing",
                0xFF => "System Reset",
                _ => "System Message"
            }
        };
    }
}
