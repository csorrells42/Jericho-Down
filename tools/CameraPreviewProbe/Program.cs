using System.Globalization;
using JerichoDown.Modules.Webcam;
using JerichoDown.Video;
using System.Windows;
using System.Windows.Threading;

var options = ProbeOptions.Parse(args);
if (options.ShowHelp)
{
    ProbeOptions.PrintUsage();
    return 0;
}

var textureProbe = options.HasFlag("--texture");
var dx12PreviewProbe = options.HasFlag("--dx12-preview");
var mediaFoundationPreviewProbe = options.HasFlag("--mf-preview");
var mode = options.Mode ?? CameraVideoMode.Auto;
var requested = options.CameraName ?? string.Join(' ', options.SearchTerms);

var cameras = MediaFoundationCameraEnumerator.GetVideoInputDevices()
    .Concat(DirectShowCameraEnumerator.GetVideoInputDevices())
    .GroupBy(camera => string.IsNullOrWhiteSpace(camera.DevicePath)
        ? $"name:{camera.Name}|source:{camera.Source}"
        : $"path:{camera.DevicePath}", StringComparer.OrdinalIgnoreCase)
    .Select(group => group.First())
    .ToList();

Console.WriteLine("Detected cameras:");
foreach (var camera in cameras)
{
    Console.WriteLine($"- {camera.Name} [{camera.Source}] {camera.DevicePath}");
}

if (options.ListOnly)
{
    return 0;
}

var candidates = SelectSourceCandidates(cameras, options.Source, textureProbe, mediaFoundationPreviewProbe);
var target = candidates
    .OrderByDescending(camera => ShouldPreferVirtualCamera(options.Source, textureProbe, mediaFoundationPreviewProbe)
        ? GetVirtualCameraPriority(camera)
        : 0)
    .ThenBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase)
    .FirstOrDefault(camera => string.IsNullOrWhiteSpace(requested)
        ? textureProbe || mediaFoundationPreviewProbe || IsLikelyVirtualCamera(camera)
        : camera.Name.Contains(requested, StringComparison.OrdinalIgnoreCase))
    ?? candidates.FirstOrDefault();

if (target is null)
{
    Console.WriteLine(options.Source switch
    {
        CameraSourcePreference.MediaFoundation => "No Media Foundation camera found to probe.",
        CameraSourcePreference.DirectShow => "No DirectShow camera found to probe.",
        _ => "No camera found to probe."
    });
    return 2;
}

if (mediaFoundationPreviewProbe)
{
    return await RunDx12MediaFoundationPreviewProbeAsync(target, mode, options.Duration, options.Visible);
}

if (textureProbe)
{
    if (dx12PreviewProbe)
    {
        return await RunDx12TexturePreviewProbeAsync(target, mode, options.Duration, options.Visible);
    }

    Console.WriteLine($"Probing texture-native camera: {target.Name} / {mode.Label}");
    using var cancellation = new CancellationTokenSource(options.Duration);
    var result = await TextureNativeCameraProbe.RunAsync(target.Name, mode, options.RequestedSamples, cancellation.Token);
    Console.WriteLine(result);
    return result.D3D12ResourceSamples > 0 ? 0 : 5;
}

if (dx12PreviewProbe)
{
    return await RunDx12DirectShowPreviewProbeAsync(target, mode, options.Duration, options.Visible);
}

Console.WriteLine($"Probing DirectShow camera: {target.Name} / {mode.Label}");
using var service = new DirectShowCameraPreviewService();
var frames = 0;
var firstFrame = DateTimeOffset.MinValue;
CameraFrame? latestFrame = null;
service.StatusChanged += (_, status) => Console.WriteLine(status);
service.FrameAvailable += (_, frame) =>
{
    if (Interlocked.Increment(ref frames) == 1)
    {
        firstFrame = DateTimeOffset.Now;
    }

    latestFrame = frame;
};

if (!service.Start(target, mode))
{
    Console.WriteLine(service.LastStatus ?? "DirectShow probe failed before start.");
    return 3;
}

await Task.Delay(options.Duration);
service.Stop();

if (frames <= 0 || latestFrame is null)
{
    Console.WriteLine($"Probe failed: no DirectShow frames arrived within {options.Duration.TotalSeconds:0.#} seconds.");
    return 4;
}

Console.WriteLine(
    $"Probe succeeded: {frames} frames, latest {latestFrame.Width}x{latestFrame.Height}, stride {latestFrame.Stride}, first frame {firstFrame:HH:mm:ss.fff}.");
