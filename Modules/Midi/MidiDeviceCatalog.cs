using NAudio.Midi;

namespace JerichoDown.Modules.Midi;

public static class MidiDeviceCatalog
{
    public static IReadOnlyList<MidiInputDevice> GetInputDevices()
    {
        var devices = new List<MidiInputDevice>();
        var count = SafeDeviceCount(() => MidiIn.NumberOfDevices);
        for (var i = 0; i < count; i++)
        {
            try
            {
                var capabilities = MidiIn.DeviceInfo(i);
                devices.Add(new MidiInputDevice(
                    i,
                    CleanName(capabilities.ProductName, i),
                    capabilities.Manufacturer.ToString(),
                    capabilities.ProductId));
            }
            catch
            {
                devices.Add(new MidiInputDevice(i, $"MIDI input {i + 1}", "Unknown", 0));
            }
        }

        return devices;
    }

    public static IReadOnlyList<MidiOutputDevice> GetOutputDevices()
    {
        var devices = new List<MidiOutputDevice>();
        var count = SafeDeviceCount(() => MidiOut.NumberOfDevices);
        for (var i = 0; i < count; i++)
        {
            try
            {
                var capabilities = MidiOut.DeviceInfo(i);
                devices.Add(new MidiOutputDevice(
                    i,
                    CleanName(capabilities.ProductName, i),
                    capabilities.Manufacturer.ToString(),
                    capabilities.ProductId,
                    capabilities.Technology.ToString(),
                    capabilities.Voices,
                    capabilities.Notes,
                    capabilities.SupportsAllChannels,
                    capabilities.SupportsVolumeControl,
                    capabilities.SupportsSeparateLeftAndRightVolume,
                    capabilities.SupportsPatchCaching,
                    capabilities.SupportsMidiStreamOut));
            }
            catch
            {
                devices.Add(new MidiOutputDevice(i, $"MIDI output {i + 1}", "Unknown", 0, "Unknown", 0, 0, true, false, false, false, false));
            }
        }

        return devices;
    }

    private static int SafeDeviceCount(Func<int> readCount)
    {
        try
        {
            return Math.Max(0, readCount());
        }
        catch
        {
            return 0;
        }
    }

    private static string CleanName(string? name, int deviceNumber)
    {
        return string.IsNullOrWhiteSpace(name) ? $"MIDI device {deviceNumber + 1}" : name.Trim();
    }
}

public sealed record MidiInputDevice(
    int DeviceNumber,
    string ProductName,
    string Manufacturer,
    int ProductId)
{
    public string DisplayName => $"{DeviceNumber + 1}: {ProductName}";

    public string Details => $"{Manufacturer} | Product {ProductId}";

    public override string ToString() => DisplayName;
}

public sealed record MidiOutputDevice(
    int DeviceNumber,
    string ProductName,
    string Manufacturer,
    int ProductId,
    string Technology,
    int Voices,
    int Notes,
    bool SupportsAllChannels,
    bool SupportsVolumeControl,
    bool SupportsSeparateLeftAndRightVolume,
    bool SupportsPatchCaching,
    bool SupportsMidiStreamOut)
{
    public string DisplayName => $"{DeviceNumber + 1}: {ProductName}";

    public string Details => $"{Manufacturer} | {Technology} | Voices {Voices} | Notes {Notes}";

    public override string ToString() => DisplayName;
}
