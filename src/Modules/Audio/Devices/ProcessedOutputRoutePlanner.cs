namespace JerichoDown.Modules.Audio.Devices;

public enum ProcessedOutputRouteBackend
{
    AsioFloat,
    AsioPcm,
    WasapiFloat,
    WasapiPcm,
    WaveOutFloat,
    WaveOutPcm,
    DirectSoundFloat,
    DirectSoundPcm
}

public static class ProcessedOutputRoutePlanner
{
    public static ProcessedOutputRouteBackend[] CreateAttemptOrder(bool canUseWaveOutFallback, bool useAsioOutput = false)
    {
        if (useAsioOutput)
        {
            return
            [
                ProcessedOutputRouteBackend.AsioFloat,
                ProcessedOutputRouteBackend.AsioPcm
            ];
        }

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
