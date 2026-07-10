namespace JerichoDown.Video;

internal static class CameraStatusText
{
    internal static string FormatCameraMode(CameraVideoMode mode)
    {
        return mode.IsAuto ? "Auto mode" : mode.Label;
    }

    internal static CameraVideoMode ResolveSelectedCameraMode(object? selectedItem)
    {
        return selectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
    }

    internal static string FormatCameraDisabledStatus(CameraVideoMode mode)
    {
        return $"Camera disabled - selected mode: {FormatCameraMode(mode)}";
    }

    internal static string FormatCameraStatus(string state, CameraDevice camera, CameraVideoMode mode)
    {
        return $"{state}: {camera.Name} at {FormatCameraMode(mode)}";
    }

    internal static string FormatDirectShowCameraSelectedStatus(CameraDevice camera)
    {
        return $"DirectShow camera selected: {camera.Name} - Auto uses the camera's current output";
    }

    internal static string FormatLoadingCameraModesStatus(CameraDevice camera, CameraVideoMode selectedMode)
    {
        return $"Loading modes: {camera.Name} - selected mode: {FormatCameraMode(selectedMode)}";
    }

    internal static string FormatCameraIdleStatus(bool cameraAvailable, CameraVideoMode mode)
    {
        return cameraAvailable
            ? FormatCameraDisabledStatus(mode)
            : "No camera source found";
    }

    internal static string FormatCameraPreviewStatus(string status, CameraDevice? camera, CameraVideoMode mode)
    {
        return camera is null
            ? status
            : $"{FormatCameraStatus("Preview", camera, mode)} - {status}";
    }

    internal static string FormatVideoDenoiseStatus(bool isTextureNative, bool denoiseEnabled)
    {
        if (isTextureNative)
        {
            return denoiseEnabled
                ? "DX12 video grain reduction is live on the preview and will be included in texture-native recordings."
                : "DX12 video grain reduction is off on the preview.";
        }

        return denoiseEnabled
            ? "Video grain reduction is live on the preview and CPU recording path."
            : "Video grain reduction is off on the preview and CPU recording path.";
    }

    internal static string FormatVideoColorPolishStatus(bool isTextureNative, VideoFrameColorSettings settings)
    {
        if (isTextureNative)
        {
            return settings.HasVisibleAdjustments
                ? "Color polish is armed for CPU fallback/recording. The current DX12 texture preview remains raw."
                : "Color polish is neutral.";
        }

        return settings.HasVisibleAdjustments
            ? "Color polish is live on the DX12 preview shader and recording path."
            : "Color polish is neutral on the preview and recording path.";
    }

    internal static object BuildVideoProcessingMetadata(
        TextureNativeRecordingResult? textureResult,
        Dx12Camera? activeCamera,
        bool denoiseEnabled,
        double denoiseSliderStrength,
        double denoiseStrength,
        VideoFrameColorSettings colorSettings,
        bool isDirectShow,
        bool isDirect3D12Ready)
    {
        if (textureResult is not null)
        {
            return new
            {
                previewPipeline = "Direct3D 12 NV12 shader preview",
                previewRenderPath = activeCamera?.PreviewPathDescription ?? "DX12 preview path unavailable",
                previewDenoiseApplied = denoiseEnabled,
                previewDenoiseSliderStrength = denoiseSliderStrength,
                previewDenoiseStrength = denoiseStrength,
                previewColorPolishApplied = false,
                previewColorPolish = CreateVideoColorMetadata(colorSettings),
                recordingPipeline = textureResult.RecordingPipeline,
                recordingDenoiseApplied = textureResult.RecordingDenoiseApplied,
                recordingMatchesPreviewDenoise = textureResult.RecordingMatchesPreviewDenoise,
                recordingColorPolishApplied = false,
                recordingMatchesPreviewColor = !colorSettings.HasVisibleAdjustments,
                note = colorSettings.HasVisibleAdjustments
                    ? "Color polish is CPU-only in this build; the saved texture-native video is raw color output."
                    : textureResult.RecordingPipeline.Contains("processed", StringComparison.OrdinalIgnoreCase)
                        ? "Texture-native recording matched the preview denoise setting through the processed bridge."
                        : denoiseEnabled
                            ? "DX12 denoise was visible in preview only; the saved texture-native video is raw camera output."
                            : "DX12 preview denoise was off; the saved texture-native video is raw camera output."
            };
        }

        return new
        {
            previewPipeline = FormatCpuCameraPreviewPipeline(isDirectShow, isDirect3D12Ready),
            previewDenoiseApplied = denoiseEnabled,
            previewDenoiseSliderStrength = denoiseSliderStrength,
            previewDenoiseStrength = denoiseStrength,
            previewColorPolishApplied = colorSettings.HasVisibleAdjustments,
            previewColorPolish = CreateVideoColorMetadata(colorSettings),
            recordingPipeline = isDirectShow ? "DirectShow CPU frames to Media Foundation MP4 writer" : "Media Foundation CPU frame writer",
            recordingDenoiseApplied = denoiseEnabled,
            recordingMatchesPreviewDenoise = true,
            recordingColorPolishApplied = colorSettings.HasVisibleAdjustments,
            recordingMatchesPreviewColor = true,
            note = denoiseEnabled && colorSettings.HasVisibleAdjustments
                ? "Preview denoise and color polish were applied before recording frames were written."
                : denoiseEnabled
                    ? "Preview denoise was applied before recording frames were written."
                    : colorSettings.HasVisibleAdjustments
                        ? "Color polish was applied before recording frames were written."
                        : "Preview denoise was off."
        };
    }