return 0;

static IEnumerable<CameraDevice> SelectSourceCandidates(
    IEnumerable<CameraDevice> cameras,
    CameraSourcePreference source,
    bool textureProbe,
    bool mediaFoundationPreviewProbe)
{
    return source switch
    {
        CameraSourcePreference.MediaFoundation => cameras.Where(IsMediaFoundationCamera),
        CameraSourcePreference.DirectShow => cameras.Where(IsDirectShowCamera),
        _ when textureProbe || mediaFoundationPreviewProbe => cameras.Where(IsMediaFoundationCamera),
        _ => cameras.Where(IsDirectShowCamera)
    };
}

static bool ShouldPreferVirtualCamera(CameraSourcePreference source, bool textureProbe, bool mediaFoundationPreviewProbe)
{
    return source is not CameraSourcePreference.MediaFoundation
        && !textureProbe
        && !mediaFoundationPreviewProbe;
}

static bool IsMediaFoundationCamera(CameraDevice camera)
{
    return camera.Source.Equals("Media Foundation", StringComparison.OrdinalIgnoreCase);
}

static bool IsDirectShowCamera(CameraDevice camera)
{
    return camera.Source.Equals("DirectShow", StringComparison.OrdinalIgnoreCase);
}

static bool IsLikelyVirtualCamera(CameraDevice camera)
{
    return GetVirtualCameraPriority(camera) > 0;
}

static int GetVirtualCameraPriority(CameraDevice camera)
{
    if (camera.Name.Contains("insta360 virtual", StringComparison.OrdinalIgnoreCase))
    {
        return 4;
    }

    if (camera.Name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
        || camera.Name.Contains("obs", StringComparison.OrdinalIgnoreCase)
        || camera.Name.Contains("broadcast", StringComparison.OrdinalIgnoreCase)
        || camera.DevicePath.StartsWith("@device:sw:", StringComparison.OrdinalIgnoreCase))
    {
        return 2;
    }

    return 0;
}

