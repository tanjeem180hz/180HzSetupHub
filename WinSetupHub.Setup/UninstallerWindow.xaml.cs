using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinSetupHub.Setup;

public partial class UninstallerWindow : Window
{
    private readonly InstallerService _installer = new();
    private bool _isUninstalling = false;
    private bool _isRepairing = false;
    private bool _isUninstallCompleted = false;
    private bool _isRepairCompleted = false;

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
        ConfirmClose();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        ConfirmClose();
    }

    private void ConfirmClose()
    {
        if (_isUninstallCompleted || _isRepairCompleted)
        {
            Close();
            return;
        }

        if (_isUninstalling || _isRepairing)
        {
            var opName = _isRepairing ? "Repair" : "Uninstallation";
            var res = MessageBox.Show(
                $"{opName} is currently in progress. Are you sure you want to cancel?",
                "180Hz Setup Hub",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        Close();
    }

    private async void BtnRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_isRepairCompleted)
        {
            LaunchInstalledApp();
            Close();
            return;
        }

        if (_isRepairing || _isUninstalling) return;

        _isRepairing = true;
        BtnRepair.Visibility = Visibility.Collapsed;
        BtnUninstall.Visibility = Visibility.Collapsed;
        BtnCancel.IsEnabled = true;

        if (FindResource("WindowsBlueBrush") is Brush blueBrush)
        {
            UninstallProgressBar.Foreground = blueBrush;
        }

        UninstallProgressBar.Value = 0;
        TxtUninstallStatus.Text = "Initializing repair process 0%...";

        var installRoot = InstallerService.GetDefaultInstallRoot();

        try
        {
            await _installer.RepairAsync(
                installRoot,
                progress: (status, percent) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        UninstallProgressBar.Value = percent;
                        TxtUninstallStatus.Text = status;
                    });
                },
                detailLog: null);

            _isRepairCompleted = true;
            _isRepairing = false;

            TxtUninstallStatus.Text = "Repair complete! 180Hz Setup Hub is healthy.";
            UninstallProgressBar.Value = 100;
            BtnRepair.Content = "Launch";
            BtnRepair.Visibility = Visibility.Visible;
            BtnCancel.Content = "Close";
        }
        catch (Exception ex)
        {
            _isRepairing = false;
            TxtUninstallStatus.Text = $"Repair failed: {ex.Message}";
            BtnRepair.Content = "Retry";
            BtnRepair.Visibility = Visibility.Visible;
            BtnUninstall.Visibility = Visibility.Visible;
            BtnCancel.IsEnabled = true;
        }
    }

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstallCompleted)
        {
            Close();
            return;
        }

        if (_isUninstalling || _isRepairing) return;

        _isUninstalling = true;
        BtnRepair.Visibility = Visibility.Collapsed;
        BtnUninstall.Visibility = Visibility.Collapsed;
        BtnCancel.IsEnabled = true;

        if (FindResource("WindowsRedBrush") is Brush redBrush)
        {
            UninstallProgressBar.Foreground = redBrush;
        }

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
                InstallerService.PerformFullCleanUninstall(installRoot, (status, percent) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtUninstallStatus.Text = status;
                        UninstallProgressBar.Value = percent;
                    });
                });
            });

            _isUninstallCompleted = true;
            _isUninstalling = false;
            TxtUninstallStatus.Text = "Uninstallation complete! All files and registry keys removed.";
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

    private void LaunchInstalledApp()
    {
        try
        {
            var installRoot = InstallerService.GetDefaultInstallRoot();
            var exePath = Path.Combine(installRoot, InstallerService.InstalledExeName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installRoot
                });
            }
        }
        catch
        {
            // Best effort
        }
    }
}
