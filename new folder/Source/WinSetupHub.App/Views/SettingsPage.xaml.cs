using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class SettingsPage : UserControl
    {
        private bool _isInitializing = true;
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRunName = "180HzSetupHub";

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadSettings();

            AppUpdateService.Instance.StatusChanged += (_, _) =>
            {
                Dispatcher.InvokeAsync(UpdateAppUpdateStatusUi);
            };
        }

        private void LoadSettings()
        {
            _isInitializing = true;
            var settings = SettingsService.Instance.Current;

            ChkAutoStart.IsChecked = settings.AutoStartWithWindows;
            ChkNotifications.IsChecked = settings.ShowNotifications;
            ChkRequireAdmin.IsChecked = settings.RequireAdminForActions;
            ChkAutoCheckAppUpdates.IsChecked = settings.AutoCheckAppUpdates;

            // Select ComboBoxItem matching frequency (including 0 for Never)
            bool matched = false;
            foreach (ComboBoxItem item in CmbUpdateFrequency.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out var hours) && hours == settings.UpdateCheckFrequencyHours)
                {
                    CmbUpdateFrequency.SelectedItem = item;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                if (settings.UpdateCheckFrequencyHours == 0)
                {
                    CmbUpdateFrequency.SelectedIndex = 0; // Never
                }
                else
                {
                    CmbUpdateFrequency.SelectedIndex = 1; // Default: Every 6 hours
                }
            }

            UpdateAppUpdateStatusUi();
            _isInitializing = false;

            // Trigger background verification if auto-check is enabled and not checked recently
            if (settings.AutoCheckAppUpdates && (DateTime.UtcNow - AppUpdateService.Instance.LastCheckTime).TotalMinutes > 15)
            {
                _ = Task.Run(async () =>
                {
                    await AppUpdateService.Instance.CheckForUpdatesAsync(silent: true);
                });
            }
        }

        private void UpdateAppUpdateStatusUi()
        {
            var s = AppUpdateService.Instance;
            TxtSettingsAppVersion.Text = $"v{s.CurrentVersion}";
            TxtSettingsAppUpdateStatus.Text = s.StatusMessage;

            if (s.IsDownloading)
            {
                PnlSettingsDownloadProgress.Visibility = Visibility.Visible;
                PrgSettingsDownload.Value = s.DownloadProgress;
                TxtSettingsDownloadBytes.Text = string.IsNullOrWhiteSpace(s.DownloadBytesText) ? "Downloading 180HzSetupHub.exe from GitHub…" : s.DownloadBytesText;
                TxtSettingsDownloadSpeed.Text = s.DownloadSpeedText;

                BtnSettingsCheckUpdate.IsEnabled = false;
                BtnSettingsUpdateNow.IsEnabled = false;
                BtnSettingsForceUpdate.IsEnabled = false;
            }
            else
            {
                PnlSettingsDownloadProgress.Visibility = Visibility.Collapsed;
                BtnSettingsCheckUpdate.IsEnabled = !s.IsChecking;
                BtnSettingsForceUpdate.IsEnabled = true;

                if (s.IsChecking)
                {
                    BtnSettingsUpdateNow.IsEnabled = false;
                    BtnSettingsUpdateNow.Content = "Checking…";
                    PnlSettingsReleaseNotes.Visibility = Visibility.Collapsed;
                }
                else if (s.UpdateAvailable)
                {
                    BtnSettingsUpdateNow.IsEnabled = true;
                    BtnSettingsUpdateNow.Content = $"🚀 Update to v{s.LatestVersion}";

                    if (!string.IsNullOrWhiteSpace(s.ReleaseNotes))
                    {
                        TxtSettingsReleaseNotes.Text = s.ReleaseNotes;
                        PnlSettingsReleaseNotes.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PnlSettingsReleaseNotes.Visibility = Visibility.Collapsed;
                    }
                }
                else if (s.LastCheckTime > DateTime.MinValue)
                {
                    BtnSettingsUpdateNow.IsEnabled = false;
                    BtnSettingsUpdateNow.Content = "✓ Up to Date";
                    PnlSettingsReleaseNotes.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BtnSettingsUpdateNow.IsEnabled = false;
                    BtnSettingsUpdateNow.Content = "Ready to Check";
                    PnlSettingsReleaseNotes.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkAutoStart.IsChecked == true;
            SettingsService.Instance.Current.AutoStartWithWindows = enabled;
            SettingsService.Instance.Save();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    if (enabled)
                    {
                        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(exePath))
                        {
                            key.SetValue(AppRunName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppRunName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Could not update Windows startup registry: {ex.Message}", ActivityType.Warning);
            }

            ActivityLogger.Instance.Log(
                enabled ? "Enabled auto-start with Windows." : "Disabled auto-start with Windows.",
                ActivityType.Info);
        }

        private void ChkNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkNotifications.IsChecked == true;
            SettingsService.Instance.Current.ShowNotifications = enabled;
            SettingsService.Instance.Save();

            ActivityLogger.Instance.Log(
                enabled ? "Enabled Windows toast notifications." : "Disabled Windows toast notifications.",
                ActivityType.Info);
        }

        private void CmbUpdateFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (CmbUpdateFrequency.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out var hours))
            {
                SettingsService.Instance.Current.UpdateCheckFrequencyHours = hours;
                SettingsService.Instance.Save();

                if (hours == 0)
                {
                    ActivityLogger.Instance.Log("Automated update checks disabled (Never).", ActivityType.Info);
                }
                else
                {
                    ActivityLogger.Instance.Log($"Set automated update frequency to {hours} hours.", ActivityType.Info);
                }

                try
                {
                    UpdateMonitorService.Instance.UpdateInterval();
                    TaskSchedulerService.SyncScheduledTask();
                }
                catch { }
            }
        }

        private void ChkRequireAdmin_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkRequireAdmin.IsChecked == true;
            SettingsService.Instance.Current.RequireAdminForActions = enabled;
            SettingsService.Instance.Save();

            ActivityLogger.Instance.Log(
                enabled ? "Require Administrator privilege enabled." : "Require Administrator privilege disabled.",
                ActivityType.Info);
        }

        private void ChkAutoCheckAppUpdates_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkAutoCheckAppUpdates.IsChecked == true;
            SettingsService.Instance.Current.AutoCheckAppUpdates = enabled;
            SettingsService.Instance.Save();

            ActivityLogger.Instance.Log(
                enabled ? "Enabled auto-check for 180Hz Setup Hub updates on launch." : "Disabled auto-check for 180Hz Setup Hub updates on launch.",
                ActivityType.Info);

            if (enabled && (DateTime.UtcNow - AppUpdateService.Instance.LastCheckTime).TotalMinutes > 5)
            {
                _ = Task.Run(async () =>
                {
                    await AppUpdateService.Instance.CheckForUpdatesAsync(silent: true);
                });
            }
        }

        private async void BtnSettingsCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnSettingsCheckUpdate.IsEnabled = false;
                BtnSettingsUpdateNow.IsEnabled = false;
                BtnSettingsUpdateNow.Content = "Checking…";
                TxtSettingsAppUpdateStatus.Text = "Checking GitHub repository for updates…";

                await AppUpdateService.Instance.CheckForUpdatesAsync(silent: false);
                UpdateAppUpdateStatusUi();
            }
            catch (Exception ex)
            {
                TxtSettingsAppUpdateStatus.Text = $"Check failed: {ex.Message}";
            }
            finally
            {
                BtnSettingsCheckUpdate.IsEnabled = true;
            }
        }

        private async void BtnSettingsUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = ThemedMessageBox.Show(
                    $"180Hz Setup Hub will download the latest build (v{AppUpdateService.Instance.LatestVersion}) from GitHub and automatically replace the running executable and restart.\n\nDo you want to proceed?",
                    "Direct EXE Update from GitHub",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                await RunExeUpdate();
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Update error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSettingsForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = ThemedMessageBox.Show(
                    "This will force download the latest 180HzSetupHub.exe from GitHub (main branch) and replace the running executable in-place.\n\nThe app will restart automatically once completed.\n\nDo you want to continue?",
                    "Force Re-download Latest EXE",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                await RunExeUpdate();
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Update error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RunExeUpdate()
        {
            UpdateAppUpdateStatusUi();
            var success = await AppUpdateService.Instance.DownloadAndApplyUpdateAsync();
            if (!success)
            {
                UpdateAppUpdateStatusUi();
                ThemedMessageBox.Show(
                    $"Could not complete EXE update: {AppUpdateService.Instance.StatusMessage}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
