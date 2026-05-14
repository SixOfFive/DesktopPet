using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Neko;

internal static class WindowEnumerator
{
    public static List<Rectangle> GetVisibleWindowRects(IntPtr selfHwnd)
    {
        var rects = new List<Rectangle>();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == selfHwnd) return true;
            if (!IsAltTabWindow(hwnd)) return true;
            if (!GetWindowRect(hwnd, out var r)) return true;

            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w < 80 || h < 40) return true;

            rects.Add(new Rectangle(r.Left, r.Top, w, h));
            return true;
        }, IntPtr.Zero);
        return rects;
    }

    private static bool IsAltTabWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (IsIconic(hwnd)) return false;

        int cloaked = 0;
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ref cloaked, sizeof(int));
        if (cloaked != 0) return false;

        IntPtr owner = GetWindow(hwnd, GW_OWNER);
        long exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

        if ((exStyle & WS_EX_APPWINDOW) != 0) return true;
        if (owner == IntPtr.Zero && (exStyle & WS_EX_TOOLWINDOW) == 0) return true;

        return false;
    }

    private const int DWMWA_CLOAKED = 14;
    private const int GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern IntPtr GetWindowLongPtr32(IntPtr hwnd, int nIndex);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, nIndex) : GetWindowLongPtr32(hwnd, nIndex);
    }
}
