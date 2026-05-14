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
            if (!IsWindowVisible(hwnd)) return true;
            if (IsIconic(hwnd)) return true;

            int cloaked = 0;
            DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ref cloaked, sizeof(int));
            if (cloaked != 0) return true;

            if (!GetWindowRect(hwnd, out var r)) return true;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w < 80 || h < 40) return true;

            rects.Add(new Rectangle(r.Left, r.Top, w, h));
            return true;
        }, IntPtr.Zero);
        return rects;
    }

    private const int DWMWA_CLOAKED = 14;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
