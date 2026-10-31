using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SnipR;

public partial class MainWindow : Window
{
    private enum Stage { Idle, Selecting, Countdown, Starting, Recording, Saving }
    private Stage stage;
    private readonly Settings settings = Settings.Load();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private RecordingSession? session;
    private SelectionWindow? selector;
    private RecordingBar? bar;
    private RecordingBorderWindow? recordingBorder;
    private string? lastVideo;
    private bool cancelRequested;
    private bool allowClose;
    private HwndSource? source;
    private bool startHotkey, stopHotkey;
    private bool closeAfterRecording;

    public MainWindow()
    {
        InitializeComponent();
        SystemAudioBox.IsChecked = settings.SystemAudio;
        MicrophoneBox.IsChecked = settings.Microphone;
        CursorBox.IsChecked = settings.ShowCursor;
        WindowFrameBox.IsChecked = settings.IncludeWindowFrame;
        Fps15.IsChecked = settings.Fps == 15;
        Fps30.IsChecked = settings.Fps == 30;
        Fps60.IsChecked = settings.Fps == 60;
        FolderText.Text = settings.OutputFolder;
        foreach (var control in new System.Windows.Controls.Primitives.ToggleButton[] { SystemAudioBox, MicrophoneBox, CursorBox, WindowFrameBox, Fps15, Fps30, Fps60 })
        {
            control.Checked += SettingsChanged;
            control.Unchecked += SettingsChanged;
        }
        Application.Current.SessionEnding += OnSessionEnding;
        timer.Tick += async (_, _) =>
        {
            if (stage != Stage.Recording || session == null) return;
            bar?.Update(session.Elapsed.ToString(@"hh\:mm\:ss"), "录制中  ·  Ctrl + Shift + S 停止", "■ 停止");
            if (session.HasExited || session.AudioFailure != null) await StopAsync();
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WindowMessage);
            startHotkey = Native.RegisterHotKey(hwnd, 1, 0x4006, 0x52);
            stopHotkey = Native.RegisterHotKey(hwnd, 2, 0x4006, 0x53);
            if (!startHotkey || !stopHotkey)
            {
                StatusText.Text = "快捷键被其他程序占用";
                DetailText.Text = "可使用窗口按钮操作；关闭冲突程序后重启 SnipR。";
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            timer.Stop();
            CloseRecordingBorder();
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.UnregisterHotKey(hwnd, 1); Native.UnregisterHotKey(hwnd, 2);
            source?.RemoveHook(WindowMessage);
            Application.Current.SessionEnding -= OnSessionEnding;
        };
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            handled = true;
            if (wParam.ToInt32() == 1 && stage == Stage.Idle) _ = BeginAsync();
            else if (wParam.ToInt32() == 2) _ = StopAsync();
        }
        return IntPtr.Zero;
    }

    private void ReadSettings()
    {
        settings.SystemAudio = SystemAudioBox.IsChecked == true;
        settings.Microphone = MicrophoneBox.IsChecked == true;
        settings.ShowCursor = CursorBox.IsChecked == true;
        settings.IncludeWindowFrame = WindowFrameBox.IsChecked == true;
        settings.Fps = Fps60.IsChecked == true ? 60 : Fps15.IsChecked == true ? 15 : 30;
        settings.Save();
    }

    private async Task BeginAsync(bool? pickWindow = null)
    {
        if (stage != Stage.Idle) return;
        stage = Stage.Selecting;
        cancelRequested = false;
        RecordButton.IsEnabled = false;
        WindowRecordButton.IsEnabled = false;
        try
        {
            if (pickWindow.HasValue) settings.PreferWindowSelection = pickWindow.Value;
            ReadSettings();
            if (!File.Exists(RecordingSession.EncoderPath)) throw new FileNotFoundException("缺少 FFmpeg 录像组件。请先运行 scripts/setup-ffmpeg.ps1，然后重新构建。 ");
            Hide();
            await Task.Delay(180);
            selector = new SelectionWindow(settings.PreferWindowSelection, settings.IncludeWindowFrame);
            selector.Show();
            var region = await selector.Result;
            selector = null;
            if (region == null || cancelRequested) { Restore(); return; }
            stage = Stage.Countdown;
            recordingBorder = new RecordingBorderWindow(region.Value);
            recordingBorder.Show();
            bar = new RecordingBar();
            bar.StopRequested += () => _ = StopAsync();
            bar.Show();
            bar.PositionNear(region.Value);
            for (var i = 3; i > 0; i--)
            {
                bar.Update($"{i} 秒后开始", $"{region.Value.Width} × {region.Value.Height} px  ·  Esc 取消", "取消");
                await Task.Delay(1000);
                if (cancelRequested) { Restore(); return; }
            }
            stage = Stage.Starting;
            bar.Update("正在启动…", "正在连接画面与音频设备", "取消");
            session = new RecordingSession();
            await session.StartAsync(region.Value, settings);
            stage = Stage.Recording;
            recordingBorder.MarkRecording();
            if (cancelRequested) { await StopAsync(); return; }
            bar.Update("00:00:00", "录制中  ·  Ctrl + Shift + S 停止", "■ 停止");
            timer.Start();
        }
        catch (Exception ex)
        {
            if (session != null) { await session.DisposeAsync(); session = null; }
            Restore();
            if (closeAfterRecording) return;
            ShowError("无法开始录制", ex);
        }
    }

    private async Task StopAsync()
    {
        if (stage == Stage.Selecting) { cancelRequested = true; selector?.Cancel(); return; }
        if (stage is Stage.Countdown or Stage.Starting) { cancelRequested = true; bar?.Update("正在取消…", "请稍候", "取消", false); return; }
        if (stage != Stage.Recording || session == null) return;
        stage = Stage.Saving;
        timer.Stop();
        CloseRecordingBorder();
        bar?.Update("正在保存…", "合并画面、系统声音与麦克风", "保存中", false);
        try
        {
            lastVideo = await session.StopAsync();
            StatusText.Text = "已保存 · 录制完成";
            DetailText.Text = lastVideo;
            OpenLastButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { ShowError("保存出现问题", ex); }
        finally { session = null; Restore(); }
    }

    private void Restore()
    {
        CloseRecordingBorder();
        bar?.FinishAndClose(); bar = null;
        stage = Stage.Idle;
        RecordButton.IsEnabled = true;
        WindowRecordButton.IsEnabled = true;
        if (closeAfterRecording) { allowClose = true; Close(); return; }
        Show(); WindowState = WindowState.Normal; Activate();
    }

    private void CloseRecordingBorder()
    {
        recordingBorder?.Close();
        recordingBorder = null;
    }

    private void ShowError(string title, Exception ex)
    {
        StatusText.Text = title;
        DetailText.Text = ex.Message;
        MessageBox.Show(this, ex.Message, "SnipR · " + title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void Record_Click(object sender, RoutedEventArgs e) => await BeginAsync(false);
    private async void WindowRecord_Click(object sender, RoutedEventArgs e) => await BeginAsync(true);
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        // Wait for both radio-button change events before persisting the final value.
        Dispatcher.BeginInvoke(() =>
        {
            try { ReadSettings(); }
            catch (Exception ex) { StatusText.Text = "设置保存失败"; DetailText.Text = ex.Message; }
        });
    }
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        try { ReadSettings(); } catch { /* Keep OS shutdown responsive. */ }
    }
    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择录像保存位置", InitialDirectory = settings.OutputFolder };
        if (dialog.ShowDialog(this) != true) return;
        settings.OutputFolder = dialog.FolderName;
        FolderText.Text = settings.OutputFolder;
        try { settings.Save(); } catch (Exception ex) { ShowError("设置无法保存", ex); }
    }
    private void OpenLast_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (lastVideo == null || !File.Exists(lastVideo)) throw new FileNotFoundException("视频文件已移动或删除。");
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            info.Arguments = $"/select,\"{lastVideo}\"";
            Process.Start(info);
        }
        catch (Exception ex) { ShowError("无法打开视频位置", ex); }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        try { ReadSettings(); }
        catch (Exception ex)
        {
            StatusText.Text = "设置保存失败"; DetailText.Text = ex.Message;
            if (!allowClose) { e.Cancel = true; return; }
        }
        if (allowClose) return;
        if (stage == Stage.Idle)
        {
            return;
        }
        e.Cancel = true;
        closeAfterRecording = true;
        if (stage == Stage.Saving) return;
        await StopAsync();
    }
}
