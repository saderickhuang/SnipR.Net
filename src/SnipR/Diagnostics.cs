using System.Drawing;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PointI = System.Drawing.Point;
using PointW = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SnipR;

internal static class Diagnostics
{
    public static async Task<int> RunAsync(string[] args)
    {
        var outputIndex = Array.IndexOf(args, "--output");
        var output = outputIndex >= 0 && args.Length > outputIndex + 1 ? Path.GetFullPath(args[outputIndex + 1]) : Path.GetFullPath("artifacts/smoke");
        Directory.CreateDirectory(output);
        Window? pattern = null;
        RecordingBorderWindow? border = null;
        var report = new Dictionary<string, object>();
        var animator = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        try
        {
            BehaviorChecks.Run(output);
            report["selectionAndSettings"] = "double-click, jitter, outside clicks, re-drag, window preview, settings roundtrip passed";
            var desktop = new Rectangle(-1920, -200, 5760, 2360);
            Require(CaptureRegion.Normalize(new PointI(301, 201), new PointI(-100, -100), desktop) == new Rectangle(-100, -100, 400, 300), "Reverse drag and negative origin");
            Require(CaptureRegion.Normalize(new PointI(0, 0), new PointI(15, 300), desktop).IsEmpty, "Minimum region");
            Require(CaptureRegion.Normalize(new PointI(4000, 0), new PointI(4100, 300), desktop).IsEmpty, "Outside desktop");
            Require(CaptureRegion.Normalize(new PointI(-2000, -500), new PointI(-1500, 100), desktop) == new Rectangle(-1920, -200, 420, 300), "Clipped bounds");
            report["geometry"] = "4 checks passed";
            var framed = new Window { Title = "SnipR · 窗口边界测试", Width = 320, Height = 180, Left = 100, Top = 100,
                Topmost = true, Content = new TextBlock { Text = "窗口内容与标题栏", Margin = new Thickness(20) } };
            try
            {
                framed.Show();
                await Task.Delay(200);
                var hwnd = new WindowInteropHelper(framed).Handle;
                var contentBounds = WindowPicker.BoundsFor(hwnd, false);
                var frameBounds = WindowPicker.BoundsFor(hwnd, true);
                Require(frameBounds.Contains(contentBounds) && frameBounds.Height > contentBounds.Height, "Window title bar option");
                report["windowFrame"] = "standard window includes title bar only when enabled";
            }
            finally { framed.Close(); }
            var text = new TextBlock { Text = "SnipR · WPF 录像测试", Foreground = Brushes.White, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 70, 45)) };
            grid.Children.Add(text);
            pattern = new Window { Title = "SnipR · 测试画面", Width = 640, Height = 380, Left = 60, Top = 60, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, Content = grid };
            if (args.Contains("--screen-edge")) { pattern.Left = 0; pattern.Top = 0; }
            pattern.Show();
            await Task.Delay(350);
            var start = pattern.PointToScreen(new PointW(0, 0));
            var end = pattern.PointToScreen(new PointW(pattern.ActualWidth, pattern.ActualHeight));
            var region = CaptureRegion.Normalize(new PointI((int)start.X, (int)start.Y), new PointI((int)end.X, (int)end.Y), Native.Desktop);
            var picked = WindowPicker.FindAt(new PointI(region.Left + 50, region.Top + 50), IntPtr.Zero, false);
            Require(picked?.Handle == new WindowInteropHelper(pattern).Handle && picked.Bounds == region, "Window content selection in physical pixels");
            report["windowSelection"] = "visible test window identified; content bounds match physical capture region";
            var count = 0;
            animator.Tick += (_, _) => text.Text = $"SnipR · WPF 录像测试\n{++count:000}  /  {DateTime.Now:HH:mm:ss.fff}";
            animator.Start();
            var options = new Settings { OutputFolder = output, SystemAudio = args.Contains("--system-audio"), Microphone = args.Contains("--microphone"), Fps = 30, ShowCursor = false };
            report["systemAudio"] = options.SystemAudio;
            report["microphone"] = options.Microphone;
            report["width"] = region.Width; report["height"] = region.Height;
            if (args.Contains("--border"))
            {
                border = new RecordingBorderWindow(region);
                border.Show();
                await Task.Delay(200);
                Require(border.IsVisible, "Recording border is visible");
                Require(border.CaptureExcluded, "Recording border is excluded from capture");
                var style = Native.GetWindowLong(new WindowInteropHelper(border).Handle, -20);
                Require((style & 0x08000020) == 0x08000020, "Recording border passes clicks and does not activate");
                Require(pattern.IsActive, "Recording border preserves the active window");
                Require(WindowPicker.FindAt(new PointI(region.Left + 50, region.Top + 50), IntPtr.Zero, false)?.Handle
                    == new WindowInteropHelper(pattern).Handle, "Window picker ignores transparent border overlay");
                var actualOrigin = border.PointToScreen(new PointW(0, 0));
                var actualEnd = border.PointToScreen(new PointW(border.ActualWidth, border.ActualHeight));
                var expected = Rectangle.Intersect(Rectangle.Inflate(region, 3, 3), Native.Desktop);
                Require(Math.Abs(actualOrigin.X - expected.Left) < 1 && Math.Abs(actualOrigin.Y - expected.Top) < 1
                    && Math.Abs(actualEnd.X - expected.Right) < 1 && Math.Abs(actualEnd.Y - expected.Bottom) < 1, "Border physical pixel bounds");
                report["border"] = "visible; capture excluded; mouse transparent; no activation; physical bounds verified";
            }
            await using var recording = new RecordingSession();
            await recording.StartAsync(region, options);
            if (border != null)
            {
                border.MarkRecording();
                await Task.Delay(150);
                // Render the WPF visual itself: screen capture deliberately excludes it.
                var dpi = VisualTreeHelper.GetDpi(border);
                var bitmap = new RenderTargetBitmap((int)Math.Round(border.ActualWidth * dpi.DpiScaleX),
                    (int)Math.Round(border.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bitmap.Render(border);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "border-visual.png")); png.Save(file);
            }
            await Task.Delay(1000);
            if (options.SystemAudio)
            {
                using var tone = new WasapiOut();
                tone.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.035, Type = SignalGeneratorType.Sin }.ToWaveProvider());
                tone.Play();
                await Task.Delay(1200);
                tone.Stop();
            }
            else await Task.Delay(1200);
            await Task.Delay(1000);
            report["video"] = await recording.StopAsync();
            if (border != null)
            {
                border.Close();
                Require(!Application.Current.Windows.OfType<RecordingBorderWindow>().Any(), "Recording border cleanup");
                border = null;
                report["borderCleanup"] = "passed";
            }
            report["result"] = "passed";
            await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            report["result"] = "failed"; report["error"] = ex.ToString();
            await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally { animator.Stop(); border?.Close(); pattern?.Close(); }
    }

    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException("Geometry check failed: " + name); }
}
