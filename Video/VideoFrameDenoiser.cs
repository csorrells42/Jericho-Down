namespace PodcastWorkbench.Video;

public sealed class VideoFrameDenoiser
{
    private byte[]? _previousFrame;

    public void Reset()
    {
        _previousFrame = null;
    }

    public void Apply(byte[] current, double strength)
    {
        ApplyTemporalDenoise(current, strength, ref _previousFrame);
    }

    public static void ApplyTemporalDenoise(byte[] current, double strength, ref byte[]? previousFrame)
    {
        var previous = previousFrame;
        if (previous is null || previous.Length != current.Length)
        {
            previousFrame = (byte[])current.Clone();
            return;
        }

        var previousWeight = Math.Clamp(strength / 12d, 0.05d, 0.62d);
        var currentWeight = 1d - previousWeight;
        for (var i = 0; i < current.Length; i += 4)
        {
            current[i] = Blend(current[i], previous[i], currentWeight, previousWeight);
            current[i + 1] = Blend(current[i + 1], previous[i + 1], currentWeight, previousWeight);
            current[i + 2] = Blend(current[i + 2], previous[i + 2], currentWeight, previousWeight);
            current[i + 3] = 255;
        }

        Buffer.BlockCopy(current, 0, previous, 0, current.Length);
    }

    private static byte Blend(byte current, byte previous, double currentWeight, double previousWeight)
    {
        return (byte)Math.Clamp((int)Math.Round(current * currentWeight + previous * previousWeight), 0, 255);
    }
}
