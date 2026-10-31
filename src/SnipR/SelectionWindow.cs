using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Globalization;
using RectI = System.Drawing.Rectangle;
using PointI = System.Drawing.Point;

namespace SnipR;

internal sealed class SelectionWindow : Window
{
    private readonly TaskCompletionSource<RectI?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RectI desktop = Native.Desktop;
    private readonly SelectionGesture gesture;
    private RectI selection => gesture.Selection;
    private readonly bool pickWindow;
    private readonly bool includeFrame;
    private bool windowLocked;
    private string windowTitle = "";
    private readonly SelectionSurface surface;
    public Task<RectI?> Result => result.Task;

    public SelectionWindow(bool pickWindow = false, bool includeFrame = false)
    {
        this.pickWindow = pickWindow;
        this.includeFrame = includeFrame;
        gesture = new SelectionGesture(desktop);
        surface = new SelectionSurface(this);
        Content = surface;
        Title = "SnipR · 框选区域";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = true;
        Cursor = Cursors.Cross;
        Left = desktop.X; Top = desktop.Y; Width = desktop.Width; Height = desktop.Height;
        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => { Place(); Activate(); Focus(); Native.GetCursorPos(out var cursor); UpdateWindowPreview(cursor); };
        Closed += (_, _) => result.TrySetResult(null);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Cancel();
            if (e.Key == Key.Enter && !selection.IsEmpty) Complete();
            if (e.Key == Key.Tab && pickWindow) { windowLocked = false; Native.GetCursorPos(out var cursor); UpdateWindowPreview(cursor); e.Handled = true; }
        };
    }

    private void Place() => Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), desktop.X, desktop.Y, desktop.Width, desktop.Height, 0x0040);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = EventScreenPoint(e);
        UpdateWindowPreview(point);
        if (gesture.Down(point, e.ClickCount)) { Complete(); return; }
        if (pickWindow) windowLocked = true;
        CaptureMouse();
        surface.InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var current = EventScreenPoint(e);
        if (gesture.Pressed)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            gesture.Move(current, SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX, SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY);
            if (gesture.Dragging) windowTitle = "";
        }
        else UpdateWindowPreview(current);
        surface.InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!gesture.Pressed) return;
        var current = EventScreenPoint(e);
        var dpi = VisualTreeHelper.GetDpi(this);
        gesture.Up(current, SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX, SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY);
        ReleaseMouseCapture();
        surface.InvalidateVisual();
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => Cancel();
    private void UpdateWindowPreview(PointI point)
    {
        if (!pickWindow || windowLocked || gesture.Pressed) return;
        var candidate = WindowPicker.FindAt(point, new WindowInteropHelper(this).Handle, includeFrame);
        gesture.Preview(candidate?.Bounds ?? RectI.Empty);
        windowTitle = candidate?.Title ?? "";
        surface.InvalidateVisual();
    }
    private PointI EventScreenPoint(MouseEventArgs e)
    {
        var pt = PointToScreen(e.GetPosition(this));
        return new PointI((int)Math.Round(pt.X), (int)Math.Round(pt.Y));
    }
    public void Cancel() { result.TrySetResult(null); Close(); }
    private void Complete() { result.TrySetResult(selection); Close(); }

    private void DrawSelection(DrawingContext dc)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        var shade = new SolidColorBrush(Color.FromArgb(145, 5, 14, 10));
        var dpi = VisualTreeHelper.GetDpi(this);
        Rect local = new((selection.X - desktop.X) / dpi.DpiScaleX, (selection.Y - desktop.Y) / dpi.DpiScaleY, selection.Width / dpi.DpiScaleX, selection.Height / dpi.DpiScaleY);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(full));
        if (!selection.IsEmpty) mask.Children.Add(new RectangleGeometry(local));
        // A nonzero alpha keeps the clear selected area hit-testable on a layered window.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, full);
        dc.DrawGeometry(shade, null, mask);
        if (!selection.IsEmpty)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(183, 245, 207)), 1.5);
            dc.DrawRectangle(null, pen, local);
            foreach (var pt in new[] { local.TopLeft, local.TopRight, local.BottomLeft, local.BottomRight }) dc.DrawRectangle(pen.Brush, null, new Rect(pt.X - 3, pt.Y - 3, 6, 6));
        }
        Native.GetCursorPos(out var cursor);
        var anchorX = selection.IsEmpty ? (cursor.X - desktop.X) / dpi.DpiScaleX : local.Left;
        var anchorY = selection.IsEmpty ? (cursor.Y - desktop.Y) / dpi.DpiScaleY + 32 : local.Bottom + 14;
        string label = selection.IsEmpty ? (pickWindow ? "悬停选择窗口  ·  拖动自由框选  ·  Esc 取消" : "拖动框选录制区域  ·  Esc / 右键取消")
            : $"{selection.Width} × {selection.Height} px   ·   选区内双击 / Enter 确认   ·   Esc 取消";
        if (pickWindow) label += "   ·   Tab 重选窗口";
        if (windowTitle.Length > 0) label = (windowTitle.Length > 24 ? windowTitle[..24] + "…" : windowTitle) + "   ·   " + label;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 13, Brushes.White, dpi.PixelsPerDip);
        var x = Math.Clamp(anchorX, 12, Math.Max(12, ActualWidth - text.Width - 40));
        var y = Math.Clamp(anchorY, 12, Math.Max(12, ActualHeight - 52));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(22, 35, 28)), null, new Rect(x, y, text.Width + 28, 40), 7, 7);
        dc.DrawText(text, new Point(x + 14, y + 10));
    }

    private sealed class SelectionSurface(SelectionWindow owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) => owner.DrawSelection(dc);
    }
}
