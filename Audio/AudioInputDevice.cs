namespace JerichoDown.Audio;

public sealed class AudioInputDevice
{
    public const int SystemAudioLoopbackDeviceNumber = -1000;
    public const int AsioInputDeviceNumber = -2000;
    public const string SystemAudioLoopbackDeviceName = "Computer audio (loopback)";

    public AudioInputDevice(
        int deviceNumber,
        string name,
        int maximumInputChannels = 0,
        string? endpointId = null,
        AudioInputBackend backend = AudioInputBackend.Windows)
    {
        DeviceNumber = deviceNumber;
        Name = name;
        MaximumInputChannels = Math.Max(0, maximumInputChannels);
        EndpointId = endpointId;
        Backend = backend;
    }

    public int DeviceNumber { get; }

    public string Name { get; }

    public int MaximumInputChannels { get; }

    public string? EndpointId { get; }

    public AudioInputBackend Backend { get; }

    public bool IsSystemAudioLoopback => DeviceNumber == SystemAudioLoopbackDeviceNumber;

    public bool IsAsio => Backend == AudioInputBackend.Asio;

    public static AudioInputDevice CreateSystemAudioLoopback()
    {
        return new AudioInputDevice(SystemAudioLoopbackDeviceNumber, SystemAudioLoopbackDeviceName, 2);
    }

    public override string ToString() => Name;
}

public enum AudioInputBackend
{
    Windows,
    Asio
}
