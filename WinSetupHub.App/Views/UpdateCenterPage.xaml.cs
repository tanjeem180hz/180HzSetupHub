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

        private void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            var appsToUpgrade = new List<AppItem>(UpdateMonitorService.Instance.UpgradableApps);
            if (appsToUpgrade.Count == 0) return;

            foreach (var app in appsToUpgrade)
            {
                UpdateMonitorService.Instance.MarkAsUpdated(app.Id, app.Name);
            }

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
                UpdateMonitorService.Instance.MarkAsUpdated(app.Id, app.Name);
                DownloadManagerService.Instance.Enqueue(app, isUpgrade: true);
                ActivityLogger.Instance.Log($"Enqueued update for {app.Name} to the download queue.", ActivityType.Info);

                // Route to Downloads section
                var mw = Window.GetWindow(this) as MainWindow;
                mw?.GoToPage("Downloads");
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
