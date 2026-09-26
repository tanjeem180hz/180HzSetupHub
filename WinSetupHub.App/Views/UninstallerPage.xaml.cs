using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UninstallerPage : UserControl
    {
        private readonly WingetService _winget = new();
        private readonly PackageCatalogService _catalog = new();
        private readonly DeepUninstallService _deepUninstall = new();
        private List<AppItem> _allApps = new();
        private AppItem? _targetApp;
        private readonly DispatcherTimer _filterDebounceTimer;

        public UninstallerPage()
        {
            InitializeComponent();

            _filterDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _filterDebounceTimer.Tick += (_, _) =>
            {
                _filterDebounceTimer.Stop();
                ApplyFilter();
            };

            Loaded += async (_, _) =>
            {
                if (_allApps.Count == 0)
                {
                    await LoadAsync();
                }
            };
        }

        private async Task LoadAsync()
        {
            StatusText.Text = "Scanning installed applications…";
            StatusText.Visibility = Visibility.Visible;
            InstalledCountText.Text = "Scanning…";

            _allApps = await _winget.GetInstalledAppsAsync();
            var presetCatalog = await _catalog.GetAllAsync();

            // Enrich all installed apps with exact registry details
            foreach (var app in _allApps)
            {
                AppMetadataHelper.EnrichAppItem(app, presetCatalog);
            }

            InstalledCountText.Text = $"{_allApps.Count} Applications Installed";
            ApplyFilter();

            // Background async icon extraction for local apps
            _ = Task.Run(async () =>
            {
                foreach (var app in _allApps)
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

        private async void ExportList_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"180hz-app-list-{DateTime.Now:yyyyMMdd}.json",
                Title = "Export Installed Applications"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var exporter = new AppListExportService();
                    await exporter.ExportAsync(dlg.FileName, _allApps);
                    ActivityLogger.Instance.Log($"Exported {_allApps.Count} applications to {dlg.FileName}", ActivityType.Success);
                    NotificationService.Notify("Export Complete", $"Exported {_allApps.Count} installed apps to JSON.");
                    MessageBox.Show($"Successfully exported {_allApps.Count} applications to:\n{dlg.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Export failed: {ex.Message}", ActivityType.Error);
                    MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFilterButton.Visibility = string.IsNullOrWhiteSpace(FilterBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FilterBox.Text = string.Empty;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = FilterBox.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allApps
                : _allApps.Where(a =>
                    a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    a.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    a.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            if (filtered.Count == 0)
            {
                StatusText.Text = "No installed applications match your filter.";
                StatusText.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Visibility = Visibility.Collapsed;
            }

            AppsListBox.ItemsSource = filtered;
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                ShowUninstallWarning(app);
            }
        }

        private void ShowUninstallWarning(AppItem app)
        {
            _targetApp = app;

            DialogAppNameText.Text = app.Name;
            DialogVersionText.Text = app.FormattedVersion;
            DialogSizeText.Text = app.FormattedSize;
            DialogIdText.Text = app.Id;

            DialogLogoGrid.Children.Clear();

            if (app.IconImageSource != null)
            {
                var img = new Image
                {
                    Source = app.IconImageSource,
                    Width = 32,
                    Height = 32,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DialogLogoGrid.Children.Add(img);
            }
            else
            {
                var fallback = new TextBlock
                {
                    Text = app.Initial,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DialogLogoGrid.Children.Add(fallback);
            }

            WarningOverlay.Visibility = Visibility.Visible;
        }

        private void CancelUninstall_Click(object sender, RoutedEventArgs e)
        {
            WarningOverlay.Visibility = Visibility.Collapsed;
            _targetApp = null;
        }

        private async void ConfirmUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_targetApp == null) return;

            var appToUninstall = _targetApp;
            WarningOverlay.Visibility = Visibility.Collapsed;

            ConfirmUninstallButton.IsEnabled = false;

            ActivityLogger.Instance.Log($"Initiated uninstallation for {appToUninstall.Name}…", ActivityType.Info);

            var success = await _winget.UninstallAsync(appToUninstall.Id);
            ConfirmUninstallButton.IsEnabled = true;

            if (success)
            {
                ActivityLogger.Instance.Log($"Successfully uninstalled {appToUninstall.Name}.", ActivityType.Success);
                NotificationService.Notify("App Removed", $"{appToUninstall.Name} uninstalled successfully.");
                _allApps.Remove(appToUninstall);
                ApplyFilter();
                InstalledCountText.Text = $"{_allApps.Count} Applications Installed";

                // Feature C: Universal heuristic scan for leftover registry keys and folders
                try
                {
                    var leftovers = await _deepUninstall.ScanAsync(appToUninstall);
                    if (leftovers.Count > 0)
                    {
                        var dialog = new LeftoverCleanupDialog(appToUninstall, leftovers)
                        {
                            Owner = Window.GetWindow(this)
                        };
                        dialog.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Leftover scan warning: {ex.Message}", ActivityType.Warning);
                }
            }
            else
            {
                ActivityLogger.Instance.Log($"Failed to uninstall {appToUninstall.Name}.", ActivityType.Error);
                MessageBox.Show($"Failed to uninstall {appToUninstall.Name}. Please try running as Administrator.",
                    "Uninstall Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _targetApp = null;
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
