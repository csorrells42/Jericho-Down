using System.Buffers;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JerichoDown.Modules.Audio.Sync;

public static class NAudioSampleRateConverter
{
    public static int EstimateOutputSampleCount(int sourceSampleCount, int sourceSampleRate, int targetSampleRate, int channelCount)
    {
        channelCount = Math.Max(1, channelCount);
        var sourceFrames = Math.Max(0, sourceSampleCount) / channelCount;
        if (sourceFrames == 0)
        {
            return 0;
        }

        if (sourceSampleRate <= 0 || sourceSampleRate == targetSampleRate)
        {
            return sourceFrames * channelCount;
        }

        var outputFrames = Math.Max(1, (int)Math.Round(sourceFrames * (double)Math.Max(1, targetSampleRate) / sourceSampleRate));
        return outputFrames * channelCount;
    }

    public static bool TryResampleInterleaved(
        ReadOnlySpan<float> source,
        int sourceSampleRate,
        int targetSampleRate,
        int channelCount,
        float[] destination,
        int destinationOffset,
        int destinationCount,
        out int samplesWritten)
    {
        samplesWritten = 0;
        channelCount = Math.Max(1, channelCount);
        sourceSampleRate = Math.Max(1, sourceSampleRate);
        targetSampleRate = Math.Max(1, targetSampleRate);
        if (source.IsEmpty || destinationCount <= 0)
        {
            return false;
        }

        if (destinationOffset < 0 || destinationOffset > destination.Length || destination.Length - destinationOffset < destinationCount)
        {
            throw new ArgumentException("Destination is too small for the requested resampled output.", nameof(destination));
        }

        var sourceSampleCount = source.Length / channelCount * channelCount;
        if (sourceSampleCount <= 0)
        {
            return false;
        }

        if (sourceSampleRate == targetSampleRate)
        {
            samplesWritten = Math.Min(sourceSampleCount, destinationCount);
            CopySanitized(source[..samplesWritten], destination.AsSpan(destinationOffset, samplesWritten));
            return samplesWritten > 0;
        }

        var sourceBuffer = ArrayPool<float>.Shared.Rent(sourceSampleCount);
        try
        {
            CopySanitized(source[..sourceSampleCount], sourceBuffer.AsSpan(0, sourceSampleCount));
            var sourceProvider = new ReadOnlyMemorySampleProvider(
                sourceBuffer,
                sourceSampleCount,
                WaveFormat.CreateIeeeFloatWaveFormat(sourceSampleRate, channelCount));
            var resampler = new WdlResamplingSampleProvider(sourceProvider, targetSampleRate);

            while (samplesWritten < destinationCount)
            {
                var read = resampler.Read(destination, destinationOffset + samplesWritten, destinationCount - samplesWritten);
                if (read <= 0)
                {
                    break;
                }

                samplesWritten += read;
            }

            Sanitize(destination.AsSpan(destinationOffset, samplesWritten));
            return samplesWritten > 0;
        }
        catch
        {
            samplesWritten = 0;
            return false;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(sourceBuffer);
        }
    }

    private static void CopySanitized(ReadOnlySpan<float> source, Span<float> destination)
    {
        for (var index = 0; index < source.Length; index++)
        {
            destination[index] = Sanitize(source[index]);
        }
    }

    private static void Sanitize(Span<float> samples)
    {
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Sanitize(samples[index]);
        }
    }

    private static float Sanitize(float sample)
    {
        return Math.Clamp(float.IsFinite(sample) ? sample : 0f, -1f, 1f);
    }

    private sealed class ReadOnlyMemorySampleProvider : ISampleProvider
    {
        private readonly float[] _source;
        private readonly int _length;
        private int _position;

        public ReadOnlyMemorySampleProvider(float[] source, int length, WaveFormat waveFormat)
        {
            _source = source;
            _length = Math.Clamp(length, 0, source.Length);
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Max(0, _length - _position);
            var copyCount = Math.Min(Math.Max(0, count), available);
            if (copyCount <= 0)
            {
                return 0;
            }

            Array.Copy(_source, _position, buffer, offset, copyCount);
            _position += copyCount;
            return copyCount;
        }
    }
}
