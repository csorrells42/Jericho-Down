# Audio.Dsp Module

Owns microphone voice processing, EQ, presets, verification, and signal-shaping math.

Current legacy files include:
- `Audio/VoiceSampleProcessor.cs`
- `Audio/VoiceProcessorSettings.cs`
- `Audio/VoiceProcessorSampleProvider.cs`
- `Audio/BuiltInVoicePresetCatalog.cs`
- `Audio/DspVerificationReportGenerator.cs`
- `Audio/NAudioBiQuadFilterRack.cs`
- `Audio/NAudioPitchShiftProcessor.cs`
- `Audio/NAudioImpulseConvolutionProcessor.cs`
- `Audio/NAudioEnvelopeGeneratorProcessor.cs`
- `Audio/NAudioDmoEffectChain.cs`

Keep NAudio-owned effect verification separate from custom Jericho DSP claims.
