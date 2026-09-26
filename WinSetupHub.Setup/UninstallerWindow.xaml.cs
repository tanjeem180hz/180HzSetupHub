using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace WinSetupHub.Setup;

public partial class UninstallerWindow : Window
{
    private bool _isUninstalling = false;
    private bool _isCompleted = false;

    public UninstallerWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            Close();
            return;
        }

        if (_isUninstalling) return;

        _isUninstalling = true;
        BtnUninstall.Visibility = Visibility.Collapsed;
        BtnCancel.IsEnabled = true;

        var installRoot = InstallerService.GetDefaultInstallRoot();
        var currentExe = Program.GetCurrentProcessPath();

        // If running from inside installRoot, delegate to worker
        if (!string.IsNullOrEmpty(currentExe) && currentExe.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                TxtUninstallStatus.Text = "Removing app package 30%...";
                UninstallProgressBar.Value = 30;

                var tempWorker = Path.Combine(Path.GetTempPath(), $"180HzSetupHub_Uninstall_{Guid.NewGuid():N}.exe");
                File.Copy(currentExe, tempWorker, overwrite: true);

                Process.Start(new ProcessStartInfo(tempWorker)
                {
                    Arguments = $"--uninstall-worker \"{installRoot}\"",
                    UseShellExecute = true
                });

                Application.Current.Shutdown(0);
                return;
            }
            catch (Exception ex)
            {
                TxtUninstallStatus.Text = $"Error: {ex.Message}";
                BtnCancel.IsEnabled = true;
                _isUninstalling = false;
                return;
            }
        }

        // Direct execution (outside install root)
        try
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtUninstallStatus.Text = "Removing app package 25%...";
                    UninstallProgressBar.Value = 25;
                });
                InstallerService.StopExistingApp(Path.Combine(installRoot, InstallerService.InstalledExeName));
                Thread.Sleep(400);

                Dispatcher.Invoke(() =>
                {
                    TxtUninstallStatus.Text = "Removing app package 55%...";
                    UninstallProgressBar.Value = 55;
                });
                InstallerService.RemoveShortcuts();
                Thread.Sleep(300);

                Dispatcher.Invoke(() =>
                {
                    TxtUninstallStatus.Text = "Removing app package 80%...";
                    UninstallProgressBar.Value = 80;
                });
                InstallerService.UnregisterUninstall();
                Thread.Sleep(300);

                Dispatcher.Invoke(() =>
                {
                    TxtUninstallStatus.Text = "Removing app package 95%...";
                    UninstallProgressBar.Value = 95;
                });

                if (Directory.Exists(installRoot))
                {
                    try
                    {
                        Directory.Delete(installRoot, recursive: true);
                    }
                    catch
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c timeout /t 2 & rmdir /s /q \"{installRoot}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                }
            });

            _isCompleted = true;
            _isUninstalling = false;
            TxtUninstallStatus.Text = "Uninstallation complete";
            UninstallProgressBar.Value = 100;
            BtnCancel.Content = "Close";
        }
        catch (Exception ex)
        {
            _isUninstalling = false;
            TxtUninstallStatus.Text = $"Error: {ex.Message}";
            BtnCancel.IsEnabled = true;
        }
    }
}