static Task<int> RunDx12DirectShowPreviewProbeAsync(CameraDevice camera, CameraVideoMode mode, TimeSpan duration, bool visible)
{
    var result = new TaskCompletionSource<int>();
    var thread = new Thread(() =>
    {
        DirectShowCameraPreviewService? service = null;
        Direct3D12PreviewHost? host = null;
        Window? window = null;
        var receivedFrames = 0;
        var renderedFrames = 0;
        var statuses = new List<string>();

        try
        {
            window = CreateProbeWindow("DX12 DirectShow Preview Probe", visible);
            host = new Direct3D12PreviewHost
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            host.StatusChanged += (_, status) =>
            {
                statuses.Add(status);
                Console.WriteLine(status);
            };
            window.Content = host;
            window.Show();

            service = new DirectShowCameraPreviewService();
            service.StatusChanged += (_, status) => Console.WriteLine(status);
            service.FrameAvailable += (_, frame) =>
            {
                Interlocked.Increment(ref receivedFrames);
                window.Dispatcher.BeginInvoke(() =>
                {
                    host.RenderBgraFrame(frame, Interlocked.Increment(ref renderedFrames));
                });
            };

            if (!service.Start(camera, mode))
            {
                Console.WriteLine(service.LastStatus ?? "DirectShow DX12 preview probe failed before start.");
                result.TrySetResult(6);
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                return;
            }

            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                service.Stop();
                window.Close();
                Console.WriteLine($"DX12 DirectShow preview probe: received {receivedFrames}, rendered {renderedFrames}, host ready {host.IsReady}, path {host.PreviewPathDescription}.");
                result.TrySetResult(receivedFrames > 0 && renderedFrames > 0 && host.IsReady ? 0 : 7);
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };
            timer.Start();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DX12 DirectShow preview probe failed: {ex}");
            result.TrySetResult(8);
        }
        finally
        {
            service?.Dispose();
            host?.Dispose();
            window?.Close();
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return result.Task;
}

static Task<int> RunDx12TexturePreviewProbeAsync(CameraDevice camera, CameraVideoMode mode, TimeSpan duration, bool visible)
{
    var result = new TaskCompletionSource<int>();
    var thread = new Thread(() =>
    {
        TextureNativeCameraStream? stream = null;
        Direct3D12PreviewHost? host = null;
        Window? window = null;
        var infoFrames = 0;
        var renderedFrames = 0;

        try
        {
            stream = new TextureNativeCameraStream(camera, mode, startImmediately: false);
            window = CreateProbeWindow("DX12 Texture Preview Probe", visible);

            stream.StatusChanged += (_, status) => Console.WriteLine(status);
            stream.FrameAvailable += (_, _) => Interlocked.Increment(ref infoFrames);
            stream.TextureFrameAvailable += (_, lease) =>
            {
                var targetHost = host;
                if (targetHost is null)
                {
                    return;
                }

                var pendingLease = lease.Duplicate();
                if (pendingLease is null)
                {
                    return;
                }

                window.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        targetHost.RenderTextureFrame(pendingLease, denoiseEnabled: false, denoiseStrength: 2d);
                        Interlocked.Increment(ref renderedFrames);
                    }
                    finally
                    {
                        pendingLease.Dispose();
                    }
                });
            };

            stream.Start();
            host = new Direct3D12PreviewHost(stream.DuplicateNativeD3D12Device())
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            host.StatusChanged += (_, status) => Console.WriteLine(status);
            window.Content = host;
            window.Show();
            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.Close();
                Console.WriteLine($"DX12 texture preview probe: info {infoFrames}, rendered {renderedFrames}, stream mode {stream.DeviceMode}, {stream.Width}x{stream.Height}@{stream.FramesPerSecond:0.###}, host ready {host.IsReady}, path {host.PreviewPathDescription}.");
                result.TrySetResult(infoFrames > 0 && renderedFrames > 0 && host.IsReady ? 0 : 9);
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };
            timer.Start();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DX12 texture preview probe failed: {ex}");
            result.TrySetResult(10);
        }
        finally
        {
            stream?.Dispose();
            host?.Dispose();
            window?.Close();
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return result.Task;
}

static Task<int> RunDx12MediaFoundationPreviewProbeAsync(CameraDevice camera, CameraVideoMode mode, TimeSpan duration, bool visible)
{
    var result = new TaskCompletionSource<int>();
    var thread = new Thread(() =>
    {
        MediaFoundationCameraPreviewService? service = null;
        Direct3D12PreviewHost? host = null;
        Window? window = null;
        var receivedFrames = 0;
        var renderedFrames = 0;

        try
        {
            window = CreateProbeWindow("DX12 Media Foundation Preview Probe", visible);
            host = new Direct3D12PreviewHost
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            host.StatusChanged += (_, status) => Console.WriteLine(status);
            window.Content = host;
            window.Show();

            service = new MediaFoundationCameraPreviewService();
            service.StatusChanged += (_, status) => Console.WriteLine(status);
            service.FrameAvailable += (_, frame) =>
            {
                Interlocked.Increment(ref receivedFrames);
                window.Dispatcher.BeginInvoke(() =>
                {
                    host.RenderBgraFrame(frame, Interlocked.Increment(ref renderedFrames));
                });
            };

            if (!service.Start(camera, mode))
            {
                Console.WriteLine("Media Foundation preview probe failed before start.");
                result.TrySetResult(11);
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                return;
            }

            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                service.Stop();
                window.Close();
                Console.WriteLine($"DX12 Media Foundation preview probe: received {receivedFrames}, rendered {renderedFrames}, host ready {host.IsReady}, path {host.PreviewPathDescription}.");
                result.TrySetResult(receivedFrames > 0 && renderedFrames > 0 && host.IsReady ? 0 : 12);
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };
            timer.Start();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DX12 Media Foundation preview probe failed: {ex}");
            result.TrySetResult(13);
        }
        finally
        {
            service?.Dispose();
            host?.Dispose();
            window?.Close();
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return result.Task;
}

static Window CreateProbeWindow(string title, bool visible)
{
    return new Window
    {
        Title = title,
        Width = 640,
        Height = 360,
        Left = visible ? 80 : -30000,
        Top = visible ? 80 : -30000,
        ShowInTaskbar = visible,
        WindowStyle = visible ? WindowStyle.SingleBorderWindow : WindowStyle.None
    };
}

internal sealed record ProbeOptions(
    HashSet<string> Flags,
    string? CameraName,
    IReadOnlyList<string> SearchTerms,
    CameraSourcePreference Source,
    CameraVideoMode? Mode,
    TimeSpan Duration,
    int RequestedSamples,
    bool Visible,
    bool ListOnly,
    bool ShowHelp)
{
    public bool HasFlag(string flag)
    {
        return Flags.Contains(flag);
    }

    public static ProbeOptions Parse(string[] args)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchTerms = new List<string>();
        string? cameraName = null;
        var source = CameraSourcePreference.Auto;
        CameraVideoMode? mode = null;
        var duration = TimeSpan.FromSeconds(6);
        var requestedSamples = 30;
        var visible = false;
        var listOnly = false;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                searchTerms.Add(arg);
                continue;
            }

            var (name, inlineValue) = SplitOption(arg);
            flags.Add(name);
            switch (name.ToLowerInvariant())
            {
                case "--camera":
                    cameraName = ReadOptionValue(args, ref i, inlineValue, name);
                    break;
                case "--source":
                    source = ParseSource(ReadOptionValue(args, ref i, inlineValue, name));
                    break;
                case "--mode":
                    mode = ParseMode(ReadOptionValue(args, ref i, inlineValue, name));
                    break;
                case "--4k24":
                    mode = new CameraVideoMode("3840x2160 @ 24 fps", 3840, 2160, 24d, null);
                    break;
                case "--seconds":
                    duration = TimeSpan.FromSeconds(ParsePositiveDouble(ReadOptionValue(args, ref i, inlineValue, name), name));
                    break;
                case "--samples":
                    requestedSamples = ParsePositiveInt(ReadOptionValue(args, ref i, inlineValue, name), name);
                    break;
                case "--visible":
                    visible = true;
                    break;
                case "--list":
                    listOnly = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
            }
        }

        return new ProbeOptions(
            flags,
            cameraName,
            searchTerms,
            source,
            mode,
            duration,
            requestedSamples,
            visible,
            listOnly,
            showHelp);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
CameraPreviewProbe options:
  --list                         Print detected cameras and exit.
  --camera <text>                Match a camera by name.
  --source <mf|directshow|any>   Choose camera source. Defaults to the probe path.
  --mode <WxH@fps|auto>          Request a mode, for example 3840x2160@24.
  --4k24                         Legacy shortcut for --mode 3840x2160@24.
  --seconds <n>                  Probe duration. Default: 6.
  --samples <n>                  Texture probe sample target. Default: 30.
  --visible                      Show the probe window instead of hiding it offscreen.
  --texture                      Use texture-native capture.
  --dx12-preview                 Render through the DX12 preview host.
  --mf-preview                   Use Media Foundation CPU preview through DX12.
""");
    }

    private static (string Name, string? Value) SplitOption(string arg)
    {
        var equals = arg.IndexOf('=');
        return equals > 0
            ? (arg[..equals], arg[(equals + 1)..])
            : (arg, null);
    }

    private static string ReadOptionValue(string[] args, ref int index, string? inlineValue, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(inlineValue))
        {
            return inlineValue;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }

    private static CameraSourcePreference ParseSource(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "mf" or "mediafoundation" or "media-foundation" => CameraSourcePreference.MediaFoundation,
            "ds" or "directshow" or "direct-show" => CameraSourcePreference.DirectShow,
            "any" or "auto" => CameraSourcePreference.Auto,
            _ => throw new ArgumentException($"Unknown camera source: {value}")
        };
    }

    private static CameraVideoMode? ParseMode(string value)
    {
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return CameraVideoMode.Auto;
        }

        var normalized = value.Replace(':', '@');
        var atParts = normalized.Split('@', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (atParts.Length is not 2)
        {
            throw new ArgumentException($"Mode must look like WxH@fps, for example 3840x2160@24: {value}");
        }

        var sizeParts = atParts[0].Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (sizeParts.Length is not 2
            || !int.TryParse(sizeParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(sizeParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || !double.TryParse(atParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var fps)
            || width <= 0
            || height <= 0
            || fps <= 0)
        {
            throw new ArgumentException($"Mode must use positive numeric width, height, and fps: {value}");
        }

        return new CameraVideoMode($"{width}x{height} @ {fps:0.###} fps", width, height, fps, null);
    }

    private static double ParsePositiveDouble(string value, string optionName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            throw new ArgumentException($"{optionName} requires a positive number.");
        }

        return result;
    }

    private static int ParsePositiveInt(string value, string optionName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            throw new ArgumentException($"{optionName} requires a positive integer.");
        }

        return result;
    }
}

internal enum CameraSourcePreference
{
    Auto,
    MediaFoundation,
    DirectShow
}
