using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NetworkMonitor.Native;

internal static class NativeWindow
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void EnableDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var on = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }

    public static void ApplyOverlayStyle(Window window, bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLong(hwnd, GwlExStyle);
        style |= WsExToolWindow | WsExNoActivate;
        if (clickThrough)
            style |= WsExTransparent;
        else
            style &= ~WsExTransparent;
        SetWindowLong(hwnd, GwlExStyle, style);
    }
}
