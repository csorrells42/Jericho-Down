namespace VoiceWorkbench.Serial;

public sealed class SerialLogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;

    public string Direction { get; init; } = "";

    public string Text { get; init; } = "";

    public string Hex { get; init; } = "";

    public string TimeLabel => Time.ToString("HH:mm:ss.fff");
}
