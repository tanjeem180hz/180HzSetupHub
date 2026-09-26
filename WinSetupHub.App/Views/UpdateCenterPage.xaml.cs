using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UpdateCenterPage : UserControl
    {
        private readonly WingetService _winget = new();

        public UpdateCenterPage()
        {
            InitializeComponent();
            UpdatesListBox.ItemsSource = UpdateMonitorService.Instance.UpgradableApps;
            UpdateMonitorService.Instance.UpgradableApps.CollectionChanged += (_, _) => UpdateSummary();

            Loaded += async (_, _) =>
            {
                UpdateSummary();
                if (UpdateMonitorService.Instance.UpgradableApps.Count == 0)
                {
                    await UpdateMonitorService.Instance.RefreshAsync(force: false);
                    UpdateSummary();
                }
            };
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
            SummaryText.Text = "Checking for updates via Winget…";
            UpgradeAllButton.IsEnabled = false;
            await UpdateMonitorService.Instance.RefreshAsync(force: true);
            UpdateSummary();
        }

        private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            var count = UpdateMonitorService.Instance.UpgradableApps.Count;
            UpgradeAllButton.IsEnabled = false;
            UpgradeAllButton.Content = "Upgrading All…";

            var success = await _winget.UpgradeAllAsync();

            ActivityLogger.Instance.Log(
                success ? "Upgraded all available applications." : "Upgrade-all finished with some warnings.",
                success ? ActivityType.Success : ActivityType.Warning);

            NotificationService.Notify(
                "Upgrade Complete",
                success ? $"{count} apps upgraded successfully." : "Upgrade-all finished with some warnings.");

            UpgradeAllButton.Content = "⚡ Upgrade All";
            await UpdateMonitorService.Instance.RefreshAsync(force: true);
            UpdateSummary();
        }

        private async void UpgradeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                btn.IsEnabled = false;
                btn.Content = "Upgrading…";

                var success = await _winget.UpgradeAsync(app.Id);

                btn.Content = success ? "Updated ✓" : "Failed";
                ActivityLogger.Instance.Log(
                    success ? $"Upgraded {app.Name} to {app.AvailableVersion}." : $"Failed to upgrade {app.Name}.",
                    success ? ActivityType.Success : ActivityType.Error);

                if (success)
                {
                    await UpdateMonitorService.Instance.RefreshAsync(force: true);
                    UpdateSummary();
                }
                else
                {
                    btn.IsEnabled = true;
                    btn.Content = "Retry";
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
