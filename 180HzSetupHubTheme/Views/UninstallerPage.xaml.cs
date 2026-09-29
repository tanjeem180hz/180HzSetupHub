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
                    Text = "📦",
                    FontSize = 18,
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary"),
                    Opacity = 0.5,
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

            // Fallback: If winget failed and we have a native registry UninstallString, execute it!
            if (!success && !string.IsNullOrWhiteSpace(appToUninstall.UninstallString))
            {
                ActivityLogger.Instance.Log($"Winget uninstallation did not succeed. Attempting native uninstaller for {appToUninstall.Name}…", ActivityType.Info);
                success = await RunNativeUninstallStringAsync(appToUninstall.UninstallString);
            }

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

                if (!App.IsRunningAsAdministrator())
                {
                    var result = MessageBox.Show(
                        $"Failed to uninstall {appToUninstall.Name}. Administrator privileges are required.\n\nWould you like to restart 180Hz Setup Hub as Administrator now?",
                        "Administrator Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (App.TryRestartAsAdministrator())
                        {
                            Application.Current.Shutdown();
                            return;
                        }
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to uninstall {appToUninstall.Name}.\n\nThe application may be currently running or requires its own uninstaller window.",
                        "Uninstall Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            _targetApp = null;
        }

        private static async Task<bool> RunNativeUninstallStringAsync(string uninstallString)
        {
            try
            {
                string raw = uninstallString.Trim();
                string fileName;
                string arguments = "";

                if (raw.StartsWith("\""))
                {
                    int closingQuote = raw.IndexOf('\"', 1);
                    if (closingQuote > 0)
                    {
                        fileName = raw.Substring(1, closingQuote - 1);
                        arguments = raw.Substring(closingQuote + 1).Trim();
                    }
                    else
                    {
                        fileName = raw.Trim('\"');
                    }
                }
                else
                {
                    int spaceIdx = raw.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        fileName = raw.Substring(0, spaceIdx);
                        arguments = raw.Substring(spaceIdx + 1).Trim();
                    }
                    else
                    {
                        fileName = raw;
                    }
                }

                // If MsiExec, make sure quiet or silent is handled
                if (fileName.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    arguments = arguments.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
                    if (!arguments.Contains("/X", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments = $"/X {arguments}";
                    }
                    if (!arguments.Contains("/qn", StringComparison.OrdinalIgnoreCase) && !arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments += " /quiet /norestart";
                    }
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    return proc.ExitCode == 0 || proc.ExitCode == 3010;
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Native uninstaller execution error: {ex.Message}", ActivityType.Warning);
            }
            return false;
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
