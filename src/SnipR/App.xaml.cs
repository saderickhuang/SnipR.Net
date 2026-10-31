using System.Windows;

namespace SnipR;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await Diagnostics.RunAsync(e.Args));
            return;
        }
        instance = new Mutex(true, "Local\\SnipR.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("SnipR 已在运行，请从任务栏打开。", "SnipR");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "SnipR · 出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