    internal static object CreateVideoColorMetadata(VideoFrameColorSettings settings)
    {
        return new
        {
            settings.Enabled,
            settings.Exposure,
            settings.Contrast,
            settings.Saturation,
            settings.Warmth
        };
    }

    internal static string FormatTextureNativeCameraStatus(
        Dx12Camera? activeCamera,
        string state,
        CameraDevice camera,
        TextureNativeFrameInfo frame,
        bool denoiseEnabled,
        double denoiseStrength,
        bool recordProcessedTextureOutput)
    {
        var textureStatus = activeCamera?.TextureFrameLeaseActive == true ? "texture lease active" : "waiting for texture lease";
        var presenterStatus = activeCamera?.IsReady == true ? "DX12 presenter active" : "DX12 presenter pending";
        var previewPathStatus = activeCamera?.PreviewPathDescription ?? "DX12 preview path pending";
        var denoiseStatus = denoiseEnabled
            ? $"DX12 denoise {denoiseStrength:0.0}"
            : "DX12 denoise off";
        var recordingStatus = recordProcessedTextureOutput ? "recording follows denoise" : "raw recording";
        return $"{state}: {camera.Name} at {frame.Width}x{frame.Height} {frame.FramesPerSecond:0.#} fps {frame.MediaSubtype} ({frame.DeviceMode}, {textureStatus}, {presenterStatus}, {previewPathStatus}, {denoiseStatus}, {recordingStatus}, frame {frame.FrameNumber})";
    }

    internal static string FormatCpuCameraPreviewPipeline(bool isDirectShow, bool isDirect3D12Ready)
    {
        var capturePath = isDirectShow ? "DirectShow RGB32 CPU frames" : "Media Foundation NV12/BGRA CPU frames";
        var presentationPath = isDirect3D12Ready
            ? isDirectShow ? "DX12 BGRA presentation" : "DX12 NV12/BGRA presentation"
            : "WPF BGRA presentation";
        return $"{capturePath} -> {presentationPath}";
    }

    internal static string FormatPreviewRecordParity(
        bool isCameraEnabled,
        Dx12Camera? activeCamera,
        bool denoiseEnabled,
        bool hasVisibleColorAdjustments,
        out bool isGood)
    {
        if (!isCameraEnabled)
        {
            isGood = true;
            return "camera idle";
        }

        if (activeCamera?.IsTextureNative == true)
        {
            if (hasVisibleColorAdjustments)
            {
                isGood = false;
                return "DX12 texture preview color is raw";
            }

            isGood = true;
            return VideoRecordingPolicy.ShouldRecordProcessedTextureOutput(denoiseEnabled)
                ? "recording includes grain reduction"
                : "preview = raw texture recording";
        }

        isGood = true;
        if (denoiseEnabled && hasVisibleColorAdjustments)
        {
            return "preview matches recording with denoise + color";
        }

        if (denoiseEnabled)
        {
            return "preview matches recording with denoise";
        }

        return hasVisibleColorAdjustments
            ? "preview matches recording with color"
            : "preview matches recording";
    }

    internal static string FormatActiveVideoPipeline(
        bool isCameraAvailable,
        bool isCameraEnabled,
        Dx12Camera? activeCamera,
        bool denoiseEnabled,
        bool hasVisibleColorAdjustments,
        bool isSelectedDirectShowCamera,
        bool isDirect3D12Ready,
        string? lastTextureNativeCameraError)
    {
        if (!isCameraAvailable)
        {
            return "no camera";
        }

        if (!isCameraEnabled)
        {
            return "camera disabled";
        }

        if (activeCamera?.IsTextureNative == true)
        {
            var previewPath = activeCamera.PreviewPathDescription;
            var colorStatus = hasVisibleColorAdjustments ? "; texture preview color raw" : string.Empty;
            return $"{previewPath}; recording {(VideoRecordingPolicy.ShouldRecordProcessedTextureOutput(denoiseEnabled) ? "grain reduction bridge" : "raw texture-native")}{colorStatus}";
        }

        if (isSelectedDirectShowCamera)
        {
            return hasVisibleColorAdjustments
                ? $"{FormatCpuCameraPreviewPipeline(isDirectShow: true, isDirect3D12Ready)} + color; Media Foundation MP4 writer"
                : $"{FormatCpuCameraPreviewPipeline(isDirectShow: true, isDirect3D12Ready)}; Media Foundation MP4 writer";
        }

        var fallback = string.IsNullOrWhiteSpace(lastTextureNativeCameraError)
            ? string.Empty
            : $" after DX12 shared stream fallback ({lastTextureNativeCameraError})";
        var color = hasVisibleColorAdjustments ? " + color" : string.Empty;
        return $"{FormatCpuCameraPreviewPipeline(isDirectShow: false, isDirect3D12Ready)}{color}; Media Foundation MP4 writer{fallback}";
    }
}
