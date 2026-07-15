namespace JerichoDown.Modules.Midi;

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

    public bool IsMomentary => string.Equals(MessageType, "Note On", StringComparison.Ordinal)
        || string.Equals(MessageType, "Control Change", StringComparison.Ordinal);

    public bool Matches(MidiMessageSnapshot message)
    {
        return string.Equals(message.MessageType, MessageType, StringComparison.Ordinal)
            && (!Channel.HasValue || message.Channel == Channel)
            && (!Data1.HasValue || message.Data1 == Data1);
    }

    public bool ShouldTrigger(MidiMessageSnapshot message)
    {
        if (!Matches(message))
        {
            return false;
        }

        if (string.Equals(message.MessageType, "Note Off", StringComparison.Ordinal))
        {
            return false;
        }

        if ((string.Equals(message.MessageType, "Note On", StringComparison.Ordinal)
                || string.Equals(message.MessageType, "Control Change", StringComparison.Ordinal))
            && message.Data2.GetValueOrDefault() == 0)
        {
            return false;
        }

        return true;
    }

    public bool IsRelease(MidiMessageSnapshot message)
    {
        if (!ChannelMatches(message) || !Data1Matches(message))
        {
            return false;
        }

        if (string.Equals(MessageType, "Note On", StringComparison.Ordinal))
        {
            return string.Equals(message.MessageType, "Note Off", StringComparison.Ordinal)
                || (string.Equals(message.MessageType, "Note On", StringComparison.Ordinal)
                    && message.Data2.GetValueOrDefault() == 0);
        }

        return string.Equals(MessageType, "Control Change", StringComparison.Ordinal)
            && string.Equals(message.MessageType, "Control Change", StringComparison.Ordinal)
            && message.Data2.GetValueOrDefault() == 0;
    }

    public static MidiControlMappingRule FromMessage(MidiMessageSnapshot message, string actionName)
    {
        return new MidiControlMappingRule(
            actionName,
            message.MessageType,
            message.Channel,
            message.Data1);
    }

    private bool ChannelMatches(MidiMessageSnapshot message)
    {
        return !Channel.HasValue || message.Channel == Channel;
    }

    private bool Data1Matches(MidiMessageSnapshot message)
    {
        return !Data1.HasValue || message.Data1 == Data1;
    }
}

public static class MidiControlMappingActions
{
    public const string ToggleSelectedInputMute = "Toggle selected input mute";
    public const string ToggleSelectedInputSolo = "Toggle selected input solo";
    public const string ToggleProcessedOutput = "Toggle processed output";
    public const string StartOrStopRecording = "Start or stop recording";
    public const string SendAllNotesOff = "Send all notes off";
    public const string ResetMidiControllers = "Reset MIDI controllers";
    public const string MidiPanic = "MIDI panic";

    public static IReadOnlyList<string> DefaultActions { get; } =
    [
        ToggleSelectedInputMute,
        ToggleSelectedInputSolo,
        ToggleProcessedOutput,
        StartOrStopRecording,
        SendAllNotesOff,
        ResetMidiControllers,
        MidiPanic
    ];
}
