using NAudio.SoundFont;
using System.IO;

namespace JerichoDown.Audio;

public static class SoundFontLibrary
{
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
    int SampleLength)
{
    public string DisplayName => Name;

    public string Details => $"{SampleRate} Hz | pitch {OriginalPitch} | {SampleLength} samples";

    public override string ToString() => DisplayName;
}
