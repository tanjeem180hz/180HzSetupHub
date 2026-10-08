using System;
using System.Windows;
using System.Windows.Threading;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;
using SetupHub180Hz.Views;

namespace SetupHub180Hz
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Automatic Run as Administrator:
            // Ensure 180Hz Setup Hub always runs with Administrator rights
            // for system package installation, updates, uninstallation, and cleanup.
            if (!IsRunningAsAdministrator())
            {
                if (TryRestartAsAdministrator(e.Args))
                {
                    Shutdown();
                    return;
                }
            }

            // Ensure Windows marks this application to always launch elevated
            EnsureAppCompatRunAsAdmin();

            // Don't let one bad Task/UI exception crash the whole app —
            // log it and keep the shell alive.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            ActivityLogger.Instance.Log("Application started (Administrator).", ActivityType.Info);
            ThemeService.ApplyTheme(SettingsService.Instance.Current.DarkTheme);

            if (Array.Exists(e.Args, a => string.Equals(a, "--auto-check", StringComparison.OrdinalIgnoreCase)))
            {
                RunHeadlessAutoCheckAsync();
                return;
            }

            // Set shutdown mode to OnMainWindowClose so closing SplashScreen doesn't terminate process
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Launch futuristic Cyber Toxic Splash Screen immediately
            SplashScreenWindow? splash = null;
            try
            {
                splash = new SplashScreenWindow();
                splash.Show();
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Could not display splash screen: {ex.Message}", ActivityType.Warning);
            }

            _ = InitializeAndLaunchAppAsync(splash);
        }

        private async Task InitializeAndLaunchAppAsync(SplashScreenWindow? splash)
        {
            try
            {
                // Stage 1: Core System & Lottie initialization (15%)
                splash?.UpdateProgress(15, "INITIALIZING 180Hz SYSTEM CORE...");
                LottieService.Initialize();
                await Task.Delay(160);

                // Stage 2: Hardware & Latency Profiles (40%)
                splash?.UpdateProgress(40, "CONFIGURING LATENCY & PERFORMANCE PROFILES...");
                UpdateMonitorService.Instance.Start();
                await Task.Delay(180);

                // Stage 3: Package Catalog & Winget Engine (65%)
                splash?.UpdateProgress(65, "INDEXING WINGET PACKAGES & REPOSITORIES...");
                await Task.Delay(200);

                // Stage 4: Revo Deep Uninstaller Engine & Cache Calibration (88%)
                splash?.UpdateProgress(88, "CALIBRATING REVO UNINSTALLER ENGINE...");
                await Task.Delay(180);

                // Stage 5: Finalization (100%)
                splash?.UpdateProgress(100, "READY // LAUNCHING 180Hz SETUP HUB...");
                await Task.Delay(140);

                // Create and reveal MainWindow
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Opacity = 0;
                mainWindow.Show();

                // Smoothly fade in MainWindow
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
                mainWindow.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                // Smoothly fade out and close splash screen
                if (splash != null)
                {
                    await splash.FadeOutAndCloseAsync();
                }
            }
            catch (Exception ex)
            {
                splash?.Close();
                ActivityLogger.Instance.Log($"Startup error: {ex}", ActivityType.Error);
                ThemedMessageBox.Show(
                    $"180Hz Setup Hub encountered a startup error:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                    "180Hz Setup Hub - Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRestartAsAdministrator(string[]? args = null)
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
                {
                    return false;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                if (args != null && args.Length > 0)
                {
                    psi.Arguments = string.Join(" ", System.Linq.Enumerable.Select(args, a => $"\"{a}\""));
                }

                var proc = System.Diagnostics.Process.Start(psi);
                return proc != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not self-elevate: {ex.Message}");
                return false;
            }
        }

        public static void EnsureAppCompatRunAsAdmin()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    var fileName = System.IO.Path.GetFileName(exePath);
                    if (!fileName.Equals("180HzSetupHub.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                    if (key != null)
                    {
                        var val = key.GetValue(exePath) as string;
                        if (string.IsNullOrEmpty(val) || !val.Contains("RUNASADMIN"))
                        {
                            key.SetValue(exePath, "~ RUNASADMIN");
                        }
                    }
                }
            }
            catch
            {
                // Non-critical if registry write fails
            }
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
            if (MainWindow == null || Windows.Count == 0)
            {
                ThemedMessageBox.Show(
                    $"Fatal UI error during startup:\n\n{e.Exception.Message}\n\n{e.Exception.InnerException?.Message}",
                    "180Hz Setup Hub",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                e.Handled = false;
                Shutdown(1);
                return;
            }
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                ActivityLogger.Instance.Log($"Fatal error: {ex.Message}", ActivityType.Error);
        }
    }
}
