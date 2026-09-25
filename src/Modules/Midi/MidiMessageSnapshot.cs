using System.Globalization;

namespace JerichoDown.Modules.Midi;

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
                return $"{SysexBytes.Length} bytes | ts {Timestamp} | {MidiHexParser.ToHex(SysexBytes)}";
            }

            return $"{DecodedDataSummary} | raw {FormatRawMessage(RawMessage)} | ts {Timestamp}";
        }
    }

    public string InspectionText
    {
        get
        {
            if (SysexBytes is { Length: > 0 })
            {
                return $"{Direction} {MessageType} | {SysexBytes.Length} bytes | ts {Timestamp} | {MidiHexParser.ToHex(SysexBytes)}";
            }

            var channelText = Channel is int channel ? $" | ch {channel}" : string.Empty;
            return $"{Direction} {MessageType}{channelText} | {DecodedDataSummary} | raw {FormatRawMessage(RawMessage)} | ts {Timestamp}";
        }
    }

    private string DecodedDataSummary => MessageType switch
    {
        "Note On" or "Note Off" => $"note {Data1 ?? 0} {FormatNoteName(Data1)} | velocity {Data2 ?? 0}",
        "Poly Aftertouch" => $"note {Data1 ?? 0} {FormatNoteName(Data1)} | pressure {Data2 ?? 0}",
        "Control Change" => $"cc {Data1 ?? 0} {FormatControllerName(Data1)} | value {Data2 ?? 0}",
        "Patch Change" => $"patch {Data1 ?? 0}",
        "Channel Aftertouch" => $"pressure {Data1 ?? 0}",
        "Pitch Wheel" => $"value {FormatPitchWheelValue(Data1, Data2)}",
        _ => $"data {Data1 ?? 0}, {Data2 ?? 0}"
    };

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

    public static string FormatNoteName(int? note)
    {
        if (!note.HasValue)
        {
            return string.Empty;
        }

        var normalized = Math.Clamp(note.Value, 0, 127);
        var octave = normalized / 12 - 1;
        var noteName = (normalized % 12) switch
        {
            0 => "C",
            1 => "C#",
            2 => "D",
            3 => "D#",
            4 => "E",
            5 => "F",
            6 => "F#",
            7 => "G",
            8 => "G#",
            9 => "A",
            10 => "A#",
            _ => "B"
        };
        return $"{noteName}{octave}";
    }

    public static string FormatControllerName(int? controller)
    {
        if (!controller.HasValue)
        {
            return string.Empty;
        }

        return controller.Value switch
        {
            0 => "Bank Select MSB",
            1 => "Modulation",
            2 => "Breath Controller",
            4 => "Foot Controller",
            7 => "Channel Volume",
            10 => "Pan",
            11 => "Expression",
            32 => "Bank Select LSB",
            64 => "Sustain Pedal",
            120 => "All Sound Off",
            121 => "Reset All Controllers",
            123 => "All Notes Off",
            _ => string.Empty
        };
    }

    public static string FormatPitchWheelValue(int? data1, int? data2)
    {
        var value = Math.Clamp(data1.GetValueOrDefault(), 0, 127)
            | (Math.Clamp(data2.GetValueOrDefault(), 0, 127) << 7);
        var offset = value - 8192;
        return offset == 0
            ? $"{value} center"
            : $"{value} {offset.ToString("+#;-#;0", CultureInfo.InvariantCulture)}";
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
