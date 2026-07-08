namespace JerichoDown.Audio;

public readonly record struct AudioDeviceFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public override string ToString() => $"{SampleRate / 1000d:0.#} kHz, {Channels} ch, {BitsPerSample}-bit";
}
