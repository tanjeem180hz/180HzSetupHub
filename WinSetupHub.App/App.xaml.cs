using System;
using System.Windows;
using System.Windows.Threading;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Don't let one bad Task/UI exception crash the whole app —
            // log it and keep the shell alive.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            ActivityLogger.Instance.Log("Application started.", ActivityType.Info);
            ThemeService.ApplyTheme(SettingsService.Instance.Current.DarkTheme);

            if (Array.Exists(e.Args, a => string.Equals(a, "--auto-check", StringComparison.OrdinalIgnoreCase)))
            {
                RunHeadlessAutoCheckAsync();
                return;
            }

            UpdateMonitorService.Instance.Start();

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private async void RunHeadlessAutoCheckAsync()
        {
            try
            {
                ActivityLogger.Instance.Log("Running headless scheduled update check…", ActivityType.Info);
                var winget = new WingetService();
                var upgradable = await winget.GetUpgradableAppsAsync();
                if (upgradable.Count > 0)
                {
                    ActivityLogger.Instance.Log($"Scheduled check found {upgradable.Count} pending update(s).", ActivityType.Info);
                    NotificationService.Notify("Updates Available", $"{upgradable.Count} app(s) have pending updates.");
                }
                else
                {
                    ActivityLogger.Instance.Log("Scheduled check: all applications are up to date.", ActivityType.Info);
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Scheduled check failed: {ex.Message}", ActivityType.Error);
            }
            finally
            {
                Shutdown();
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ActivityLogger.Instance.Log($"Unexpected error: {e.Exception.Message}", ActivityType.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                ActivityLogger.Instance.Log($"Fatal error: {ex.Message}", ActivityType.Error);
        }
    }
}
