namespace PodcastWorkbench.Video;

public sealed class CameraFrame
{
    public CameraFrame(byte[] bgraBytes, int width, int height, int stride)
    {
        BgraBytes = bgraBytes;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public byte[] BgraBytes { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }
}
