using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace WinSetupHub.Setup;

public partial class InstallerWindow : Window
{
    private readonly InstallerService _installer = new();
    private bool _isInstalling = false;
    private bool _isCompleted = false;

    public InstallerWindow()
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

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        ConfirmCancel();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        ConfirmCancel();
    }

    private void ConfirmCancel()
    {
        if (_isCompleted)
        {
            Close();
            return;
        }

        if (_isInstalling)
        {
            var res = MessageBox.Show(
                "Installation is in progress. Are you sure you want to cancel?",
                "180Hz Setup Hub",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        Close();
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            LaunchInstalledApp();
            Close();
            return;
        }

        if (_isInstalling) return;

        _isInstalling = true;
        // In Windows App Installer, Install button is hidden while installing, only Cancel remains
        BtnInstall.Visibility = Visibility.Collapsed;
        BtnCancel.IsEnabled = true;
        ChkLaunchWhenReady.IsEnabled = false;

        TxtStatus.Text = "Installing app package 0%...";
        InstallProgressBar.Value = 0;

        var installRoot = InstallerService.GetDefaultInstallRoot();

        try
        {
            await _installer.InstallAsync(
                installRoot,
                createDesktopShortcut: true,
                progress: (status, percent) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        InstallProgressBar.Value = percent;
                        TxtStatus.Text = status;
                    });
                },
                detailLog: null);

            _isCompleted = true;
            _isInstalling = false;

            TxtStatus.Text = "Installation complete! Click Launch to open.";
            InstallProgressBar.Value = 100;
            BtnInstall.Content = "Launch";
            BtnInstall.Visibility = Visibility.Visible;
            BtnInstall.IsEnabled = true;
            BtnCancel.Content = "Close";
            BtnCancel.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _isInstalling = false;
            TxtStatus.Text = $"Installation error: {ex.Message}";
            BtnInstall.Content = "Retry";
            BtnInstall.Visibility = Visibility.Visible;
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
