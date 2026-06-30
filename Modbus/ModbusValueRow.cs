namespace VoiceWorkbench.Modbus;

public sealed class ModbusValueRow
{
    public int Address { get; init; }

    public string Kind { get; init; } = "";

    public string Value { get; init; } = "";

    public string Hex { get; init; } = "";
}
