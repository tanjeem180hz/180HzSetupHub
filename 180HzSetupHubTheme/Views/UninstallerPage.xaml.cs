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
        private List<AppItem> _allApps = new();
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

            // Enrich all installed apps with exact registry details & session download history
            var completedHistory = DownloadManagerService.Instance.CompletedHistory;
            foreach (var app in _allApps)
            {
                AppMetadataHelper.EnrichAppItem(app, presetCatalog);

                var completedInSession = completedHistory.FirstOrDefault(h =>
                    string.Equals(h.App.Id, app.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h.App.Name, app.Name, StringComparison.OrdinalIgnoreCase));
                if (completedInSession != null)
                {
                    app.InstallDate = completedInSession.CompletedAt;
                }
            }

            InstalledCountText.Text = $"{_allApps.Count} Applications Installed";
            ApplyFilter();

            // Background async icon extraction & website discovery in parallel
            _ = Task.Run(async () =>
            {
                await Parallel.ForEachAsync(_allApps, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (app, ct) =>
                {
                    // If WebUrl is still missing, query winget metadata manifest in background
                    if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(app.Id) && !app.Id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
                    {
                        var meta = await _winget.GetMetadataAsync(app.Id);
                        if (meta?.BestLink != null)
                        {
                            app.WebUrl = meta.BestLink;
                            if (string.IsNullOrWhiteSpace(app.IconUrl))
                            {
                                app.IconUrl = IconCacheService.DeriveFaviconUrl(app.WebUrl) ?? "";
                            }
                        }
                    }

                    if (app.IconImageSource == null && (!string.IsNullOrWhiteSpace(app.IconUrl) || !string.IsNullOrWhiteSpace(app.LocalIconPath)))
                    {
                        var img = await IconCacheService.GetImageAsync(app.IconUrl, app.LocalIconPath);
                        if (img != null)
                        {
                            await Dispatcher.InvokeAsync(() => app.IconImageSource = img);
                        }
                    }
                });
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
                    ThemedMessageBox.Show($"Successfully exported {_allApps.Count} applications to:\n{dlg.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Export failed: {ex.Message}", ActivityType.Error);
                    ThemedMessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ApplyFilter(resetScroll: true);
        }

        private void ApplyFilter(bool resetScroll = false)
        {
            var query = FilterBox.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allApps
                : _allApps.Where(a =>
                    a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    a.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    a.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            // Default sort: Most recently downloaded / installed apps at the very top
            filtered = filtered
                .OrderByDescending(a => a.InstallDate ?? DateTime.MinValue)
                .ThenBy(a => a.Name)
                .ToList();

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

            if (resetScroll)
            {
                try
                {
                    var sv = SmoothScrollHelper.FindChildScrollViewer(AppsListBox);
                    if (sv != null)
                    {
                        SmoothScrollHelper.ScrollToTopImmediate(sv);
                    }
                    else if (AppsListBox.Items.Count > 0)
                    {
                        AppsListBox.ScrollIntoView(AppsListBox.Items[0]);
                    }
                }
                catch { }
            }
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                var wizard = new RevoUninstallWizardDialog(app)
                {
                    Owner = Window.GetWindow(this)
                };
                wizard.ShowDialog();

                if (wizard.IsUninstalled)
                {
                    ActivityLogger.Instance.Log($"Revo engine successfully removed {app.Name}.", ActivityType.Success);
                    _allApps.RemoveAll(a => a == app ||
                        (!string.IsNullOrWhiteSpace(a.Id) && string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(a.Name) && string.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase)));
                    ApplyFilter();
                    InstalledCountText.Text = $"{_allApps.Count} Applications Installed";

                    // Invalidate cached registry data and notify all components that this app is now uninstalled!
                    AppMetadataHelper.InvalidateCache();
                    PackageCatalogService.NotifyStatusChanged(app.Id, false);
                    PackageCatalogService.NotifyStatusChanged(app.Name, false);
                    UpdateMonitorService.Instance.UnmarkUpdated(app.Id);
                    UpdateMonitorService.Instance.UnmarkUpdated(app.Name);
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
