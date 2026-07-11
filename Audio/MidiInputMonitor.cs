using NAudio.Midi;

namespace JerichoDown.Audio;

public sealed class MidiInputMonitor : IDisposable
{
    private readonly object _gate = new();
    private MidiIn? _midiIn;

    public event EventHandler<MidiMessageSnapshot>? MessageReceived;

    public event EventHandler<string>? StatusChanged;

    public bool IsRunning { get; private set; }

    public int? DeviceNumber { get; private set; }

    public void Start(MidiInputDevice device, bool receiveSysex = true)
    {
        Start(device.DeviceNumber, receiveSysex);
    }

    public void Start(int deviceNumber, bool receiveSysex = true)
    {
        lock (_gate)
        {
            StopLocked();
            try
            {
                _midiIn = new MidiIn(deviceNumber);
                _midiIn.MessageReceived += MidiInMessageReceived;
                _midiIn.ErrorReceived += MidiInErrorReceived;
                _midiIn.SysexMessageReceived += MidiInSysexMessageReceived;
                if (receiveSysex)
                {
                    TryCreateSysexBuffers(_midiIn);
                }

                _midiIn.Start();
                DeviceNumber = deviceNumber;
                IsRunning = true;
                StatusChanged?.Invoke(this, $"MIDI input {deviceNumber + 1} listening.");
            }
            catch (Exception ex)
            {
                StopLocked();
                throw new InvalidOperationException($"MIDI input {deviceNumber + 1} could not start: {ex.Message}", ex);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static void TryCreateSysexBuffers(MidiIn midiIn)
    {
        try
        {
            midiIn.CreateSysexBuffers(4096, 4);
        }
        catch
        {
        }
    }

    private void StopLocked()
    {
        if (_midiIn is null)
        {
            IsRunning = false;
            DeviceNumber = null;
            return;
        }

        try
        {
            _midiIn.Stop();
            _midiIn.Reset();
        }
        catch
        {
        }

        _midiIn.MessageReceived -= MidiInMessageReceived;
        _midiIn.ErrorReceived -= MidiInErrorReceived;
        _midiIn.SysexMessageReceived -= MidiInSysexMessageReceived;
        _midiIn.Dispose();
        _midiIn = null;
        IsRunning = false;
        DeviceNumber = null;
        StatusChanged?.Invoke(this, "MIDI input stopped.");
    }

    private void MidiInMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        MessageReceived?.Invoke(this, MidiMessageSnapshot.FromRaw(e.RawMessage, e.Timestamp));
    }

    private void MidiInErrorReceived(object? sender, MidiInMessageEventArgs e)
    {
        MessageReceived?.Invoke(this, MidiMessageSnapshot.FromRaw(e.RawMessage, e.Timestamp, "Error"));
    }

    private void MidiInSysexMessageReceived(object? sender, MidiInSysexMessageEventArgs e)
    {
        MessageReceived?.Invoke(this, MidiMessageSnapshot.FromSysex(e.SysexBytes, e.Timestamp));
    }
}
