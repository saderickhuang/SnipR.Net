using System.Runtime.InteropServices;
using System.Drawing;

namespace SnipR;

internal static partial class Native
{
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    internal static Rectangle Desktop => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
}

internal static class CaptureRegion
{
    public static Rectangle Normalize(Point start, Point end, Rectangle desktop)
    {
        var rect = Rectangle.Intersect(Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)), desktop);
        // H.264 4:2:0 requires even dimensions. Keep the origin in physical screen pixels.
        return rect.Width < 16 || rect.Height < 16 ? Rectangle.Empty : new(rect.X, rect.Y, rect.Width & ~1, rect.Height & ~1);
    }
}
