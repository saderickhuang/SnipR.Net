using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SnipR;

public partial class RecordingBar : Window
{
    private bool finished;
    public event Action? StopRequested;
    public bool CaptureExcluded { get; private set; }
    public RecordingBar()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => CaptureExcluded = Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) StopRequested?.Invoke(); };
        Closing += (_, e) => { if (!finished) { e.Cancel = true; StopRequested?.Invoke(); } };
    }
    internal void PositionNear(System.Drawing.Rectangle region)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)(Width * dpi.DpiScaleX);
        var height = (int)(Height * dpi.DpiScaleY);
        var desktop = Native.Desktop;
        var x = Math.Clamp(region.X, desktop.Left, Math.Max(desktop.Left, desktop.Right - width));
        var y = region.Bottom + 12;
        if (y + height > desktop.Bottom) y = Math.Max(desktop.Top, region.Top - height - 12);
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), x, y, width, height, 0x0040);
    }
    public void Update(string text, string hint, string button, bool enabled = true)
    {
        TimerText.Text = text; HintText.Text = hint; StopButton.Content = button; StopButton.IsEnabled = enabled;
    }
    public void FinishAndClose() { finished = true; Close(); }
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void DragBar(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
}
