using JerichoDown.Modules.Webcam;

namespace JerichoDown.Modules.Webcam.Dx12;

public sealed class Dx12CameraOptions
{
    public CameraDevice? Camera { get; init; }

    public CameraVideoMode? Mode { get; init; }

    public bool DenoiseEnabled { get; init; }

    public double DenoiseStrength { get; init; } = 2d;

    public EventHandler<TextureNativeFrameInfo>? FrameAvailable { get; init; }

    public EventHandler<TextureNativeFrameLease>? TextureFrameAvailable { get; init; }

    public EventHandler<string>? StatusChanged { get; init; }
}
