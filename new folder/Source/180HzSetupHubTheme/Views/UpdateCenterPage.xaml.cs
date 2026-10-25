using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UpdateCenterPage : UserControl, IRealtimeRefreshable
    {
        private readonly WingetService _winget = new();

        public void RefreshRealtime()
        {
            UpdateSummary();
            UpdateAppSelfUpdateUi();
        }

        public UpdateCenterPage()
        {
            InitializeComponent();
            UpdatesListBox.ItemsSource = UpdateMonitorService.Instance.UpgradableApps;
            UpdateMonitorService.Instance.UpgradableApps.CollectionChanged += (_, _) => UpdateSummary();

            AppUpdateService.Instance.StatusChanged += (_, _) =>
            {
                Dispatcher.InvokeAsync(UpdateAppSelfUpdateUi);
            };

            Loaded += async (_, _) =>
            {
                UpdateAppSelfUpdateUi();
                UpdateSummary();

                // Check for 180Hz Setup Hub GitHub updates in background if needed
                _ = Task.Run(async () =>
                {
                    if (SettingsService.Instance.Current.AutoCheckAppUpdates &&
                        (DateTime.UtcNow - AppUpdateService.Instance.LastCheckTime).TotalMinutes > 15)
                    {
                        await AppUpdateService.Instance.CheckForUpdatesAsync(silent: true);
                    }
                });

                if (UpdateMonitorService.Instance.UpgradableApps.Count == 0)
                {
                    await UpdateMonitorService.Instance.RefreshAsync(force: false);
                    UpdateSummary();
                }
            };
        }

        private void UpdateAppSelfUpdateUi()
        {
            var s = AppUpdateService.Instance;
            TxtAppCurrentVersion.Text = $"Installed: v{s.CurrentVersion}";
            TxtAppUpdateStatus.Text = s.StatusMessage;

            if (s.IsDownloading)
            {
                PnlAppDownloadProgress.Visibility = Visibility.Visible;
                PrgAppDownload.Value = s.DownloadProgress;
                TxtAppDownloadBytes.Text = string.IsNullOrWhiteSpace(s.DownloadBytesText) ? "Downloading update…" : s.DownloadBytesText;
                TxtAppDownloadSpeed.Text = s.DownloadSpeedText;

                BtnCheckAppUpdate.IsEnabled = false;
                BtnApplyAppUpdate.IsEnabled = false;
                BtnForceAppUpdate.IsEnabled = false;
            }
            else
            {
                PnlAppDownloadProgress.Visibility = Visibility.Collapsed;
                BtnCheckAppUpdate.IsEnabled = !s.IsChecking;
                BtnForceAppUpdate.IsEnabled = true;

                if (s.UpdateAvailable)
                {
                    BtnApplyAppUpdate.IsEnabled = true;
                    BtnApplyAppUpdate.Content = s.IsDeltaUpdate
                        ? $"⚡ Quick Patch ({s.UpdateSizeText})"
                        : $"🚀 Update to v{s.LatestVersion} ({s.UpdateSizeText})";
                    BtnApplyAppUpdate.Visibility = Visibility.Visible;

                    if (!string.IsNullOrWhiteSpace(s.ReleaseNotes))
                    {
                        TxtAppReleaseNotes.Text = s.ReleaseNotes;
                        PnlAppReleaseNotes.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PnlAppReleaseNotes.Visibility = Visibility.Collapsed;
                    }
                }
                else if (s.IsChecking)
                {
                    BtnApplyAppUpdate.IsEnabled = false;
                    BtnApplyAppUpdate.Content = "Checking…";
                    PnlAppReleaseNotes.Visibility = Visibility.Collapsed;
                }
                else if (s.LastCheckTime > DateTime.MinValue)
                {
                    BtnApplyAppUpdate.IsEnabled = false;
                    BtnApplyAppUpdate.Content = "✓ Up to Date";
                    PnlAppReleaseNotes.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BtnApplyAppUpdate.IsEnabled = false;
                    BtnApplyAppUpdate.Content = "Ready to Check";
                    PnlAppReleaseNotes.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void BtnCheckAppUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnCheckAppUpdate.IsEnabled = false;
                BtnApplyAppUpdate.IsEnabled = false;
                BtnApplyAppUpdate.Content = "Checking…";
                TxtAppUpdateStatus.Text = "Checking GitHub for updates…";

                await AppUpdateService.Instance.CheckForUpdatesAsync(silent: false);
                UpdateAppSelfUpdateUi();
            }
            catch (Exception ex)
            {
                TxtAppUpdateStatus.Text = $"Check failed: {ex.Message}";
            }
            finally
            {
                BtnCheckAppUpdate.IsEnabled = true;
            }
        }

        private async void BtnApplyAppUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = AppUpdateService.Instance;
                var modeText = s.IsDeltaUpdate ? "⚡ Fast Delta Patch (only changed files)" : "🚀 Full Application Update";
                var res = ThemedMessageBox.Show(
                    $"180Hz Setup Hub will now download and install v{s.LatestVersion} from GitHub.\n\n" +
                    $"• Mode: {modeText}\n" +
                    $"• Download Size: {s.UpdateSizeText}\n\n" +
                    "The application will automatically restart once the update is applied.\n\nDo you want to proceed?",
                    "Install 180Hz Setup Hub Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                await StartSelfUpdate(forceFullBinary: false);
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Update error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnForceAppUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = ThemedMessageBox.Show(
                    "This will force download the latest executable (~85 MB) from GitHub (main branch) and reinstall it, even if versions appear identical.\n\nThe application will restart automatically.\n\nDo you want to continue?",
                    "Force Sync from GitHub",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;

                await StartSelfUpdate(forceFullBinary: true);
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Update error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StartSelfUpdate(bool forceFullBinary = false)
        {
            UpdateAppSelfUpdateUi();
            var success = await AppUpdateService.Instance.DownloadAndApplyUpdateAsync(forceFullBinary: forceFullBinary);
            if (!success)
            {
                UpdateAppSelfUpdateUi();
                ThemedMessageBox.Show(
                    $"Could not complete application update: {AppUpdateService.Instance.StatusMessage}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            var count = UpdateMonitorService.Instance.UpgradableApps.Count;
            if (count == 0)
            {
                SummaryText.Text = "🎉 Everything is up to date! No updates found.";
                UpgradeAllButton.IsEnabled = false;
            }
            else
            {
                SummaryText.Text = $"🚀 {count} application(s) have updates available.";
                UpgradeAllButton.IsEnabled = true;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SummaryText.Text = "Checking for updates via Winget…";
                UpgradeAllButton.IsEnabled = false;
                await UpdateMonitorService.Instance.RefreshAsync(force: true);
                UpdateSummary();
            }
            catch (Exception ex)
            {
                SummaryText.Text = $"Update check notice: {ex.Message}";
            }
            finally
            {
                UpgradeAllButton.IsEnabled = UpdateMonitorService.Instance.UpgradableApps.Count > 0;
            }
        }

        private void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            var appsToUpgrade = new List<AppItem>(UpdateMonitorService.Instance.UpgradableApps);
            if (appsToUpgrade.Count == 0) return;

            UpdateMonitorService.Instance.UpgradableApps.Clear();

            DownloadManagerService.Instance.EnqueueRange(appsToUpgrade, isUpgrade: true);
            ActivityLogger.Instance.Log($"Enqueued {appsToUpgrade.Count} application updates to the download queue.", ActivityType.Info);
            NotificationService.Notify("Updates Enqueued", $"Enqueued {appsToUpgrade.Count} updates to Download Manager.");

            // Route to Downloads section so user sees real-time CDN speed, progress, ETA, and hero cards
            var mw = Window.GetWindow(this) as MainWindow;
            mw?.GoToPage("Downloads");
        }

        private void UpgradeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                UpdateMonitorService.Instance.UpgradableApps.Remove(app);
                DownloadManagerService.Instance.Enqueue(app, isUpgrade: true);
                ActivityLogger.Instance.Log($"Enqueued update for {app.Name} to the download queue.", ActivityType.Info);

                // Route to Downloads section
                var mw = Window.GetWindow(this) as MainWindow;
                mw?.GoToPage("Downloads");
            }
        }

        private async void WebLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                string url = app.EffectiveWebUrl;
                if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(app.Id))
                {
                    try
                    {
                        var meta = await _winget.GetMetadataAsync(app.Id);
                        if (!string.IsNullOrWhiteSpace(meta?.BestLink))
                        {
                            app.WebUrl = meta.BestLink;
                            url = meta.BestLink;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ThemedMessageBox.Show($"Could not open website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void OfficialLinkContainer_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ContentControl cc && cc.DataContext is AppItem app && cc.Content == null)
            {
                cc.Content = RowHelpers.BuildOfficialLinkButton(_winget, app);
            }
        }
    }
}
