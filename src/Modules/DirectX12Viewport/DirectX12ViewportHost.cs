using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JerichoDown.Modules.DirectX12Viewport;

public abstract class DirectX12ViewportHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;
    private const int SsBlackRect = 0x00000004;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;

    private readonly string _childWindowFailureMessage;
    private readonly bool _useBlackBackground;
    private IntPtr _viewportHandle;

    protected DirectX12ViewportHost(string childWindowFailureMessage, bool useBlackBackground = false)
    {
        _childWindowFailureMessage = childWindowFailureMessage;
        _useBlackBackground = useBlackBackground;
    }

    protected IntPtr ViewportHandle => _viewportHandle;

    protected int ViewportWidth => Math.Max(1, (int)ActualWidth);

    protected int ViewportHeight => Math.Max(1, (int)ActualHeight);

    protected sealed override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var width = ViewportWidth;
        var height = ViewportHeight;
        var style = WsChild | WsVisible | WsClipChildren | WsClipSiblings;
        if (_useBlackBackground)
        {
            style |= SsBlackRect;
        }

        _viewportHandle = CreateWindowEx(
            0,
            "static",
            string.Empty,
            style,
            0,
            0,
            width,
            height,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_viewportHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(_childWindowFailureMessage);
        }

        try
        {
            OnViewportCreated(_viewportHandle, width, height);
        }
        catch (Exception ex)
        {
            OnViewportCreateFailed(ex);
        }

        return new HandleRef(this, _viewportHandle);
    }

    protected sealed override void DestroyWindowCore(HandleRef hwnd)
    {
        try
        {
            OnViewportDestroying();
        }
        finally
        {
            if (hwnd.Handle != IntPtr.Zero)
            {
                DestroyWindow(hwnd.Handle);
            }

            _viewportHandle = IntPtr.Zero;
        }
    }

    protected sealed override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_viewportHandle == IntPtr.Zero)
        {
            return;
        }

        var width = ViewportWidth;
        var height = ViewportHeight;
        SetWindowPos(_viewportHandle, IntPtr.Zero, 0, 0, width, height, SwpNoZOrder | SwpNoActivate);
        try
        {
            OnViewportResized(width, height);
        }
        catch (Exception ex)
        {
            OnViewportResizeFailed(ex);
        }
    }

    protected abstract void OnViewportCreated(IntPtr hwnd, int width, int height);

    protected abstract void OnViewportDestroying();

    protected abstract void OnViewportResized(int width, int height);

    protected virtual void OnViewportCreateFailed(Exception ex)
    {
        throw new InvalidOperationException("DirectX12 viewport initialization failed.", ex);
    }

    protected virtual void OnViewportResizeFailed(Exception ex)
    {
        throw new InvalidOperationException("DirectX12 viewport resize failed.", ex);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);
}
