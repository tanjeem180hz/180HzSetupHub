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
        private readonly PackageCatalogService _catalogService = new();
        private List<AppItem>? _catalog;
        private List<AppItem> _upgradable = new();

        public UpdateCenterPage()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                if (_upgradable.Count == 0)
                {
                    await LoadAsync();
                }
            };
        }

        private async Task LoadAsync()
        {
            UpdatesListBox.ItemsSource = null;
            SummaryText.Text = "Checking for updates via Winget…";
            UpgradeAllButton.IsEnabled = false;

            _catalog ??= await _catalogService.GetAllAsync();

            _upgradable = await _winget.GetUpgradableAppsAsync();

            if (_upgradable.Count == 0)
            {
                SummaryText.Text = "🎉 Everything is up to date! No updates found.";
                return;
            }

            SummaryText.Text = $"🚀 {_upgradable.Count} application(s) have updates available.";
            UpgradeAllButton.IsEnabled = true;

            foreach (var app in _upgradable)
            {
                AppMetadataHelper.EnrichAppItem(app, _catalog);
            }

            UpdatesListBox.ItemsSource = _upgradable;

            // Background icon loading
            _ = Task.Run(async () =>
            {
                foreach (var app in _upgradable)
                {
                    if (app.IconImageSource == null)
                    {
                        var img = await IconCacheService.GetImageAsync(app.IconUrl, app.LocalIconPath);
                        if (img != null)
                        {
                            await Dispatcher.InvokeAsync(() => app.IconImageSource = img);
                        }
                    }
                }
            });
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            UpgradeAllButton.IsEnabled = false;
            UpgradeAllButton.Content = "Upgrading All…";

            var success = await _winget.UpgradeAllAsync();

            ActivityLogger.Instance.Log(
                success ? "Upgraded all available applications." : "Upgrade-all finished with some warnings.",
                success ? ActivityType.Success : ActivityType.Warning);

            NotificationService.Notify(
                "Upgrade Complete",
                success ? $"{_upgradable.Count} apps upgraded successfully." : "Upgrade-all finished with some warnings.");

            UpgradeAllButton.Content = "⚡ Upgrade All";
            await LoadAsync();
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

                if (!success)
                {
                    btn.IsEnabled = true;
                    btn.Content = "Retry";
                }
            }
        }
    }
}
