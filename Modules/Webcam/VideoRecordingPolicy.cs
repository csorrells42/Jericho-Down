namespace JerichoDown.Modules.Webcam;

internal static class VideoRecordingPolicy
{
    internal static bool ShouldUseTextureNativeRecording()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_TEXTURE_NATIVE_RECORDING");
        return IsEnabledEnvironmentValue(value);
    }

    internal static bool ShouldRecordProcessedTextureOutput(bool denoiseEnabled)
    {
        return denoiseEnabled;
    }

    private static bool IsEnabledEnvironmentValue(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
