using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace SnipR;

/// <summary>A passive outline that lives only for countdown and active recording.</summary>
internal sealed class RecordingBorderWindow : Window
{
    private const int BorderPixels = 3;
    private readonly Rectangle bounds;
    private readonly BorderSurface surface;
    private bool recording;
    public bool CaptureExcluded { get; private set; }

    public RecordingBorderWindow(Rectangle region)
    {
        // Prefer pixels outside the video. At a desktop edge, draw inward so a
        // fullscreen selection still has a visible outline; affinity excludes it.
        bounds = Rectangle.Intersect(Rectangle.Inflate(region, BorderPixels, BorderPixels), Native.Desktop);
        Title = "SnipR · 录制范围";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        surface = new BorderSurface(this);
        Content = surface;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: do not
            // intercept clicks or keyboard focus, including clicks on the line.
            Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x20 | 0x08000000 | 0x80);
            CaptureExcluded = Native.SetWindowDisplayAffinity(hwnd, 0x11);
            Place();
        };
        Loaded += (_, _) => Place();
    }

    public void MarkRecording()
    {
        recording = true;
        surface.InvalidateVisual();
    }

    private void Place() => Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
        bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Reapply physical coordinates after WPF processes the suggested DPI rect.
        Dispatcher.BeginInvoke(() => { if (IsVisible) { Place(); surface.InvalidateVisual(); } });
    }

    private sealed class BorderSurface(RecordingBorderWindow owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var x = BorderPixels / dpi.DpiScaleX;
            var y = BorderPixels / dpi.DpiScaleY;
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 2 * x || height <= 2 * y) return;
            var brush = new SolidColorBrush(owner.recording
                ? Color.FromRgb(255, 91, 91) : Color.FromRgb(183, 245, 207));
            // Filled strips avoid half-pixel stroke expansion into the selected area.
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, y));
            dc.DrawRectangle(brush, null, new Rect(0, height - y, width, y));
            dc.DrawRectangle(brush, null, new Rect(0, y, x, height - 2 * y));
            dc.DrawRectangle(brush, null, new Rect(width - x, y, x, height - 2 * y));
        }
    }
}
