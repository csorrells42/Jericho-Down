using PodcastWorkbench.Video;
using System.Windows;
using System.Windows.Threading;

var textureProbe = args.Any(arg => arg.Equals("--texture", StringComparison.OrdinalIgnoreCase));
var dx12PreviewProbe = args.Any(arg => arg.Equals("--dx12-preview", StringComparison.OrdinalIgnoreCase));
var mediaFoundationPreviewProbe = args.Any(arg => arg.Equals("--mf-preview", StringComparison.OrdinalIgnoreCase));
var mode = args.Any(arg => arg.Equals("--4k24", StringComparison.OrdinalIgnoreCase))
    ? new CameraVideoMode("3840x2160 @ 24 fps", 3840, 2160, 24d, null)
    : CameraVideoMode.Auto;
var searchTerms = args
    .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
    .ToArray();

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

var requested = searchTerms.Length > 0 ? string.Join(' ', searchTerms) : string.Empty;
var candidates = textureProbe || mediaFoundationPreviewProbe
    ? cameras.Where(camera => camera.Source.Equals("Media Foundation", StringComparison.OrdinalIgnoreCase))
    : cameras.Where(camera => camera.Source.Equals("DirectShow", StringComparison.OrdinalIgnoreCase));
var target = candidates
    .OrderByDescending(camera => textureProbe || mediaFoundationPreviewProbe ? 0 : GetVirtualCameraPriority(camera))
    .ThenBy(camera => camera.Name, StringComparer.OrdinalIgnoreCase)
    .FirstOrDefault(camera => string.IsNullOrWhiteSpace(requested)
        ? textureProbe || mediaFoundationPreviewProbe || IsLikelyVirtualCamera(camera)
        : camera.Name.Contains(requested, StringComparison.OrdinalIgnoreCase))
    ?? candidates.FirstOrDefault();

if (target is null)
{
    Console.WriteLine(textureProbe || mediaFoundationPreviewProbe
        ? "No Media Foundation camera found to probe."
        : "No DirectShow camera found to probe.");
    return 2;
}

if (mediaFoundationPreviewProbe)
{
    return await RunDx12MediaFoundationPreviewProbeAsync(target, mode);
}

if (textureProbe)
{
    if (dx12PreviewProbe)
    {
        return await RunDx12TexturePreviewProbeAsync(target, mode);
    }

    Console.WriteLine($"Probing texture-native camera: {target.Name} / {mode.Label}");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var result = await TextureNativeCameraProbe.RunAsync(target.Name, mode, requestedSamples: 30, cancellation.Token);
    Console.WriteLine(result);
    return result.SamplesRead > 0 ? 0 : 5;
}

if (dx12PreviewProbe)
{
    return await RunDx12DirectShowPreviewProbeAsync(target, mode);
}

Console.WriteLine($"Probing DirectShow camera: {target.Name}");
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

if (!service.Start(target, CameraVideoMode.Auto))
{
    Console.WriteLine(service.LastStatus ?? "DirectShow probe failed before start.");
    return 3;
}

await Task.Delay(TimeSpan.FromSeconds(5));
service.Stop();

if (frames <= 0 || latestFrame is null)
{
    Console.WriteLine("Probe failed: no DirectShow frames arrived within 5 seconds.");
    return 4;
}

Console.WriteLine(
    $"Probe succeeded: {frames} frames, latest {latestFrame.Width}x{latestFrame.Height}, stride {latestFrame.Stride}, first frame {firstFrame:HH:mm:ss.fff}.");
return 0;

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

static Task<int> RunDx12DirectShowPreviewProbeAsync(CameraDevice camera, CameraVideoMode mode)
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
            window = CreateProbeWindow("DX12 DirectShow Preview Probe");
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

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
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

static Task<int> RunDx12TexturePreviewProbeAsync(CameraDevice camera, CameraVideoMode mode)
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
            stream = new TextureNativeCameraStream(camera, mode);
            window = CreateProbeWindow("DX12 Texture Preview Probe");
            host = new Direct3D12PreviewHost(stream.DuplicateNativeD3D12Device())
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            host.StatusChanged += (_, status) => Console.WriteLine(status);
            window.Content = host;
            window.Show();

            stream.FrameAvailable += (_, _) => Interlocked.Increment(ref infoFrames);
            stream.TextureFrameAvailable += (_, lease) =>
            {
                window.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        host.RenderTextureFrame(lease, denoiseEnabled: false, denoiseStrength: 2d);
                        Interlocked.Increment(ref renderedFrames);
                    }
                    finally
                    {
                        lease.Dispose();
                    }
                });
            };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
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

static Task<int> RunDx12MediaFoundationPreviewProbeAsync(CameraDevice camera, CameraVideoMode mode)
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
            window = CreateProbeWindow("DX12 Media Foundation Preview Probe");
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

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
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

static Window CreateProbeWindow(string title)
{
    return new Window
    {
        Title = title,
        Width = 640,
        Height = 360,
        Left = -30000,
        Top = -30000,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None
    };
}
