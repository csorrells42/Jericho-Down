namespace JerichoDown.Audio;

public enum ProcessedOutputRouteBackend
{
    WasapiFloat,
    WasapiPcm,
    WaveOutFloat,
    WaveOutPcm,
    DirectSoundFloat,
    DirectSoundPcm
}

public static class ProcessedOutputRoutePlanner
{
    public static ProcessedOutputRouteBackend[] CreateAttemptOrder(bool canUseWaveOutFallback)
    {
        return canUseWaveOutFallback
            ?
            [
                ProcessedOutputRouteBackend.WasapiFloat,
                ProcessedOutputRouteBackend.WasapiPcm,
                ProcessedOutputRouteBackend.WaveOutFloat,
                ProcessedOutputRouteBackend.WaveOutPcm,
                ProcessedOutputRouteBackend.DirectSoundFloat,
                ProcessedOutputRouteBackend.DirectSoundPcm
            ]
            :
            [
                ProcessedOutputRouteBackend.WasapiFloat,
                ProcessedOutputRouteBackend.WasapiPcm
            ];
    }
}
