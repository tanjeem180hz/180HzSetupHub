using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class SetupAppsPage : UserControl
    {
        private readonly PackageCatalogService _catalog = new();
        private readonly WingetService _winget = new();
        private readonly BundleService _bundleService = new();
        private List<AppItem> _allPackages = new();
        private string _activeCategory = "All";
        private readonly DispatcherTimer _searchDebounceTimer;

        public SetupAppsPage()
        {
            InitializeComponent();

            BundlesItemsControl.ItemsSource = _bundleService.GetBundles();

            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                ApplyFilter();
            };

            Loaded += async (_, _) =>
            {
                if (_allPackages.Count == 0)
                {
                    await InitializeCatalogAsync();
                }
            };
        }

        private async Task InitializeCatalogAsync()
        {
            StatusText.Text = "Loading application catalog…";
            StatusText.Visibility = Visibility.Visible;

            _allPackages = await _catalog.GetAllAsync();
            CatalogCountText.Text = $"{_allPackages.Count} Packages Ready";

            BuildCategoryChips();
            ApplyFilter();

            // Background async icon fetching (immediate parallel) & installed status check (zero UI thread blocking)
            _ = Task.Run(async () =>
            {
                // 1. Fetch high-res icons in parallel immediately
                _ = Parallel.ForEachAsync(_allPackages, new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (pkg, ct) =>
                {
                    if (pkg.IconImageSource == null)
                    {
                        if (string.IsNullOrWhiteSpace(pkg.IconUrl) && !string.IsNullOrWhiteSpace(pkg.WebUrl))
                        {
                            pkg.IconUrl = IconCacheService.DeriveFaviconUrl(pkg.WebUrl) ?? "";
                        }

                        if (!string.IsNullOrWhiteSpace(pkg.IconUrl) || !string.IsNullOrWhiteSpace(pkg.LocalIconPath))
                        {
                            var img = await IconCacheService.GetImageAsync(pkg.IconUrl, pkg.LocalIconPath);
                            if (img != null)
                            {
                                await Dispatcher.InvokeAsync(() => pkg.IconImageSource = img);
                            }
                        }
                    }
                });

                // 2. Concurrently check installed status
                await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);
            });
        }

        private void BuildCategoryChips()
        {
            CategoryChipsPanel.Children.Clear();

            var categoryCounts = _allPackages
                .GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var categories = new List<(string Key, string Display, string Emoji)>
            {
                ("All", "All Apps", "🌟"),
                ("Microsoft Store", "Microsoft Store", "🛍️"),
                ("Browsers", "Browsers & Web", "🌐"),
                ("Developer", "Developer Tools", "💻"),
                ("Gaming", "Gaming Tools", "🎮"),
                ("Productivity", "Office & Productivity", "💼"),
                ("Communication", "Communication & Chat", "💬"),
                ("Design & Creative", "Design & Creative", "🎨"),
                ("Media & Streaming", "Media & Audio", "🎬"),
                ("Video Editing", "Video Editing", "📹"),
                ("Security", "Security & Privacy", "🔒"),
                ("Utilities", "Utilities & System", "🛠️"),
                ("Databases", "Databases", "🗄️"),
                ("Networking", "Networking & Cloud", "🌐"),
                ("Drivers", "System Drivers", "⚡"),
                ("Virtualization", "Virtualization", "📦"),
                ("Game Development", "Game Dev", "🕹️"),
                ("Problem Solving", "Diagnostics", "🧩"),
                ("Printer Drivers", "Printer Drivers", "🖨️")
            };

            foreach (var (key, display, emoji) in categories)
            {
                int count = key == "All"
                    ? _allPackages.Count
                    : (categoryCounts.TryGetValue(key, out var c) ? c : 0);

                if (count == 0 && key != "All") continue;

                var chip = new RadioButton
                {
                    Style = (Style)FindResource("CategoryChip"),
                    Content = $"{emoji}  {display} ({count})",
                    Tag = key,
                    IsChecked = key == "All"
                };

                chip.Checked += (_, _) =>
                {
                    _activeCategory = key;
                    ApplyFilter();
                };

                CategoryChipsPanel.Children.Add(chip);
            }
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text.Trim();
            var filtered = _allPackages.AsEnumerable();

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(p => string.Equals(p.Category, _activeCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var results = filtered.ToList();

            if (results.Count == 0)
            {
                StatusText.Text = "No packages match your search or category filter.";
                StatusText.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Visibility = Visibility.Collapsed;
            }

            AppsListBox.ItemsSource = results;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            ApplyFilter();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _searchDebounceTimer.Stop();
                ApplyFilter();
            }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
        }

        private async void WingetSearch_Click(object sender, RoutedEventArgs e)
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please enter a software name in the search box to search Winget's online repository.",
                    "Search Query Required", MessageBoxButton.OK, MessageBoxImage.Information);
                SearchBox.Focus();
                return;
            }

            StatusText.Text = $"Searching online Winget repository for '{query}'…";
            StatusText.Visibility = Visibility.Visible;
            WingetSearchButton.IsEnabled = false;

            try
            {
                var onlineResults = await _winget.SearchAsync(query);
                if (onlineResults.Count == 0)
                {
                    StatusText.Text = $"No online packages found matching '{query}'.";
                }
                else
                {
                    StatusText.Visibility = Visibility.Collapsed;
                    foreach (var app in onlineResults)
                    {
                        AppMetadataHelper.EnrichAppItem(app, _allPackages);
                    }
                    AppsListBox.ItemsSource = onlineResults;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Online search error: {ex.Message}";
            }
            finally
            {
                WingetSearchButton.IsEnabled = true;
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                if (app.IsBusy || app.IsInstalled) return;

                app.IsBusy = true;
                app.Status = "Installing…";

                // Special handling for Microsoft Store app itself
                if (app.Id.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("ms-windows-store://") { UseShellExecute = true });
                        app.Status = "Opened";
                        app.IsInstalled = true;
                        ActivityLogger.Instance.Log("Launched Microsoft Store.", ActivityType.Success);
                        return;
                    }
                    catch { }
                }

                var success = await _winget.InstallAsync(app.Id, app.Source, line =>
                {
                    if (line.Contains('%'))
                    {
                        Dispatcher.Invoke(() => app.Status = "Installing…");
                    }
                });

                app.IsBusy = false;
                if (success)
                {
                    app.IsInstalled = true;
                    app.Status = "Installed";
                    ActivityLogger.Instance.Log($"Successfully installed {app.Name}.", ActivityType.Success);
                }
                else
                {
                    app.Status = "Failed";
                    ActivityLogger.Instance.Log($"Installation failed for {app.Name}.", ActivityType.Error);
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

        private void WebLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app && !string.IsNullOrWhiteSpace(app.WebUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(app.WebUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async void BundleInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppBundle bundle)
            {
                if (bundle.IsInstalling) return;

                bundle.IsInstalling = true;
                bundle.ButtonContent = "⏳ Installing…";
                int total = bundle.WingetIds.Count;
                int ok = 0;

                ActivityLogger.Instance.Log($"Starting bundle deployment: {bundle.Name} ({total} apps)", ActivityType.Info);

                for (int i = 0; i < total; i++)
                {
                    var id = bundle.WingetIds[i];
                    bundle.ProgressText = $"Installing {i + 1} of {total}: {id}…";

                    ActivityLogger.Instance.Log($"Installing [{i + 1}/{total}] {id} from bundle '{bundle.Name}'…", ActivityType.Info);

                    bool success = await _winget.InstallAsync(id);
                    if (success)
                    {
                        ok++;
                        ActivityLogger.Instance.Log($"Successfully installed {id}.", ActivityType.Success);
                    }
                    else
                    {
                        ActivityLogger.Instance.Log($"Failed to install {id}.", ActivityType.Error);
                    }
                }

                bundle.ProgressText = $"Completed: {ok}/{total} installed";
                bundle.ButtonContent = "⚡ Install All";
                bundle.IsInstalling = false;

                ActivityLogger.Instance.Log($"Bundle '{bundle.Name}' finished: {ok}/{total} succeeded.", ActivityType.Success);
                NotificationService.Notify("Bundle installed", $"{bundle.Name}: {ok}/{total} succeeded");
            }
        }

        private async void ImportInstall_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = "Import Software List"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var exporter = new AppListExportService();
                    var importedApps = await exporter.ImportAsync(dlg.FileName);
                    if (importedApps.Count == 0)
                    {
                        MessageBox.Show("No valid applications found in JSON file.", "Import Applications", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var confirm = MessageBox.Show($"Found {importedApps.Count} applications in list.\n\nDo you want to sequentially install missing packages now?", "Confirm Import & Install", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;

                    StatusText.Text = $"Installing imported packages (0/{importedApps.Count})…";
                    StatusText.Visibility = Visibility.Visible;

                    int ok = 0;
                    int total = importedApps.Count;
                    ActivityLogger.Instance.Log($"Starting imported list installation ({total} packages)…", ActivityType.Info);

                    for (int i = 0; i < total; i++)
                    {
                        var app = importedApps[i];
                        StatusText.Text = $"Installing imported app {i + 1}/{total}: {app.Name}…";
                        ActivityLogger.Instance.Log($"Import installing [{i + 1}/{total}]: {app.Name} ({app.Id})", ActivityType.Info);

                        bool success = await _winget.InstallAsync(app.Id);
                        if (success)
                        {
                            ok++;
                            ActivityLogger.Instance.Log($"Successfully installed {app.Name}.", ActivityType.Success);
                        }
                        else
                        {
                            ActivityLogger.Instance.Log($"Failed installing {app.Name}.", ActivityType.Error);
                        }
                    }

                    StatusText.Visibility = Visibility.Collapsed;
                    ActivityLogger.Instance.Log($"Import installation finished: {ok}/{total} succeeded.", ActivityType.Success);
                    NotificationService.Notify("Import & Install Complete", $"{ok}/{total} packages installed successfully.");
                    MessageBox.Show($"Import & Install completed!\n{ok} of {total} packages succeeded.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Import error: {ex.Message}", ActivityType.Error);
                    MessageBox.Show($"Failed to import file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
