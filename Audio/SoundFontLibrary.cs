using NAudio.SoundFont;
using NAudio.Wave;
using System.IO;

namespace JerichoDown.Audio;

public static class SoundFontLibrary
{
    private const int MaxPreviewDurationSeconds = 5;
    private const int MaxPreviewBytes = 10 * 1024 * 1024;

    public static SoundFontSummary LoadSummary(string filePath)
    {
        var soundFont = new SoundFont(filePath);
        var presets = soundFont.Presets
            .Select((preset, index) => new SoundFontPresetSummary(
                index,
                preset.Name,
                preset.Bank,
                preset.PatchNumber,
                preset.Zones?.Length ?? 0))
            .OrderBy(preset => preset.Bank)
            .ThenBy(preset => preset.Patch)
            .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var instruments = soundFont.Instruments
            .Select((instrument, index) => new SoundFontInstrumentSummary(
                index,
                instrument.Name,
                instrument.Zones?.Length ?? 0))
            .OrderBy(instrument => instrument.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var samples = soundFont.SampleHeaders
            .Select((sample, index) => new SoundFontSampleSummary(
                index,
                sample.SampleName,
                ClampToInt(sample.SampleRate),
                sample.OriginalPitch,
                sample.Start,
                sample.End,
                ClampToInt(sample.End > sample.Start ? sample.End - sample.Start : 0u)))
            .OrderBy(sample => sample.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SoundFontSummary(
            filePath,
            Path.GetFileName(filePath),
            presets,
            instruments,
            samples);
    }

    private static int ClampToInt(uint value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    public static WaveStream CreateSamplePreviewStream(string filePath, int sampleIndex)
    {
        var soundFont = new SoundFont(filePath);
        if (sampleIndex < 0 || sampleIndex >= soundFont.SampleHeaders.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleIndex), "SoundFont sample index is unavailable.");
        }

        var sampleHeader = soundFont.SampleHeaders[sampleIndex];
        var sampleRate = ClampToInt(sampleHeader.SampleRate);
        if (sampleRate <= 0)
        {
            throw new InvalidOperationException("SoundFont sample has no playable sample rate.");
        }

        var maxPreviewSamples = Math.Max(
            1,
            Math.Min((int)Math.Min((long)sampleRate * MaxPreviewDurationSeconds, int.MaxValue), MaxPreviewBytes / 2));
        var sampleBytes = CopySamplePcm16(soundFont.SampleData, sampleHeader.Start, sampleHeader.End, maxPreviewSamples);
        return new RawSourceWaveStream(
            new MemoryStream(sampleBytes, writable: false),
            new WaveFormat(sampleRate, bits: 16, channels: 1));
    }

    public static byte[] CopySamplePcm16(byte[] sampleData, uint startSample, uint endSample, int? maxSampleCount = null)
    {
        if (endSample <= startSample)
        {
            throw new InvalidOperationException("SoundFont sample has no playable data.");
        }

        if (maxSampleCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSampleCount), "Preview sample count must be positive.");
        }

        var requestedSampleCount = (long)endSample - startSample;
        var sampleCount = maxSampleCount.HasValue
            ? Math.Min(requestedSampleCount, maxSampleCount.Value)
            : requestedSampleCount;
        var startByte = (long)startSample * 2L;
        var lengthBytes = sampleCount * 2L;
        var endByte = startByte + lengthBytes;
        if (lengthBytes <= 0 || endByte > sampleData.LongLength || endByte <= startByte)
        {
            throw new InvalidOperationException("SoundFont sample data is outside the loaded sample buffer.");
        }

        var length = (int)lengthBytes;
        var preview = new byte[length];
        Buffer.BlockCopy(sampleData, (int)startByte, preview, 0, length);
        return preview;
    }
}

public sealed record SoundFontSummary(
    string FilePath,
    string FileName,
    IReadOnlyList<SoundFontPresetSummary> Presets,
    IReadOnlyList<SoundFontInstrumentSummary> Instruments,
    IReadOnlyList<SoundFontSampleSummary> Samples)
{
    public string DisplayText => $"{FileName} | {Presets.Count} presets | {Instruments.Count} instruments | {Samples.Count} samples";
}

public sealed record SoundFontPresetSummary(
    int Index,
    string Name,
    int Bank,
    int Patch,
    int ZoneCount)
{
    public string DisplayName => $"{Bank}:{Patch:000} {Name}";

    public string Details => $"{ZoneCount} zones";

    public override string ToString() => DisplayName;
}

public sealed record SoundFontInstrumentSummary(
    int Index,
    string Name,
    int ZoneCount)
{
    public string DisplayName => Name;

    public string Details => $"{ZoneCount} zones";

    public override string ToString() => DisplayName;
}

public sealed record SoundFontSampleSummary(
    int Index,
    string Name,
    int SampleRate,
    int OriginalPitch,
    uint Start,
    uint End,
    int SampleLength)
{
    public string DisplayName => Name;

    public string Details => $"{SampleRate} Hz | pitch {OriginalPitch} | {SampleLength} samples";

    public override string ToString() => DisplayName;
}
