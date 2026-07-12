using System.IO;
using NAudio.Wave;

namespace JerichoDown.Audio;

public sealed record AudioWaveformPeak(float Minimum, float Maximum);

public sealed record AudioFileAnalysis(
    string Path,
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    float PeakLevel,
    double RmsLevel,
    long SampleCount,
    long ClippedSamples,
    TimeSpan LeadingSilence,
    TimeSpan TrailingSilence,
    IReadOnlyList<AudioWaveformPeak> WaveformPeaks)
{
    public bool HasClipping => ClippedSamples > 0;

    public double ClippedSamplePercent => SampleCount <= 0
        ? 0d
        : ClippedSamples * 100d / SampleCount;

    public string BrowserSummary
    {
        get
        {
            var clippingText = HasClipping
                ? $" | clips {ClippedSamples} ({ClippedSamplePercent:0.###}%)"
                : string.Empty;
            return $"{FormatDuration(Duration)} | {SampleRate / 1000d:0.#} kHz, {Channels} ch, {BitsPerSample}-bit | peak {FormatLevelDb(PeakLevel)} | RMS {FormatLevelDb(RmsLevel)}{clippingText}";
        }
    }

    public string DetailSummary => $"{BrowserSummary} | silence lead {FormatDuration(LeadingSilence)}, tail {FormatDuration(TrailingSilence)} | waveform {WaveformPeaks.Count} buckets";

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero || !double.IsFinite(duration.TotalSeconds))
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours:0}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:0}:{duration.Seconds:00}";
    }

    public static string FormatLevelDb(double level)
    {
        if (level <= 0d || !double.IsFinite(level))
        {
            return "-inf dB";
        }

        return $"{20d * Math.Log10(Math.Clamp(level, double.Epsilon, 1d)):0.0} dB";
    }
}

public static class AudioFileAnalyzer
{
    private const int ReadBufferSampleCount = 8192;
    private const int MaximumWaveformBuckets = 80;
    private const float SilenceThreshold = 0.001f;
    private const float ClipThreshold = 0.999f;

    public static bool TryAnalyze(string path, out AudioFileAnalysis analysis, out string status)
    {
        analysis = default!;
        status = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = "Audio file was not found.";
            return false;
        }

        try
        {
            using var reader = new AudioFileReader(path);
            var waveFormat = reader.WaveFormat;
            var channels = Math.Max(1, waveFormat.Channels);
            var sampleRate = Math.Max(1, waveFormat.SampleRate);
            var bitsPerSample = waveFormat.BitsPerSample > 0 ? waveFormat.BitsPerSample : 32;
            var duration = reader.TotalTime;
            var estimatedFrames = Math.Max(1L, (long)Math.Round(Math.Max(0d, duration.TotalSeconds) * sampleRate));
            var bucketCount = (int)Math.Clamp(estimatedFrames, 1L, MaximumWaveformBuckets);
            var bucketFrameSize = Math.Max(1L, (long)Math.Ceiling(estimatedFrames / (double)bucketCount));
            var waveformPeaks = new List<AudioWaveformPeak>(bucketCount);
            var buffer = new float[ReadBufferSampleCount];
            long frameIndex = 0;
            long sampleCount = 0;
            long clippedSamples = 0;
            long firstAudibleFrame = -1;
            long lastAudibleFrame = -1;
            double sumSquares = 0d;
            var peakLevel = 0f;
            var bucketMinimum = 0f;
            var bucketMaximum = 0f;
            var bucketHasSamples = false;

            int samplesRead;
            while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var wholeFrameSamples = samplesRead - samplesRead % channels;
                for (var offset = 0; offset < wholeFrameSamples; offset += channels)
                {
                    var frameMinimum = 0f;
                    var frameMaximum = 0f;
                    var framePeak = 0f;
                    for (var channel = 0; channel < channels; channel++)
                    {
                        var sample = Sanitize(buffer[offset + channel]);
                        frameMinimum = Math.Min(frameMinimum, sample);
                        frameMaximum = Math.Max(frameMaximum, sample);
                        var absolute = Math.Abs(sample);
                        framePeak = Math.Max(framePeak, absolute);
                        peakLevel = Math.Max(peakLevel, absolute);
                        sumSquares += sample * sample;
                        sampleCount++;
                        if (absolute >= ClipThreshold)
                        {
                            clippedSamples++;
                        }
                    }

                    if (framePeak >= SilenceThreshold)
                    {
                        if (firstAudibleFrame < 0)
                        {
                            firstAudibleFrame = frameIndex;
                        }

                        lastAudibleFrame = frameIndex;
                    }

                    if (!bucketHasSamples)
                    {
                        bucketMinimum = frameMinimum;
                        bucketMaximum = frameMaximum;
                        bucketHasSamples = true;
                    }
                    else
                    {
                        bucketMinimum = Math.Min(bucketMinimum, frameMinimum);
                        bucketMaximum = Math.Max(bucketMaximum, frameMaximum);
                    }

                    frameIndex++;
                    if (frameIndex % bucketFrameSize == 0)
                    {
                        waveformPeaks.Add(new AudioWaveformPeak(bucketMinimum, bucketMaximum));
                        bucketMinimum = 0f;
                        bucketMaximum = 0f;
                        bucketHasSamples = false;
                    }
                }
            }

            if (bucketHasSamples || waveformPeaks.Count == 0)
            {
                waveformPeaks.Add(new AudioWaveformPeak(bucketMinimum, bucketMaximum));
            }

            var rmsLevel = sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0d;
            var leadingSilence = firstAudibleFrame < 0
                ? duration
                : TimeSpan.FromSeconds(firstAudibleFrame / (double)sampleRate);
            var trailingFrames = firstAudibleFrame < 0
                ? frameIndex
                : Math.Max(0L, frameIndex - lastAudibleFrame - 1L);
            var trailingSilence = TimeSpan.FromSeconds(trailingFrames / (double)sampleRate);

            analysis = new AudioFileAnalysis(
                path,
                duration,
                sampleRate,
                channels,
                bitsPerSample,
                peakLevel,
                rmsLevel,
                sampleCount,
                clippedSamples,
                leadingSilence,
                trailingSilence,
                waveformPeaks);
            status = analysis.DetailSummary;
            return true;
        }
        catch (Exception ex)
        {
            status = $"Audio analysis failed: {ex.Message}";
            return false;
        }
    }

    private static float Sanitize(float sample)
    {
        return float.IsFinite(sample)
            ? Math.Clamp(sample, -1f, 1f)
            : 0f;
    }
}
