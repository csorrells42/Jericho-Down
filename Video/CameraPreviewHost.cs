using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PodcastWorkbench.Video;

public sealed class CameraPreviewHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WmCapDriverConnect = 0x0400 + 10;
    private const int WmCapDriverDisconnect = 0x0400 + 11;
    private const int WmCapSetPreview = 0x0400 + 50;
    private const int WmCapSetPreviewRate = 0x0400 + 52;
    private const int WmCapSetScale = 0x0400 + 53;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private IntPtr _captureWindow;
    private int? _pendingDeviceNumber;
    private bool _isPreviewing;

    public bool StartPreview(int deviceNumber)
    {
        _pendingDeviceNumber = deviceNumber;
        if (_captureWindow == IntPtr.Zero)
        {
            return false;
        }

        StopPreview();

        if (SendMessage(_captureWindow, WmCapDriverConnect, (IntPtr)deviceNumber, IntPtr.Zero) == IntPtr.Zero)
        {
            return false;
        }

        SendMessage(_captureWindow, WmCapSetScale, new IntPtr(1), IntPtr.Zero);
        SendMessage(_captureWindow, WmCapSetPreviewRate, new IntPtr(33), IntPtr.Zero);
        SendMessage(_captureWindow, WmCapSetPreview, new IntPtr(1), IntPtr.Zero);
        _isPreviewing = true;
        return true;
    }

    public void StopPreview()
    {
        if (_captureWindow == IntPtr.Zero || !_isPreviewing)
        {
            return;
        }

        SendMessage(_captureWindow, WmCapSetPreview, IntPtr.Zero, IntPtr.Zero);
        SendMessage(_captureWindow, WmCapDriverDisconnect, IntPtr.Zero, IntPtr.Zero);
        _isPreviewing = false;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _captureWindow = CapCreateCaptureWindow(
            "Podcast Workbench Camera Preview",
            WsChild | WsVisible,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            0);

        if (_pendingDeviceNumber is int deviceNumber)
        {
            StartPreview(deviceNumber);
        }

        return new HandleRef(this, _captureWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        StopPreview();
        if (hwnd.Handle != IntPtr.Zero)
        {
            DestroyWindow(hwnd.Handle);
        }

        _captureWindow = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_captureWindow == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            _captureWindow,
            IntPtr.Zero,
            0,
            0,
            Math.Max(1, (int)rcBoundingBox.Width),
            Math.Max(1, (int)rcBoundingBox.Height),
            SwpNoZOrder | SwpNoActivate);
    }

    [DllImport("avicap32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr capCreateCaptureWindowA(
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parentWindow,
        int id);

    private static IntPtr CapCreateCaptureWindow(
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parentWindow,
        int id)
    {
        return capCreateCaptureWindowA(windowName, style, x, y, width, height, parentWindow, id);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}

