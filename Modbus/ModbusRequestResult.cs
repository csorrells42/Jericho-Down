namespace VoiceWorkbench.Modbus;

public sealed class ModbusRequestResult
{
    public bool Success { get; init; }

    public string Status { get; init; } = "";

    public byte[] RequestBytes { get; init; } = [];

    public byte[] ResponseBytes { get; init; } = [];

    public IReadOnlyList<ModbusValueRow> Values { get; init; } = [];
}
