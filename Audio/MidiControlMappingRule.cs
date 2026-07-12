namespace JerichoDown.Audio;

public sealed record MidiControlMappingRule(
    string ActionName,
    string MessageType,
    int? Channel,
    int? Data1)
{
    public string DisplayName => $"{ActionName} <- {InputName}";

    public string Details => Channel is int channel
        ? $"{MessageType} | channel {channel} | data 1 {Data1?.ToString() ?? "any"}"
        : $"{MessageType} | data 1 {Data1?.ToString() ?? "any"}";

    private string InputName => Channel is int channel
        ? $"{MessageType} ch {channel} d1 {Data1?.ToString() ?? "any"}"
        : $"{MessageType} d1 {Data1?.ToString() ?? "any"}";

    public bool Matches(MidiMessageSnapshot message)
    {
        return string.Equals(message.MessageType, MessageType, StringComparison.Ordinal)
            && (!Channel.HasValue || message.Channel == Channel)
            && (!Data1.HasValue || message.Data1 == Data1);
    }

    public static MidiControlMappingRule FromMessage(MidiMessageSnapshot message, string actionName)
    {
        return new MidiControlMappingRule(
            actionName,
            message.MessageType,
            message.Channel,
            message.Data1);
    }
}

public static class MidiControlMappingActions
{
    public const string ToggleSelectedInputMute = "Toggle selected input mute";
    public const string ToggleSelectedInputSolo = "Toggle selected input solo";
    public const string ToggleProcessedOutput = "Toggle processed output";
    public const string StartOrStopRecording = "Start or stop recording";
    public const string MidiPanic = "MIDI panic";

    public static IReadOnlyList<string> DefaultActions { get; } =
    [
        ToggleSelectedInputMute,
        ToggleSelectedInputSolo,
        ToggleProcessedOutput,
        StartOrStopRecording,
        MidiPanic
    ];
}
