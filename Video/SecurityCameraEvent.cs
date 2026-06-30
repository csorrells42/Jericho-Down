namespace VoiceWorkbench.Video;

public sealed class SecurityCameraEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string TimeLabel => Timestamp.ToString("h:mm:ss tt");

    public string Event { get; init; } = "";

    public string Motion { get; init; } = "";

    public string File { get; init; } = "";
}
