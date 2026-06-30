namespace VoiceWorkbench.Modbus;

public sealed record ModbusOperationOption(byte FunctionCode, string Label)
{
    public bool IsWrite => FunctionCode is 5 or 6 or 15 or 16;

    public override string ToString() => Label;
}
