using System.Runtime.InteropServices;

namespace JerichoDown.Modules.Audio.Recording;

public static class ProcessedAudioSampleConverter
{
    public static int GetStereoFloat32ByteCount(int sampleCount, int sourceChannelCount)
    {
        return GetFrameCount(sampleCount, sourceChannelCount) * 2 * sizeof(float);
    }

    public static int GetStereoPcm16ByteCount(int sampleCount, int sourceChannelCount)
    {
        return GetFrameCount(sampleCount, sourceChannelCount) * 2 * sizeof(short);
    }

    public static int GetConvertedSampleCount(int sampleCount, int sourceChannelCount, int destinationChannelCount)
    {
        return GetFrameCount(sampleCount, sourceChannelCount) * Math.Max(1, destinationChannelCount);
    }

    public static int WriteStereoFloat32(
        ReadOnlySpan<float> samples,
        int sourceChannelCount,
        Span<byte> destination,
        Func<float, float>? prepareSample = null)
    {
        var byteCount = GetStereoFloat32ByteCount(samples.Length, sourceChannelCount);
        if (destination.Length < byteCount)
        {
            throw new ArgumentException("Destination is too small for the converted stereo float output.", nameof(destination));
        }

        sourceChannelCount = Math.Max(1, sourceChannelCount);
        var frameCount = GetFrameCount(samples.Length, sourceChannelCount);
        var outputSamples = MemoryMarshal.Cast<byte, float>(destination[..byteCount]);
        var offset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var left = GetFrameChannelSample(samples, sourceChannelCount, frame, 0);
            var right = sourceChannelCount > 1
                ? GetFrameChannelSample(samples, sourceChannelCount, frame, 1)
                : left;
            outputSamples[offset++] = Prepare(left, prepareSample);
            outputSamples[offset++] = Prepare(right, prepareSample);
        }

        return byteCount;
    }

    public static int WriteStereoPcm16(
        ReadOnlySpan<float> samples,
        int sourceChannelCount,
        Span<byte> destination,
        ref uint ditherState,
        Func<float, float>? prepareSample = null)
    {
        var byteCount = GetStereoPcm16ByteCount(samples.Length, sourceChannelCount);
        if (destination.Length < byteCount)
        {
            throw new ArgumentException("Destination is too small for the converted stereo PCM output.", nameof(destination));
        }

        sourceChannelCount = Math.Max(1, sourceChannelCount);
        var frameCount = GetFrameCount(samples.Length, sourceChannelCount);
        var outputSamples = MemoryMarshal.Cast<byte, short>(destination[..byteCount]);
        var offset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var left = GetFrameChannelSample(samples, sourceChannelCount, frame, 0);
            var right = sourceChannelCount > 1
                ? GetFrameChannelSample(samples, sourceChannelCount, frame, 1)
                : left;
            var dither = (NextDitherRandom(ref ditherState) - NextDitherRandom(ref ditherState)) / 32768d;
            outputSamples[offset++] = QuantizeToPcm16(Prepare(left, prepareSample), dither);
            dither = (NextDitherRandom(ref ditherState) - NextDitherRandom(ref ditherState)) / 32768d;
            outputSamples[offset++] = QuantizeToPcm16(Prepare(right, prepareSample), dither);
        }

        return byteCount;
    }

    public static int WriteChannelCount(
        ReadOnlySpan<float> samples,
        int sourceChannelCount,
        int destinationChannelCount,
        Span<float> destination)
    {
        sourceChannelCount = Math.Max(1, sourceChannelCount);
        destinationChannelCount = Math.Max(1, destinationChannelCount);
        var requiredSamples = GetConvertedSampleCount(samples.Length, sourceChannelCount, destinationChannelCount);
        if (destination.Length < requiredSamples)
        {
            throw new ArgumentException("Destination is too small for the converted channel count.", nameof(destination));
        }

        var frameCount = GetFrameCount(samples.Length, sourceChannelCount);
        if (destinationChannelCount == 1)
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var left = GetFrameChannelSample(samples, sourceChannelCount, frame, 0);
                var right = sourceChannelCount > 1
                    ? GetFrameChannelSample(samples, sourceChannelCount, frame, 1)
                    : left;
                destination[frame] = (left + right) * 0.5f;
            }

            return requiredSamples;
        }

        var write = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var left = GetFrameChannelSample(samples, sourceChannelCount, frame, 0);
            var right = sourceChannelCount > 1
                ? GetFrameChannelSample(samples, sourceChannelCount, frame, 1)
                : left;
            destination[write++] = left;
            destination[write++] = right;
            for (var channel = 2; channel < destinationChannelCount; channel++)
            {
                destination[write++] = 0f;
            }
        }

        return requiredSamples;
    }

    public static float GetFrameChannelSample(ReadOnlySpan<float> samples, int sourceChannelCount, int frame, int channel)
    {
        sourceChannelCount = Math.Max(1, sourceChannelCount);
        var index = checked(frame * sourceChannelCount + Math.Clamp(channel, 0, sourceChannelCount - 1));
        return index >= 0 && index < samples.Length
            ? Sanitize(samples[index])
            : 0f;
    }

    public static short QuantizeToPcm16(float inputSample, double dither = 0d)
    {
        var sample = Math.Clamp((double)Sanitize(inputSample) + dither, -1d, 1d);
        var scaled = sample < 0d
            ? sample * -short.MinValue
            : sample * short.MaxValue;
        var quantized = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(quantized, short.MinValue, short.MaxValue);
    }

    private static int GetFrameCount(int sampleCount, int sourceChannelCount)
    {
        return Math.Max(0, sampleCount) / Math.Max(1, sourceChannelCount);
    }

    private static float Prepare(float sample, Func<float, float>? prepareSample)
    {
        return prepareSample is null
            ? sample
            : Sanitize(prepareSample(sample));
    }

    private static double NextDitherRandom(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state / ((double)uint.MaxValue + 1d);
    }

    private static float Sanitize(float sample)
    {
        return Math.Clamp(float.IsFinite(sample) ? sample : 0f, -1f, 1f);
    }
}
