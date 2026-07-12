using NAudio.Midi;

namespace JerichoDown.Audio;

public sealed class MidiOutputPort : IDisposable
{
    private readonly object _gate = new();
    private MidiOut? _midiOut;

    public bool IsOpen { get; private set; }

    public int? DeviceNumber { get; private set; }

    public void Open(MidiOutputDevice device)
    {
        Open(device.DeviceNumber);
    }

    public void Open(int deviceNumber)
    {
        lock (_gate)
        {
            CloseLocked();
            try
            {
                _midiOut = new MidiOut(deviceNumber);
                DeviceNumber = deviceNumber;
                IsOpen = true;
            }
            catch (Exception ex)
            {
                CloseLocked();
                throw new InvalidOperationException($"MIDI output {deviceNumber + 1} could not open: {ex.Message}", ex);
            }
        }
    }

    public int SendNoteOn(int channel, int note, int velocity)
    {
        var raw = CreateNoteOnRawMessage(channel, note, velocity);
        SendRawMessage(raw);
        return raw;
    }

    public int SendNoteOff(int channel, int note, int velocity)
    {
        var raw = CreateNoteOffRawMessage(channel, note, velocity);
        SendRawMessage(raw);
        return raw;
    }

    public int SendControlChange(int channel, int controller, int value)
    {
        var raw = CreateControlChangeRawMessage(channel, controller, value);
        SendRawMessage(raw);
        return raw;
    }

    public int SendPatchChange(int channel, int patch)
    {
        var raw = CreatePatchChangeRawMessage(channel, patch);
        SendRawMessage(raw);
        return raw;
    }

    public IReadOnlyList<int> SendBankSelect(int channel, int bank)
    {
        var normalizedBank = Math.Clamp(bank, 0, 16383);
        var bankMsb = (normalizedBank >> 7) & 0x7F;
        var bankLsb = normalizedBank & 0x7F;
        return
        [
            SendControlChange(channel, (int)MidiController.BankSelect, bankMsb),
            SendControlChange(channel, (int)MidiController.BankSelectLsb, bankLsb)
        ];
    }

    public int SendPitchWheel(int channel, int value)
    {
        var raw = CreatePitchWheelRawMessage(channel, value);
        SendRawMessage(raw);
        return raw;
    }

    public void SendRawMessage(int rawMessage)
    {
        lock (_gate)
        {
            EnsureOpen();
            _midiOut!.Send(rawMessage & 0x00FFFFFF);
        }
    }

    public void SendSysex(byte[] bytes)
    {
        lock (_gate)
        {
            EnsureOpen();
            _midiOut!.SendBuffer(bytes);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_midiOut is not null)
            {
                _midiOut.Reset();
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            CloseLocked();
        }
    }

    public void Dispose()
    {
        Close();
    }

    public static int CreateNoteOnRawMessage(int channel, int note, int velocity)
    {
        return MidiMessage.StartNote(NormalizeSevenBit(note), NormalizeSevenBit(velocity), NormalizeChannel(channel)).RawData;
    }

    public static int CreateNoteOffRawMessage(int channel, int note, int velocity)
    {
        return MidiMessage.StopNote(NormalizeSevenBit(note), NormalizeSevenBit(velocity), NormalizeChannel(channel)).RawData;
    }

    public static int CreateControlChangeRawMessage(int channel, int controller, int value)
    {
        return MidiMessage.ChangeControl(NormalizeSevenBit(controller), NormalizeSevenBit(value), NormalizeChannel(channel)).RawData;
    }

    public static int CreatePatchChangeRawMessage(int channel, int patch)
    {
        return MidiMessage.ChangePatch(NormalizeSevenBit(patch), NormalizeChannel(channel)).RawData;
    }

    public static IReadOnlyList<int> CreateBankSelectRawMessages(int channel, int bank)
    {
        var normalizedBank = Math.Clamp(bank, 0, 16383);
        var bankMsb = (normalizedBank >> 7) & 0x7F;
        var bankLsb = normalizedBank & 0x7F;
        return
        [
            CreateControlChangeRawMessage(channel, (int)MidiController.BankSelect, bankMsb),
            CreateControlChangeRawMessage(channel, (int)MidiController.BankSelectLsb, bankLsb)
        ];
    }

    public static int CreatePitchWheelRawMessage(int channel, int value)
    {
        var normalized = Math.Clamp(value, 0, 16383);
        var lsb = normalized & 0x7F;
        var msb = (normalized >> 7) & 0x7F;
        var status = 0xE0 | (NormalizeChannel(channel) - 1);
        return new MidiMessage(status, lsb, msb).RawData;
    }

    public static int NormalizeChannel(int channel)
    {
        return Math.Clamp(channel, 1, 16);
    }

    public static int NormalizeSevenBit(int value)
    {
        return Math.Clamp(value, 0, 127);
    }

    private void EnsureOpen()
    {
        if (_midiOut is null)
        {
            throw new InvalidOperationException("MIDI output is not open.");
        }
    }

    private void CloseLocked()
    {
        if (_midiOut is null)
        {
            IsOpen = false;
            DeviceNumber = null;
            return;
        }

        try
        {
            _midiOut.Reset();
        }
        catch
        {
        }

        _midiOut.Dispose();
        _midiOut = null;
        IsOpen = false;
        DeviceNumber = null;
    }
}
