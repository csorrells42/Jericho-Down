using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PodcastWorkbench.Video;

public sealed class Dx12Camera : IDisposable
{
    private static readonly object ActiveLock = new();
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(2);
    private static readonly Dictionary<string, string> TextureNativePreviewFailures = new(StringComparer.OrdinalIgnoreCase);
    private static Dx12Camera? _active;

    private readonly object _stateLock = new();
    private readonly CameraDevice _camera;
    private readonly CameraVideoMode _mode;
    private readonly PreviewTarget _target;
    private readonly Dispatcher _dispatcher;
    private readonly Dx12CameraKind _kind;
    private readonly Action? _fallbackStop;
    private readonly string _fallbackDescription;
    private TextureNativeCameraStream? _stream;
    private Direct3D12PreviewHost? _previewHost;
    private TextureNativeFrameLease? _pendingPreviewFrame;
    private WriteableBitmap? _fallbackPreviewBitmap;
    private int _previewRenderQueued;
    private long _fallbackFrameNumber;
    private bool _textureFrameLeaseActive;
    private bool _denoiseEnabled;
    private double _denoiseStrength = 2d;
    private bool _disposed;

    public Dx12Camera(Panel previewWindow)
        : this(RequireDefaultCamera(), CameraVideoMode.Auto, new PreviewTarget(previewWindow))
    {
    }

    public Dx12Camera(Panel previewWindow, CameraVideoMode? mode)
        : this(RequireDefaultCamera(), mode, new PreviewTarget(previewWindow))
    {
    }

    public Dx12Camera(CameraDevice camera, Panel previewWindow)
        : this(camera, CameraVideoMode.Auto, new PreviewTarget(previewWindow))
    {
    }

    public Dx12Camera(CameraDevice camera, CameraVideoMode? mode, Panel previewWindow)
        : this(camera, mode, new PreviewTarget(previewWindow))
    {
    }

    public Dx12Camera(CameraDevice camera, PreviewTarget target)
        : this(camera, CameraVideoMode.Auto, target)
    {
    }

    public Dx12Camera(CameraDevice camera, CameraVideoMode? mode, PreviewTarget target)
    {
        if (!target.PreviewWindow.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Dx12Camera must be constructed on the preview window's UI thread.");
        }

        _camera = camera;
        _mode = mode ?? CameraVideoMode.Auto;
        _target = target;
        _dispatcher = target.PreviewWindow.Dispatcher;
        _kind = Dx12CameraKind.TextureNative;
        _fallbackDescription = string.Empty;
        lock (ActiveLock)
        {
            _active?.Dispose();
            _active = this;
        }

        try
        {
            Initialize();
        }
        catch
        {
            lock (ActiveLock)
            {
                if (ReferenceEquals(_active, this))
                {
                    _active = null;
                }
            }

            Dispose();
            throw;
        }
    }

    private Dx12Camera(
        CameraDevice camera,
        CameraVideoMode mode,
        PreviewTarget target,
        Dx12CameraKind kind,
        string fallbackDescription,
        Action fallbackStop)
    {
        _camera = camera;
        _mode = mode;
        _target = target;
        _dispatcher = target.PreviewWindow.Dispatcher;
        _kind = kind;
        _fallbackDescription = fallbackDescription;
        _fallbackStop = fallbackStop;
        InitializeFallbackPreview();
    }

    ~Dx12Camera()
    {
        Dispose(disposing: false);
    }

    public event EventHandler<TextureNativeFrameInfo>? FrameAvailable;
    public event EventHandler<TextureNativeFrameLease>? TextureFrameAvailable;
    public event EventHandler<string>? StatusChanged;

    public static IReadOnlyList<CameraDevice> GetCameras()
    {
        return CameraDeviceCatalog.MergeDevices(
            MediaFoundationCameraEnumerator.GetVideoInputDevices(),
            DirectShowCameraEnumerator.GetVideoInputDevices());
    }

    public static CameraDevice? GetDefaultCamera()
    {
        return GetCameras().FirstOrDefault();
    }

    public static CameraDevice RequireDefaultCamera()
    {
        return GetDefaultCamera()
            ?? throw new InvalidOperationException("No camera devices were found.");
    }

