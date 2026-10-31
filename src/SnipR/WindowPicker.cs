using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SnipR;

internal sealed record WindowCandidate(IntPtr Handle, Rectangle Bounds, string Title);

internal static class WindowPicker
{
    internal static WindowCandidate? FindAt(Point point, IntPtr excluded, bool includeFrame)
    {
        WindowCandidate? found = null;
        // Enumerate in z-order: WindowFromPoint would always return the overlay.
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == excluded || !IsWindowVisible(hwnd) || IsIconic(hwnd) || hwnd == GetShellWindow()) return true;
            // Ignore click-through overlays, tooltips, and non-activating utility windows.
            if ((Native.GetWindowLong(hwnd, -20) & (0x20 | 0x80 | 0x08000000)) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (!GetWindowRect(hwnd, out var hitRect) || !hitRect.Rectangle.Contains(point)) return true;
            var bounds = BoundsFor(hwnd, includeFrame);
            // A point on a title bar can still select the window's content area.
            if (bounds.IsEmpty) return true;
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            found = new(hwnd, bounds, title.Length == 0 ? "窗口" : title.ToString());
            return false;
        }, IntPtr.Zero);
        return found;
    }

    internal static Rectangle BoundsFor(IntPtr hwnd, bool includeFrame)
    {
        Rect bounds;
        if (includeFrame)
        {
            if (DwmGetWindowAttribute(hwnd, 9, out bounds, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(hwnd, out bounds)) return Rectangle.Empty;
        }
        else
        {
            if (!GetClientRect(hwnd, out bounds)) return Rectangle.Empty;
            var origin = new Point(bounds.Left, bounds.Top);
            if (!ClientToScreen(hwnd, ref origin)) return Rectangle.Empty;
            bounds = new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + bounds.Right - bounds.Left, Bottom = origin.Y + bounds.Bottom - bounds.Top };
        }
        return CaptureRegion.Normalize(new(bounds.Left, bounds.Top), new(bounds.Right, bounds.Bottom), Native.Desktop);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
