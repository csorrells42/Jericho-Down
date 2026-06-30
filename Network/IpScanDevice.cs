namespace VoiceWorkbench.Network;

public sealed class IpScanDevice
{
    public string Address { get; init; } = "";

    public string HostName { get; init; } = "";

    public string Status { get; init; } = "";

    public long? LatencyMs { get; init; }

    public int? Ttl { get; init; }

    public string MacAddress { get; init; } = "";

    public string OsGuess { get; init; } = "";

    public string Notes { get; init; } = "";

    public string LatencyLabel => LatencyMs is long latency ? $"{latency} ms" : "";

    public string TtlLabel => Ttl is int ttl ? ttl.ToString() : "";
}