    public static CameraDevice? FindCamera(
        IReadOnlyList<CameraDevice> cameras,
        string? devicePath,
        string? source,
        string? name)
    {
        if (!string.IsNullOrWhiteSpace(devicePath))
        {
            var pathMatch = cameras.FirstOrDefault(camera =>
                camera.EnumerateSourceDevices().Any(sourceDevice =>
                    sourceDevice.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase)
                    && sourceDevice.Source.Equals(source ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
            if (pathMatch is not null)
            {
                return pathMatch;
            }
        }

        return string.IsNullOrWhiteSpace(name)
            ? null
            : cameras.FirstOrDefault(camera =>
                camera.EnumerateSourceDevices().Any(sourceDevice =>
                    sourceDevice.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && sourceDevice.Source.Equals(source ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                ?? cameras.FirstOrDefault(camera =>
                    camera.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDirectShowCamera(CameraDevice camera)
    {
        return string.Equals(camera.Source, "DirectShow", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSelectedDirectShowCamera(bool isDirectShowPreviewActive, CameraDevice? selectedCamera)
    {
        return isDirectShowPreviewActive
            || selectedCamera is not null && IsDirectShowCamera(selectedCamera);
    }

    public static bool IsCpuCameraPreviewOwningCamera(
        bool isCameraEnabled,
        Dx12Camera? activeCamera,
        bool mediaFoundationPreviewIsRunning,
        bool directShowPreviewIsRunning,
        bool isDirectShowPreviewActive)
    {
        return isCameraEnabled
            && activeCamera?.IsTextureNative != true
            && (mediaFoundationPreviewIsRunning || directShowPreviewIsRunning || isDirectShowPreviewActive);
    }

    public static string FormatCameraMode(CameraVideoMode mode)
    {
        return mode.IsAuto ? "Auto mode" : mode.Label;
    }

    public static CameraVideoMode ResolveSelectedCameraMode(object? selectedItem)
    {
        return selectedItem as CameraVideoMode ?? CameraVideoMode.Auto;
    }

    public static string FormatCameraDisabledStatus(CameraVideoMode mode)
    {
        return $"Camera disabled - selected mode: {FormatCameraMode(mode)}";
    }

    public static string FormatCameraStatus(string state, CameraDevice camera, CameraVideoMode mode)
    {
        return $"{state}: {camera.Name} at {FormatCameraMode(mode)}";
    }

    public static string FormatDirectShowCameraSelectedStatus(CameraDevice camera)
    {
        return $"DirectShow camera selected: {camera.Name} - Auto uses the camera's current output";
    }

    public static string FormatLoadingCameraModesStatus(CameraDevice camera, CameraVideoMode selectedMode)
    {
        return $"Loading modes: {camera.Name} - selected mode: {FormatCameraMode(selectedMode)}";
    }

    public static string FormatCameraIdleStatus(bool cameraAvailable, CameraVideoMode mode)
    {
        return cameraAvailable
            ? FormatCameraDisabledStatus(mode)
            : "No camera source found";
    }

    public static string FormatCameraPreviewStatus(string status, CameraDevice? camera, CameraVideoMode mode)
    {
        return camera is null
            ? status
            : $"{FormatCameraStatus("Preview", camera, mode)} - {status}";
    }

    public static string FormatChooseCameraControlsStatus()
    {
        return "Choose a camera to load controls.";
    }

    public static string FormatNoCameraControlsStatus()
    {
        return "No standard Windows camera controls were exposed by this source.";
    }

    public static string FormatCameraControlsLoadedStatus(CameraDevice camera, int controlCount)
    {
        return $"{controlCount} Windows camera controls exposed by {camera.Name}.";
    }

    public static string FormatCameraControlSetStatus(CameraControlItem control, int value, bool isAuto, bool success)
    {
        return success
            ? $"{control.Name}: {(isAuto ? "Auto" : FormatCameraControlValue(value))}"
            : $"Could not set {control.Name}. The camera may be busy or this control may be locked by its driver.";
    }

    public static string FormatVideoDenoiseStatus(bool isTextureNative, bool denoiseEnabled)
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

    public static string FormatVideoColorPolishStatus(bool isTextureNative, VideoFrameColorSettings settings)
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

    public static object BuildVideoProcessingMetadata(
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

    public static object CreateVideoColorMetadata(VideoFrameColorSettings settings)
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

    public static CameraProfile CaptureCameraProfile(
        string name,
        CameraDevice? camera,
        CameraVideoMode mode,
        bool cameraEnabled,
        bool denoiseEnabled,
        double denoiseStrength,
        bool colorPolishEnabled,
        VideoFrameColorSettings colorSettings)
    {
        return new CameraProfile
        {
            Name = name,
            CameraName = camera?.Name,
            CameraSource = camera?.Source,
            CameraDevicePath = camera?.DevicePath,
            CameraEnabled = cameraEnabled,
            ModeLabel = mode.Label,
            ModeWidth = mode.Width,
            ModeHeight = mode.Height,
            ModeFramesPerSecond = mode.FramesPerSecond,
            ModeInputFormat = mode.InputFormat,
            DenoiseEnabled = denoiseEnabled,
            DenoiseStrength = denoiseStrength,
            ColorPolishEnabled = colorPolishEnabled,
            Exposure = colorSettings.Exposure,
            Contrast = colorSettings.Contrast,
            Saturation = colorSettings.Saturation,
            Warmth = colorSettings.Warmth
        };
    }

    public static void SaveCameraProfile(
        string profileFolder,
        string name,
        CameraProfile profile,
        JsonSerializerOptions jsonOptions)
    {
        Directory.CreateDirectory(profileFolder);
        var json = JsonSerializer.Serialize(profile, jsonOptions);
        File.WriteAllText(GetCameraProfilePath(profileFolder, name), json);
    }

    public static CameraProfile? LoadCameraProfile(
        string profileFolder,
        string name,
        JsonSerializerOptions jsonOptions)
    {
        var path = GetCameraProfilePath(profileFolder, name);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CameraProfile>(json, jsonOptions);
    }

    public static CameraDevice? FindCameraForProfile(IEnumerable<CameraDevice> cameras, CameraProfile profile)
    {
        return FindCamera(
            cameras.ToList(),
            profile.CameraDevicePath,
            profile.CameraSource,
            profile.CameraName);
    }

    public static string GetCameraProfilePath(string profileFolder, string name)
    {
        return Path.Combine(profileFolder, $"{SanitizeCameraProfileFileName(name)}.json");
    }

    public static string ReadCameraProfileName(string path, JsonSerializerOptions jsonOptions)
    {
        try
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<CameraProfile>(json, jsonOptions);
            if (!string.IsNullOrWhiteSpace(profile?.Name))
            {
                return profile.Name.Trim();
            }
        }
        catch
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static string SanitizeCameraProfileFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());

        sanitized = sanitized.Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "CameraProfile";
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80].Trim();
    }

    public static bool IsKaraokeTabSelected(TabControl? tabControl)
    {
        return IsTabSelected(tabControl, "Karaoke");
    }

    public static bool IsPodcastTabSelected(TabControl? tabControl)
    {
        return IsTabSelected(tabControl, "Podcast");
    }

    public static string FormatTextureNativeCameraStatus(
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

    public static string FormatCpuCameraPreviewPipeline(bool isDirectShow, bool isDirect3D12Ready)
    {
        var capturePath = isDirectShow ? "DirectShow RGB32 CPU frames" : "Media Foundation NV12/BGRA CPU frames";
        var presentationPath = isDirect3D12Ready
            ? isDirectShow ? "DX12 BGRA presentation" : "DX12 NV12/BGRA presentation"
            : "WPF BGRA presentation";
        return $"{capturePath} -> {presentationPath}";
    }

    public static bool ShouldRecordProcessedTextureOutput(bool denoiseEnabled)
    {
        return denoiseEnabled;
    }

    public static string FormatPreviewRecordParity(
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
            return ShouldRecordProcessedTextureOutput(denoiseEnabled)
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

    public static string FormatActiveVideoPipeline(
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
            return $"{previewPath}; recording {(ShouldRecordProcessedTextureOutput(denoiseEnabled) ? "grain reduction bridge" : "raw texture-native")}{colorStatus}";
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

    public static int RoundCameraControlToStep(double value, CameraControlItem control)
    {
        var step = Math.Max(1, control.Step);
        var rounded = control.Minimum + (int)Math.Round((value - control.Minimum) / step) * step;
        return Math.Clamp(rounded, control.Minimum, control.Maximum);
    }

    public static int ApplyCameraControlDefaultMagnet(int value, CameraControlItem control)
    {
        var snapDistance = Math.Max(control.Step * 1.25d, (control.Maximum - control.Minimum) * 0.025d);
        return Math.Abs(value - control.DefaultValue) <= snapDistance
            ? control.DefaultValue
            : value;
    }

    public static int GetCameraControlNudgeStep(CameraControlItem control)
    {
        return Math.Max(1, control.Step);
    }

    public static bool UsesCameraControlNudgeButtons(CameraControlItem control)
    {
        return control.Kind == CameraControlKind.Camera
            && (control.PropertyId == 0 || control.PropertyId == 1 || control.PropertyId == 2);
    }

    public static string FormatCameraControlValue(int value)
    {
        return value.ToString("0");
    }

    public static bool ShouldUseTextureNativeRecording()
    {
        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_TEXTURE_NATIVE_RECORDING");
        return IsEnabledEnvironmentValue(value);
    }

    public static bool ShouldUseSharedTextureCameraStream(bool safeStartDx12Disabled)
    {
        if (safeStartDx12Disabled)
        {
            return false;
        }

        var value = Environment.GetEnvironmentVariable("PODCAST_WORKBENCH_SHARED_TEXTURE_CAMERA");
        return string.Equals(value, "force", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "preview", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnabledEnvironmentValue(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetTextureNativePreviewFailure(
        CameraDevice camera,
        CameraVideoMode mode,
        out string reason)
    {
        return TextureNativePreviewFailures.TryGetValue(CreateTextureNativePreviewFailureKey(camera, mode), out reason!);
    }

    public static void RememberTextureNativePreviewFailure(
        CameraDevice camera,
        CameraVideoMode mode,
        string reason)
    {
        TextureNativePreviewFailures[CreateTextureNativePreviewFailureKey(camera, mode)] = string.IsNullOrWhiteSpace(reason)
            ? "previous shared texture preview attempt failed"
            : reason;
    }

    public static void ForgetTextureNativePreviewFailure(CameraDevice camera, CameraVideoMode mode)
    {
        TextureNativePreviewFailures.Remove(CreateTextureNativePreviewFailureKey(camera, mode));
    }

    private static string CreateTextureNativePreviewFailureKey(CameraDevice camera, CameraVideoMode mode)
    {
        var cameraKey = string.IsNullOrWhiteSpace(camera.DevicePath)
            ? $"{camera.Source}|{camera.Name}"
            : $"{camera.Source}|{camera.DevicePath}";
        var modeKey = mode.IsAuto
            ? "auto"
            : $"{mode.Width}x{mode.Height}@{mode.FramesPerSecond:0.###}";
        return $"{cameraKey}|{modeKey}";
    }

    private static bool IsTabSelected(TabControl? tabControl, string header)
    {
        return tabControl?.SelectedItem is TabItem { Header: string selectedHeader }
            && selectedHeader.Equals(header, StringComparison.OrdinalIgnoreCase);
    }

    public static Dx12Camera GetOrCreate(CameraDevice camera, CameraVideoMode mode, PreviewTarget target)
    {
        if (!target.PreviewWindow.Dispatcher.CheckAccess())
        {
            return target.PreviewWindow.Dispatcher.Invoke(() => GetOrCreate(camera, mode, target));
        }

        lock (ActiveLock)
        {
            if (_active is not null
                && !_active._disposed
                && _active._kind == Dx12CameraKind.TextureNative
                && _active.IsInitializedFor(target.PreviewWindow)
                && _active.Matches(camera, mode))
            {
                return _active;
            }

            _active?.Dispose();
            _active = new Dx12Camera(camera, mode, target);
            return _active;
        }
    }

    public static Dx12Camera StartDefault(Panel previewWindow)
    {
        return StartDefault(new PreviewTarget(previewWindow));
    }

    public static Dx12Camera StartDefault(Panel previewWindow, CameraVideoMode? mode)
    {
        return StartDefault(new PreviewTarget(previewWindow), mode);
    }

    public static Dx12Camera StartDefault(
        PreviewTarget target,
        CameraVideoMode? mode = null,
        bool denoiseEnabled = false,
        double denoiseStrength = 2d)
    {
        return OpenTextureNative(
            RequireDefaultCamera(),
            mode ?? CameraVideoMode.Auto,
            target,
            denoiseEnabled,
            denoiseStrength);
    }

    public static Dx12Camera Start(Panel previewWindow)
    {
        return StartDefault(previewWindow);
    }

    public static Dx12Camera Start(CameraDevice camera, Panel previewWindow)
    {
        return OpenTextureNative(
            camera,
            CameraVideoMode.Auto,
            new PreviewTarget(previewWindow),
            denoiseEnabled: false,
            denoiseStrength: 2d);
    }

    public static Dx12Camera ClaimFallback(
        CameraDevice camera,
        CameraVideoMode mode,
        PreviewTarget target,
        string fallbackDescription,
        Action fallbackStop)
    {
        if (!target.PreviewWindow.Dispatcher.CheckAccess())
        {
            return target.PreviewWindow.Dispatcher.Invoke(() => ClaimFallback(camera, mode, target, fallbackDescription, fallbackStop));
        }

        var kind = fallbackDescription.Contains("DirectShow", StringComparison.OrdinalIgnoreCase)
            ? Dx12CameraKind.DirectShowFallback
            : Dx12CameraKind.MediaFoundationFallback;

        lock (ActiveLock)
        {
            if (_active is not null
                && !_active._disposed
                && _active._kind == kind
                && _active.IsInitializedFor(target.PreviewWindow)
                && _active.Matches(camera, mode))
            {
                return _active;
            }

            _active?.Dispose();
            _active = new Dx12Camera(camera, mode, target, kind, fallbackDescription, fallbackStop);
            return _active;
        }
    }

    public static Dx12Camera OpenFallback(
        CameraDevice camera,
        CameraVideoMode mode,
        PreviewTarget target,
        string fallbackDescription,
        Action fallbackStop)
    {
        return ClaimFallback(camera, mode, target, fallbackDescription, fallbackStop);
    }

    public static Dx12Camera OpenTextureNative(
        CameraDevice camera,
        CameraVideoMode mode,
        PreviewTarget target,
        bool denoiseEnabled,
        double denoiseStrength)
    {
        if (!target.PreviewWindow.Dispatcher.CheckAccess())
        {
            return target.PreviewWindow.Dispatcher.Invoke(
                () => OpenTextureNative(camera, mode, target, denoiseEnabled, denoiseStrength));
        }

        var dx12Camera = new Dx12Camera(camera, mode, target);
        dx12Camera.Denoise(denoiseEnabled, denoiseStrength);
        return dx12Camera;
    }

    public static void DestroyActive(bool collectGarbage = false)
    {
        lock (ActiveLock)
        {
            _active?.Dispose();
            _active = null;
        }

        if (collectGarbage)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    public static void CloseActive(bool collectGarbage = true)
    {
        DestroyActive(collectGarbage);
    }

    public static TextureNativeRecordingResult? CloseActiveCamera(
        Dx12Camera? camera,
        Action<Dx12Camera>? detach = null,
        bool collectGarbage = true)
    {
        if (camera is null)
        {
            return null;
        }

        detach?.Invoke(camera);

        TextureNativeRecordingResult? result = null;
        try
        {
            result = camera.StopRecordingIfActive();
        }
        finally
        {
            camera.Close(collectGarbage);
        }

        return result;
    }

    public bool IsInitializedFor(Panel previewWindow)
    {
        return ReferenceEquals(_target.PreviewWindow, previewWindow);
    }

    public CameraDevice Camera => _camera;

    public CameraVideoMode Mode => _mode;

    public string Name => _camera.Name;

    public bool IsRecording => _stream?.IsRecording == true;

    public bool IsTextureNative => _kind == Dx12CameraKind.TextureNative;

    public bool IsFallback => _kind is Dx12CameraKind.MediaFoundationFallback or Dx12CameraKind.DirectShowFallback;

    public PipelineKind Pipeline => _kind switch
    {
        Dx12CameraKind.TextureNative => PipelineKind.TextureNative,
        Dx12CameraKind.DirectShowFallback => PipelineKind.DirectShowFallback,
        _ => PipelineKind.MediaFoundationFallback
    };

    public int Width => _stream?.Width ?? 0;

    public int Height => _stream?.Height ?? 0;

    public double FramesPerSecond => _stream?.FramesPerSecond ?? 0d;

    public string DeviceMode => _stream?.DeviceMode ?? "none";

    public string MediaSubtype => _stream?.MediaSubtype ?? "none";

    public int SamplesWritten => _stream?.SamplesWritten ?? 0;

    public bool IsReady => _previewHost?.IsReady == true;

    public static bool IsPreviewRendererReady(Dx12Camera? camera)
    {
        return camera?.IsReady == true;
    }

    public static void ConfigureMediaFoundationPreview(
        MediaFoundationCameraPreviewService service,
        bool denoiseEnabled,
        double denoiseStrength,
        bool denoiseHandledByPreviewRenderer,
        VideoFrameColorSettings colorSettings)
    {
        service.DenoiseEnabled = denoiseEnabled;
        service.DenoiseStrength = denoiseStrength;
        service.DenoiseHandledByPreviewRenderer = denoiseHandledByPreviewRenderer;
        service.ColorSettings = colorSettings;
    }

    public static void ConfigureDirectShowPreview(
        DirectShowCameraPreviewService service,
        bool denoiseEnabled,
        double denoiseStrength,
        VideoFrameColorSettings colorSettings)
    {
        service.DenoiseEnabled = denoiseEnabled;
        service.DenoiseStrength = denoiseStrength;
        service.ColorSettings = colorSettings;
    }

    public static void ConfigureCpuPreviewServices(
        MediaFoundationCameraPreviewService mediaFoundationPreview,
        DirectShowCameraPreviewService directShowPreview,
        Dx12Camera? camera,
        bool denoiseEnabled,
        double denoiseStrength,
        VideoFrameColorSettings colorSettings)
    {
        ConfigureMediaFoundationPreview(
            mediaFoundationPreview,
            denoiseEnabled,
            denoiseStrength,
            IsPreviewRendererReady(camera),
            colorSettings);
        ConfigureDirectShowPreview(
            directShowPreview,
            denoiseEnabled,
            denoiseStrength,
            colorSettings);
    }

    public static bool TryStartMediaFoundationPreview(
        MediaFoundationCameraPreviewService service,
        CameraDevice camera,
        CameraVideoMode mode,
        Dx12Camera? activeCamera,
        bool denoiseEnabled,
        double denoiseStrength,
        VideoFrameColorSettings colorSettings)
    {
        ConfigureMediaFoundationPreview(
            service,
            denoiseEnabled,
            denoiseStrength,
            IsPreviewRendererReady(activeCamera),
            colorSettings);
        return service.Start(camera, mode);
    }

    public static bool TryStartDirectShowPreview(
        MediaFoundationCameraPreviewService mediaFoundationPreview,
        DirectShowCameraPreviewService directShowPreview,
        CameraDevice directShowCamera,
        CameraVideoMode mode,
        bool denoiseEnabled,
        double denoiseStrength,
        VideoFrameColorSettings colorSettings)
    {
        mediaFoundationPreview.Stop();
        ConfigureDirectShowPreview(
            directShowPreview,
            denoiseEnabled,
            denoiseStrength,
            colorSettings);
        return directShowPreview.Start(directShowCamera, mode);
    }

    public static bool TryGetDirectShowFallbackCamera(CameraDevice primaryCamera, out CameraDevice? directShowFallback)
    {
        directShowFallback = primaryCamera.FallbackDevice;
        return directShowFallback is not null && IsDirectShowCamera(directShowFallback);
    }

    public static void StopPreviewServices(
        MediaFoundationCameraPreviewService mediaFoundationPreview,
        DirectShowCameraPreviewService directShowPreview)
    {
        TryStopPreviewService(mediaFoundationPreview.Stop);
        TryStopPreviewService(directShowPreview.Stop);
    }

    public static void CollectReleasedCamera()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void TryStopPreviewService(Action stop)
    {
        try
        {
            stop();
        }
        catch
        {
        }
    }

    public bool TextureFrameLeaseActive => _textureFrameLeaseActive;

    public string PreviewPathDescription => IsTextureNative
        ? _previewHost?.PreviewPathDescription ?? "DX12 preview path pending"
        : _fallbackDescription;

    public static void AttachToSlot(
        ref Dx12Camera? slot,
        Dx12Camera camera,
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged)
    {
        if (ReferenceEquals(slot, camera))
        {
            return;
        }

        DetachFromSlot(ref slot, frameAvailable, statusChanged);
        slot = camera;
        camera.AttachPreviewHandlers(frameAvailable, statusChanged);
    }

    public static bool TryOpenTextureNativeIntoSlot(
        ref Dx12Camera? slot,
        CameraDevice camera,
        CameraVideoMode mode,
        PreviewTarget target,
        bool denoiseEnabled,
        double denoiseStrength,
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged)
    {
        if (slot is not null)
        {
            return false;
        }

        var dx12Camera = OpenTextureNative(camera, mode, target, denoiseEnabled, denoiseStrength);
        AttachToSlot(ref slot, dx12Camera, frameAvailable, statusChanged);
        return true;
    }

    public static bool TryOpenFallbackIntoSlot(
        ref Dx12Camera? slot,
        CameraDevice camera,
        CameraVideoMode mode,
        PreviewTarget target,
        string fallbackDescription,
        Action fallbackStop,
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged)
    {
        if (slot is not null)
        {
            return false;
        }

        var owner = OpenFallback(camera, mode, target, fallbackDescription, fallbackStop);
        AttachToSlot(ref slot, owner, frameAvailable, statusChanged);
        return true;
    }

    public static void DetachFromSlot(
        ref Dx12Camera? slot,
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged)
    {
        var camera = slot;
        if (camera is null)
        {
            return;
        }

        camera.DetachPreviewHandlers(frameAvailable, statusChanged);
        slot = null;
    }

    public static TextureNativeRecordingResult? CloseSlot(
        ref Dx12Camera? slot,
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged,
        bool collectGarbage = true)
    {
        var camera = slot;
        if (camera is null)
        {
            return null;
        }

        DetachFromSlot(ref slot, frameAvailable, statusChanged);

        TextureNativeRecordingResult? result = null;
        try
        {
            result = camera.StopRecordingIfActive();
        }
        finally
        {
            camera.Close(collectGarbage);
        }

        return result;
    }

    public void AttachPreviewHandlers(
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged)
    {
        FrameAvailable += frameAvailable;
        StatusChanged += statusChanged;
    }

    public void DetachPreviewHandlers(
        EventHandler<TextureNativeFrameInfo> frameAvailable,
        EventHandler<string> statusChanged)
    {
        FrameAvailable -= frameAvailable;
        StatusChanged -= statusChanged;
    }

    public void Denoise(int magnitude)
    {
        Denoise(magnitude > 0, magnitude);
    }

    public void Denoise(double strength)
    {
        Denoise(strength > 0d, strength);
    }

    public void Denoise(bool enabled, double strength)
    {
        _denoiseEnabled = enabled;
        _denoiseStrength = Math.Clamp(strength, 0.5d, 5d);
    }

    public void denoise(int magnitude)
    {
        Denoise(magnitude);
    }

    public void denoise(double strength)
    {
        Denoise(strength);
    }

    public void UpdateRenderSettings(bool denoiseEnabled, double denoiseStrength)
    {
        Denoise(denoiseEnabled, denoiseStrength);
    }

    public bool WriteMP4(string path)
    {
        return WriteMP4(
            path,
            processedOutputEnabled: _denoiseEnabled,
            denoiseEnabled: _denoiseEnabled,
            denoiseStrength: _denoiseStrength);
    }

    public bool WriteMP4(
        string path,
        bool processedOutputEnabled,
        bool denoiseEnabled,
        double denoiseStrength)
    {
        return WriteMP4(
            path,
            new TextureNativeRecordingOptions(
                processedOutputEnabled,
                denoiseEnabled,
                denoiseStrength));
    }

    public bool WriteMP4(string path, TextureNativeRecordingOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An MP4 file path is required.", nameof(path));
        }

        return StartRecording(path, options);
    }

    public bool writeMP4(string path)
    {
        return WriteMP4(path);
    }

    public bool writeMP4(
        string path,
        bool processedOutputEnabled,
        bool denoiseEnabled,
        double denoiseStrength)
    {
        return WriteMP4(path, processedOutputEnabled, denoiseEnabled, denoiseStrength);
    }

    public bool StartRecording(
        string path,
        bool processedOutputEnabled = false,
        bool denoiseEnabled = false,
        double denoiseStrength = 2d)
    {
        return StartRecording(
            path,
            new TextureNativeRecordingOptions(
                processedOutputEnabled,
                denoiseEnabled,
                denoiseStrength));
    }

    public bool StartRecording(string path, TextureNativeRecordingOptions options)
    {
        var stream = _stream ?? throw new InvalidOperationException("DX12 camera stream is not initialized.");
        return stream.StartRecording(path, options);
    }

    public void PauseRecording()
    {
        _stream?.PauseRecording();
    }

    public void PauseMP4()
    {
        PauseRecording();
    }

    public void ResumeRecording()
    {
        _stream?.ResumeRecording();
    }

    public void ResumeMP4()
    {
        ResumeRecording();
    }

    public TextureNativeRecordingResult? StopRecording()
    {
        return _stream?.StopRecording();
    }

    public TextureNativeRecordingResult? StopRecordingIfActive()
    {
        return IsRecording ? StopRecording() : null;
    }

    public TextureNativeRecordingResult? StopMP4()
    {
        return StopRecording();
    }

    public TextureNativeRecordingResult? stopMP4()
    {
        return StopMP4();
    }

    public void Close(bool collectGarbage = true)
    {
        Dispose();
        if (collectGarbage)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    public void Stop(bool collectGarbage = true)
    {
        Close(collectGarbage);
    }

    public void RenderProofFrame(TextureNativeFrameInfo frame)
    {
        _previewHost?.RenderProofFrame(frame);
    }

    public void RenderFallbackFrame(
        CameraFrame frame,
        VideoFrameColorSettings colorSettings = default,
        bool denoiseEnabled = false,
        double denoiseStrength = 0d)
    {
        if (_disposed || !IsFallback)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => RenderFallbackFrame(frame, colorSettings, denoiseEnabled, denoiseStrength), DispatcherPriority.Background);
            return;
        }

        _target.Placeholder?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        if (_previewHost?.IsReady == true)
        {
            _previewHost.RenderBgraFrame(
                frame,
                Interlocked.Increment(ref _fallbackFrameNumber),
                colorSettings,
                denoiseEnabled,
                denoiseStrength);
            _target.PreviewImage?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            return;
        }

        if (_target.PreviewImage is not Image image)
        {
            return;
        }

        var bgraBytes = frame.BgraBytes;
        var stride = frame.Stride;
        if (!frame.HasBgra)
        {
            if (!frame.HasNv12 || frame.Nv12Bytes is not { Length: > 0 } nv12Bytes)
            {
                return;
            }

            var converted = Nv12FrameConverter.ConvertToBgra(
                nv12Bytes,
                frame.Nv12Stride,
                frame.Width,
                frame.Height,
                out var convertedStride);
            if (converted is not { Length: > 0 } || convertedStride <= 0)
            {
                return;
            }

            bgraBytes = converted;
            stride = convertedStride;
        }

        if (_fallbackPreviewBitmap is null
            || _fallbackPreviewBitmap.PixelWidth != frame.Width
            || _fallbackPreviewBitmap.PixelHeight != frame.Height)
        {
            _fallbackPreviewBitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null);
            image.Source = _fallbackPreviewBitmap;
        }

        _fallbackPreviewBitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            bgraBytes,
            stride,
            0);
        image.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Initialize()
    {
        _target.PreviewImage?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _target.Placeholder?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
        _target.StatusText?.SetCurrentValue(TextBlock.TextProperty, $"DX12 camera starting for {_target.Name}.");

        var stream = new TextureNativeCameraStream(_camera, _mode, startImmediately: false);
        _stream = stream;
        stream.FrameAvailable += StreamFrameAvailable;
        stream.TextureFrameAvailable += StreamTextureFrameAvailable;
        stream.StatusChanged += StreamStatusChanged;

        stream.Start();
        ShowPreviewHost(stream.DuplicateNativeD3D12Device());
        if (!WaitForFirstFrame(stream))
        {
            throw new TimeoutException(
                $"No DX12 texture frames arrived within {FirstFrameTimeout.TotalSeconds:0.#} seconds ({stream.DeviceMode}, {stream.Width}x{stream.Height}@{stream.FramesPerSecond:0.###}, {stream.MediaSubtype}).");
        }
    }

    private void InitializeFallbackPreview()
    {
        _target.PreviewImage?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        _target.Placeholder?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
        _target.StatusText?.SetCurrentValue(TextBlock.TextProperty, $"{_fallbackDescription} starting for {_target.Name}.");
        ShowPreviewHost(IntPtr.Zero);
    }

    private static bool WaitForFirstFrame(TextureNativeCameraStream stream)
    {
        var deadline = DateTimeOffset.UtcNow + FirstFrameTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (stream.FramesRead > 0)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return stream.FramesRead > 0;
    }

    private bool Matches(CameraDevice camera, CameraVideoMode mode)
    {
        return string.Equals(_camera.Name, camera.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_camera.DevicePath, camera.DevicePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_camera.Source, camera.Source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_mode.Label, mode.Label, StringComparison.Ordinal)
            && _mode.Width == mode.Width
            && _mode.Height == mode.Height
            && Nullable.Equals(_mode.FramesPerSecond, mode.FramesPerSecond)
            && string.Equals(_mode.InputFormat, mode.InputFormat, StringComparison.OrdinalIgnoreCase)
            && _mode.IsAuto == mode.IsAuto;
    }

    private void ShowPreviewHost(IntPtr nativeD3D12Device)
    {
        try
        {
            var host = new Direct3D12PreviewHost(nativeD3D12Device)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            nativeD3D12Device = IntPtr.Zero;
            host.StatusChanged += PreviewHostStatusChanged;
            _previewHost = host;
            var insertIndex = Math.Min(_target.HostInsertIndex, _target.PreviewWindow.Children.Count);
            _target.PreviewWindow.Children.Insert(insertIndex, host);
            host.Visibility = Visibility.Visible;
        }
        finally
        {
            if (nativeD3D12Device != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.Release(nativeD3D12Device);
            }
        }
    }

    private void HidePreviewHost()
    {
        var host = _previewHost;
        if (host is null)
        {
            return;
        }

        _previewHost = null;
        host.StatusChanged -= PreviewHostStatusChanged;
        _target.PreviewWindow.Children.Remove(host);
        host.Dispose();
    }

    private void StreamFrameAvailable(object? sender, TextureNativeFrameInfo frame)
    {
        FrameAvailable?.Invoke(this, frame);
    }

    private void StreamTextureFrameAvailable(object? sender, TextureNativeFrameLease frame)
    {
        _textureFrameLeaseActive = frame.IsValid;
        TextureFrameAvailable?.Invoke(this, frame);

        var pendingFrame = frame.Duplicate();
        if (pendingFrame is null)
        {
            return;
        }

        Interlocked.Exchange(ref _pendingPreviewFrame, pendingFrame)?.Dispose();
        if (Interlocked.Exchange(ref _previewRenderQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke((Action)ProcessPendingPreviewFrame, DispatcherPriority.Background);
    }

    private void ProcessPendingPreviewFrame()
    {
        var frame = Interlocked.Exchange(ref _pendingPreviewFrame, null);
        if (frame is not null)
        {
            try
            {
                if (!_disposed)
                {
                    _previewHost?.RenderTextureFrame(frame, _denoiseEnabled, _denoiseStrength);
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        Volatile.Write(ref _previewRenderQueued, 0);
        if (Volatile.Read(ref _pendingPreviewFrame) is not null
            && Interlocked.Exchange(ref _previewRenderQueued, 1) == 0)
        {
            _dispatcher.BeginInvoke((Action)ProcessPendingPreviewFrame, DispatcherPriority.Background);
        }
    }

    private void StreamStatusChanged(object? sender, string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void PreviewHostStatusChanged(object? sender, string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void Dispose(bool disposing)
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        var stream = _stream;
        _stream = null;
        if (stream is not null)
        {
            stream.FrameAvailable -= StreamFrameAvailable;
            stream.TextureFrameAvailable -= StreamTextureFrameAvailable;
            stream.StatusChanged -= StreamStatusChanged;
        }

        try
        {
            stream?.Dispose();
        }
        catch
        {
        }

        if (disposing && _fallbackStop is not null)
        {
            try
            {
                _fallbackStop();
            }
            catch
            {
            }
        }

        Interlocked.Exchange(ref _pendingPreviewFrame, null)?.Dispose();
        _textureFrameLeaseActive = false;
        Volatile.Write(ref _previewRenderQueued, 0);

        if (disposing && _dispatcher.CheckAccess())
        {
            HidePreviewHost();
        }
        else if (disposing)
        {
            try
            {
                _dispatcher.Invoke(HidePreviewHost);
            }
            catch
            {
            }
        }

        if (disposing)
        {
            lock (ActiveLock)
            {
                if (ReferenceEquals(_active, this))
                {
                    _active = null;
                }
            }
        }
    }

    private enum Dx12CameraKind
    {
        TextureNative,
        MediaFoundationFallback,
        DirectShowFallback
    }

    public enum PipelineKind
    {
        TextureNative,
        MediaFoundationFallback,
        DirectShowFallback
    }

    public sealed class PreviewTarget
    {
        public PreviewTarget(
            Panel previewWindow,
            UIElement? previewImage = null,
            UIElement? placeholder = null,
            TextBlock? statusText = null,
            int hostInsertIndex = 0,
            string name = "Camera")
        {
            PreviewWindow = previewWindow;
            PreviewImage = previewImage;
            Placeholder = placeholder;
            StatusText = statusText;
            HostInsertIndex = Math.Max(0, hostInsertIndex);
            Name = name;
        }

        public Panel PreviewWindow { get; }

        public UIElement? PreviewImage { get; }

        public UIElement? Placeholder { get; }

        public TextBlock? StatusText { get; }

        public int HostInsertIndex { get; }

        public string Name { get; }
    }

    public sealed class CameraProfile
    {
        public int Version { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        public string? CameraName { get; set; }

        public string? CameraSource { get; set; }

        public string? CameraDevicePath { get; set; }

        public bool CameraEnabled { get; set; }

        public string ModeLabel { get; set; } = CameraVideoMode.Auto.Label;

        public int? ModeWidth { get; set; }

        public int? ModeHeight { get; set; }

        public double? ModeFramesPerSecond { get; set; }

        public string? ModeInputFormat { get; set; }

        public bool DenoiseEnabled { get; set; }

        public double DenoiseStrength { get; set; } = 2d;

        public bool ColorPolishEnabled { get; set; }

        public double Exposure { get; set; }

        public double Contrast { get; set; }

        public double Saturation { get; set; }

        public double Warmth { get; set; }
    }
}
