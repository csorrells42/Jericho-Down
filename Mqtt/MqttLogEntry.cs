namespace VoiceWorkbench.Mqtt;

public sealed class MqttLogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;

    public string Direction { get; init; } = "";

    public string Topic { get; init; } = "";

    public string Payload { get; init; } = "";

    public int QualityOfService { get; init; }

    public bool Retained { get; init; }

    public string TimeLabel => Time.ToString("HH:mm:ss.fff");

    public string RetainedLabel => Retained ? "Yes" : "";
}
