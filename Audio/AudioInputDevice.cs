namespace JerichoDown.Audio;

public sealed class AudioInputDevice
{
    public const int SystemAudioLoopbackDeviceNumber = -1000;
    public const string SystemAudioLoopbackDeviceName = "Computer audio (loopback)";

    public AudioInputDevice(int deviceNumber, string name, int maximumInputChannels = 0)
    {
        DeviceNumber = deviceNumber;
        Name = name;
        MaximumInputChannels = Math.Max(0, maximumInputChannels);
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public int MaximumInputChannels { get; }

    public bool IsSystemAudioLoopback => DeviceNumber == SystemAudioLoopbackDeviceNumber;

    public static AudioInputDevice CreateSystemAudioLoopback()
    {
        return new AudioInputDevice(SystemAudioLoopbackDeviceNumber, SystemAudioLoopbackDeviceName, 2);
    }

    public override string ToString() => Name;
}


