using System.Windows;
using System.Windows.Threading;

namespace WinSetupHub.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = false;
    }

    private static void LogCrash(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logRoot = Directory.Exists(@"E:\WinSetupHub\Data")
                ? @"E:\WinSetupHub\Data\logs"
                : string.IsNullOrWhiteSpace(localAppData)
                    ? Path.Combine(AppContext.BaseDirectory, "Data", "logs")
                    : Path.Combine(localAppData, "180HzSetupHub", "Data", "logs");

            Directory.CreateDirectory(logRoot);
            var logPath = Path.Combine(logRoot, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(logPath, exception.ToString());
        }
        catch
        {
            // Crash logging must never trigger another crash.
        }
    }
}
