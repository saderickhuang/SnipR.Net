using System.ComponentModel;
using System.Net.Http;
using System.Windows;

namespace SnipR;

public partial class EncoderSetupWindow : Window
{
    private CancellationTokenSource? cancellation;
    private bool running;
    private bool closeRequested;

    public EncoderSetupWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InstallAsync();
        Closing += OnClosing;
    }

    private async Task InstallAsync()
    {
        if (running) return;
        running = true;
        RetryButton.Visibility = Visibility.Collapsed;
        DownloadProgress.IsIndeterminate = true;
        ProgressText.Text = "正在连接…";
        cancellation = new CancellationTokenSource();
        var progress = new Progress<EncoderProgress>(value =>
        {
            if (!running || closeRequested) return;
            ProgressText.Text = value.Message;
            DownloadProgress.IsIndeterminate = !value.Percent.HasValue;
            DownloadProgress.Value = value.Percent ?? 0;
        });
        var ready = false;
        try
        {
            await EncoderInstaller.Default.EnsureAsync(progress, cancellation.Token);
            ready = true;
        }
        catch (OperationCanceledException)
        {
            if (!closeRequested) ShowFailure("下载超时，请检查网络后重试。");
        }
        catch (HttpRequestException) { ShowFailure("暂时无法下载 FFmpeg，请检查网络连接后重试。"); }
        catch (Exception ex) { ShowFailure(ex.Message); }
        finally
        {
            running = false;
            cancellation.Dispose();
            cancellation = null;
        }
        if (ready || closeRequested) DialogResult = ready && !closeRequested;
    }

    private void ShowFailure(string message)
    {
        DownloadProgress.IsIndeterminate = false;
        DownloadProgress.Value = 0;
        ProgressText.Text = message;
        RetryButton.Visibility = Visibility.Visible;
    }

    public void Cancel() => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        e.Cancel = true;
        closeRequested = true;
        CancelButton.IsEnabled = false;
        ProgressText.Text = "正在取消…";
        cancellation?.Cancel();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();
    private async void Retry_Click(object sender, RoutedEventArgs e) => await InstallAsync();
}
