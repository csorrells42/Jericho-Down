namespace JerichoDown.Audio;

public enum ProcessedOutputRouteBackend
{
    WasapiFloat,
    WasapiPcm,
    WaveOutFloat,
    WaveOutPcm
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
                ProcessedOutputRouteBackend.WaveOutPcm
            ]
            :
            [
                ProcessedOutputRouteBackend.WasapiFloat,
                ProcessedOutputRouteBackend.WasapiPcm
            ];
    }
}
