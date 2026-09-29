using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private string _currentGhostSuggestion = "";
        private bool _isSelectingFromPopup = false;
        private System.Threading.CancellationTokenSource? _onlineSearchCts;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<AppItem>> _onlineSuggestCache =
            new(StringComparer.OrdinalIgnoreCase);

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

            DownloadManagerService.Instance.ProgressChanged += OnDownloadProgressChanged;
            DownloadManagerService.Instance.QueueChanged += OnDownloadQueueChanged;
            DownloadManagerService.Instance.QueueCompleted += OnDownloadQueueCompleted;

            Loaded += async (_, _) =>
            {
                if (_allPackages.Count == 0)
                {
                    await InitializeCatalogAsync();
                }
            };
        }

        private static readonly string[] TopBasicDailyApps = new[]
        {
            // Top Browsers
            "Google.Chrome",
            "Mozilla.Firefox",
            "Brave.Brave",
            "Microsoft.Edge",
            "Opera.Opera",

            // Top Daily Utilities & Archivers
            "7zip.7zip",
            "RARLab.WinRAR",
            "voidtools.Everything",
            "Notepad++.Notepad++",

            // Top Media & Audio
            "VideoLAN.VLC",
            "Spotify.Spotify",

            // Top Communication
            "Discord.Discord",
            "Telegram.TelegramDesktop",
            "9NKSQGP7F2NH", // WhatsApp Desktop (Microsoft Store)
            "WhatsApp.WhatsApp",
            "Zoom.Zoom",

            // Top Developer Tools
            "Microsoft.VisualStudioCode",
            "Git.Git",
            "OpenJS.NodeJS",
            "Python.Python.3.13",

            // Top Gaming & System Launchers
            "Valve.Steam",
            "EpicGames.EpicGamesLauncher",
            "Microsoft.PowerToys",
            "Microsoft.WindowsTerminal",
            "Microsoft.WindowsStore"
        };

        private static readonly Dictionary<string, int> TopBasicRankMap =
            TopBasicDailyApps
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

        private static int GetAppSortRank(AppItem app)
        {
            if (TopBasicRankMap.TryGetValue(app.Id, out int rank))
            {
                return rank;
            }
            if (app.Essential)
            {
                return 100;
            }
            return 1000;
        }

        private async Task InitializeCatalogAsync()
        {
            try
            {
                StatusText.Text = "Loading application catalog…";
                StatusText.Visibility = Visibility.Visible;

                var rawPackages = await _catalog.GetAllAsync();
                _allPackages = rawPackages
                    .OrderBy(GetAppSortRank)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                CatalogCountText.Text = $"{_allPackages.Count} Packages Ready";

                BuildCategoryChips();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading catalog: {ex.Message}";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

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

            var results = filtered
                .OrderBy(GetAppSortRank)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CatalogCountText.Text = $"{results.Count} Packages Ready";

            if (results.Count == 0)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(query)
                    ? "No packages match your category filter."
                    : $"No local packages found for '{query}'. Click '🌐 Deep Web Search' to find it online.";
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
            if (_isSelectingFromPopup) return;

            var text = SearchBox.Text;
            bool hasText = !string.IsNullOrWhiteSpace(text);

            ClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

            if (!hasText)
            {
                _currentGhostSuggestion = "";
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                SearchSuggestionsPopup.IsOpen = false;
                _onlineSearchCts?.Cancel();
            }
            else
            {
                var query = text.Trim();
                UpdateSuggestionsAndGhost(query);

                // Cancel prior online suggestion search and trigger new debounced Winget repository search
                _onlineSearchCts?.Cancel();
                _onlineSearchCts?.Dispose();
                _onlineSearchCts = new System.Threading.CancellationTokenSource();
                _ = SearchOnlineSuggestionsDebouncedAsync(query, _onlineSearchCts.Token);
            }

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void UpdateSuggestionsAndGhost(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _currentGhostSuggestion = "";
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                SearchSuggestionsPopup.IsOpen = false;
                return;
            }

            // Find matching apps from _allPackages (preset catalog, 0ms instant)
            var matches = _allPackages
                .Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (p.DetectionNames != null && p.DetectionNames.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase))))
                .OrderBy(p =>
                {
                    // Exact prefix of app name gets highest priority
                    if (p.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
                    // Any word starting with prefix
                    var words = p.Name.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Any(w => w.StartsWith(query, StringComparison.OrdinalIgnoreCase))) return 1;
                    // Id prefix
                    if (p.Id.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
                    return 3;
                })
                .ThenBy(GetAppSortRank)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(7)
                .ToList();

            var topMatch = matches.FirstOrDefault();
            if (topMatch != null)
            {
                _currentGhostSuggestion = topMatch.Name;

                if (topMatch.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    SearchGhostText.Text = query + topMatch.Name.Substring(query.Length);
                }
                else
                {
                    SearchGhostText.Text = "";
                }
                TabHintBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _currentGhostSuggestion = "";
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
            }

            SuggestionsListBox.ItemsSource = matches;
            PopupDeepSearchText.Text = $"Deep Web Search for '{query}'";
            SearchSuggestionsPopup.IsOpen = matches.Count > 0 || !string.IsNullOrWhiteSpace(query);
        }

        private async Task SearchOnlineSuggestionsDebouncedAsync(string query, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return;

            try
            {
                // 250ms debounce before invoking winget CLI
                await Task.Delay(250, ct);
                if (ct.IsCancellationRequested) return;

                List<AppItem>? onlineList;
                if (!_onlineSuggestCache.TryGetValue(query, out onlineList))
                {
                    onlineList = await _winget.SearchAsync(query);
                    if (onlineList != null)
                    {
                        _onlineSuggestCache[query] = onlineList;
                    }
                }

                if (ct.IsCancellationRequested || onlineList == null || onlineList.Count == 0) return;

                // Take top 6 online packages and enrich them
                var topOnline = onlineList.Take(6).ToList();
                foreach (var app in topOnline)
                {
                    AppMetadataHelper.EnrichAppItem(app, _allPackages);
                    if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General")
                    {
                        app.Category = "🌐 Winget Repository";
                    }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (!string.Equals(SearchBox.Text.Trim(), query, StringComparison.OrdinalIgnoreCase)) return;

                    var currentList = (SuggestionsListBox.ItemsSource as IEnumerable<AppItem>)?.ToList() ?? new List<AppItem>();

                    // Merge online packages that aren't already present in currentList
                    bool added = false;
                    foreach (var onlineApp in topOnline)
                    {
                        if (!currentList.Any(m => string.Equals(m.Id, onlineApp.Id, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(m.Name, onlineApp.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentList.Add(onlineApp);
                            added = true;
                        }
                    }

                    if (added || currentList.Count > 0)
                    {
                        // If ghost text was empty (i.e. not in preset catalog), adopt top online package!
                        if (string.IsNullOrWhiteSpace(_currentGhostSuggestion) && currentList.Count > 0)
                        {
                            var top = currentList[0];
                            _currentGhostSuggestion = top.Name;
                            if (top.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                            {
                                SearchGhostText.Text = query + top.Name.Substring(query.Length);
                            }
                            TabHintBadge.Visibility = Visibility.Visible;
                        }

                        SuggestionsListBox.ItemsSource = null;
                        SuggestionsListBox.ItemsSource = currentList;
                        SearchSuggestionsPopup.IsOpen = true;

                        // Load icons for online items
                        _ = Task.Run(async () =>
                        {
                            foreach (var item in topOnline)
                            {
                                if (item.IconImageSource == null && (!string.IsNullOrWhiteSpace(item.IconUrl) || !string.IsNullOrWhiteSpace(item.LocalIconPath)))
                                {
                                    var img = await IconCacheService.GetImageAsync(item.IconUrl, item.LocalIconPath);
                                    if (img != null)
                                    {
                                        await Dispatcher.InvokeAsync(() => item.IconImageSource = img);
                                    }
                                }
                            }
                        });
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch { /* Ignore background suggest errors */ }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && !string.IsNullOrWhiteSpace(_currentGhostSuggestion))
            {
                ApplyGhostSuggestion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && SearchSuggestionsPopup.IsOpen && SuggestionsListBox.Items.Count > 0)
            {
                SuggestionsListBox.Focus();
                SuggestionsListBox.SelectedIndex = 0;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                SearchSuggestionsPopup.IsOpen = false;
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                SearchSuggestionsPopup.IsOpen = false;
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                _searchDebounceTimer.Stop();
                ApplyFilter();

                // If no local packages found for the typed query, automatically run Deep Web Search!
                var currentItems = AppsListBox.ItemsSource as IEnumerable<AppItem>;
                if (currentItems == null || !currentItems.Any())
                {
                    WingetSearch_Click(sender, e);
                }
                e.Handled = true;
                return;
            }
        }

        private void ApplyGhostSuggestion()
        {
            if (string.IsNullOrWhiteSpace(_currentGhostSuggestion)) return;

            _isSelectingFromPopup = true;
            try
            {
                SearchBox.Text = _currentGhostSuggestion;
                SearchBox.CaretIndex = SearchBox.Text.Length;
                _currentGhostSuggestion = "";
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                SearchSuggestionsPopup.IsOpen = false;
                _searchDebounceTimer.Stop();

                // Check if matching in local catalog
                var match = _allPackages.FirstOrDefault(p =>
                    string.Equals(p.Name, SearchBox.Text, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.StartsWith(SearchBox.Text, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    ApplyFilter();
                }
                else
                {
                    // It's an online package from Winget! Auto-trigger online search/display
                    WingetSearch_Click(this, new RoutedEventArgs());
                }
            }
            finally
            {
                _isSelectingFromPopup = false;
            }
        }

        private void TabHintBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ApplyGhostSuggestion();
            SearchBox.Focus();
        }

        private void SuggestionsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab || e.Key == Key.Enter)
            {
                if (SuggestionsListBox.SelectedItem is AppItem selected)
                {
                    _currentGhostSuggestion = selected.Name;
                    ApplyGhostSuggestion();
                    SearchBox.Focus();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Up && SuggestionsListBox.SelectedIndex <= 0)
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                SearchSuggestionsPopup.IsOpen = false;
                SearchBox.Focus();
                e.Handled = true;
                return;
            }
        }

        private void SuggestionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SuggestionsListBox.SelectedItem is AppItem selected)
            {
                _isSelectingFromPopup = true;
                try
                {
                    SearchBox.Text = selected.Name;
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                    _currentGhostSuggestion = "";
                    SearchGhostText.Text = "";
                    TabHintBadge.Visibility = Visibility.Collapsed;
                    SearchSuggestionsPopup.IsOpen = false;
                    _searchDebounceTimer.Stop();

                    // If selected is already in _allPackages:
                    if (_allPackages.Any(p => string.Equals(p.Id, selected.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        ApplyFilter();
                    }
                    else
                    {
                        // It's an online Winget package! Display it directly in the main list ready to install!
                        DisplaySingleOrOnlinePackage(selected);
                    }
                }
                finally
                {
                    _isSelectingFromPopup = false;
                }
            }
        }

        private async void DisplaySingleOrOnlinePackage(AppItem app)
        {
            AppMetadataHelper.EnrichAppItem(app, _allPackages);
            var list = new List<AppItem> { app };
            await _catalog.CheckInstalledStatusAsync(_winget, list);

            CatalogCountText.Text = $"1 Online Package Ready for '{app.Name}'";
            StatusText.Visibility = Visibility.Collapsed;
            AppsListBox.ItemsSource = list;

            // Load icon
            if (app.IconImageSource == null && (!string.IsNullOrWhiteSpace(app.IconUrl) || !string.IsNullOrWhiteSpace(app.LocalIconPath)))
            {
                var img = await IconCacheService.GetImageAsync(app.IconUrl, app.LocalIconPath);
                if (img != null)
                {
                    app.IconImageSource = img;
                }
            }
        }

        private void PopupDeepWebSearch_Click(object sender, MouseButtonEventArgs e)
        {
            SearchSuggestionsPopup.IsOpen = false;
            SearchGhostText.Text = "";
            TabHintBadge.Visibility = Visibility.Collapsed;
            WingetSearch_Click(sender, e);
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            _currentGhostSuggestion = "";
            SearchGhostText.Text = "";
            TabHintBadge.Visibility = Visibility.Collapsed;
            SearchSuggestionsPopup.IsOpen = false;
            ApplyFilter();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SearchSuggestionsPopup.IsOpen = false;
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                _searchDebounceTimer.Stop();
                ApplyFilter();

                // If no local packages found for the typed query, automatically run Deep Web Search!
                var currentItems = AppsListBox.ItemsSource as IEnumerable<AppItem>;
                if (currentItems == null || !currentItems.Any())
                {
                    WingetSearch_Click(sender, e);
                }
                e.Handled = true;
                return;
            }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            SearchSuggestionsPopup.IsOpen = false;
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

            SearchSuggestionsPopup.IsOpen = false;
            SearchGhostText.Text = "";
            TabHintBadge.Visibility = Visibility.Collapsed;

            StatusText.Text = $"Searching online Winget repository for '{query}'…";
            StatusText.Visibility = Visibility.Visible;
            WingetSearchButton.IsEnabled = false;

            try
            {
                var onlineResults = await _winget.SearchAsync(query);
                if (onlineResults.Count == 0)
                {
                    StatusText.Text = $"No online packages found matching '{query}'.";
                    StatusText.Visibility = Visibility.Visible;
                }
                else
                {
                    StatusText.Visibility = Visibility.Collapsed;

                    // 1. Enrich metadata & match with local catalog
                    foreach (var app in onlineResults)
                    {
                        AppMetadataHelper.EnrichAppItem(app, _allPackages);
                    }

                    // 2. Check installed status
                    await _catalog.CheckInstalledStatusAsync(_winget, onlineResults);

                    // 3. Update count & list view
                    CatalogCountText.Text = $"{onlineResults.Count} Online Packages Found for '{query}'";
                    AppsListBox.ItemsSource = onlineResults;

                    // 4. Background parallel icon extraction & website discovery
                    _ = Task.Run(async () =>
                    {
                        await Parallel.ForEachAsync(onlineResults, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (app, ct) =>
                        {
                            if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(app.Id))
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
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Online search error: {ex.Message}";
                StatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                WingetSearchButton.IsEnabled = true;
            }
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                if (app.IsInstalled) return;

                var dm = DownloadManagerService.Instance;
                if (dm.CurrentApp == app)
                {
                    if (dm.IsPaused)
                    {
                        dm.Resume();
                    }
                    else
                    {
                        dm.Pause();
                    }
                    return;
                }

                if (dm.RemainingQueue.Contains(app))
                {
                    dm.MoveToTop(app);
                    return;
                }

                if (app.IsBusy) return;

                StartSequentialQueue(new[] { app });
            }
        }

        private void AppCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectionUI();
        }

        private void UpdateSelectionUI()
        {
            var selected = _allPackages.Where(p => p.IsSelected).ToList();
            int count = selected.Count;

            if (count > 0)
            {
                BatchActionBar.Visibility = Visibility.Visible;
                SelectedAppsCountText.Text = $"{count} app{(count > 1 ? "s" : "")} selected";
                InstallSelectedButton.Content = $"⚡ Install Selected ({count})";
            }
            else
            {
                BatchActionBar.Visibility = Visibility.Collapsed;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (AppsListBox.ItemsSource is IEnumerable<AppItem> currentItems)
            {
                foreach (var item in currentItems)
                {
                    if (!item.IsInstalled) item.IsSelected = true;
                }
                UpdateSelectionUI();
            }
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allPackages)
            {
                item.IsSelected = false;
            }
            UpdateSelectionUI();
        }

        private void InstallSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allPackages.Where(p => p.IsSelected && !p.IsInstalled).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one uninstalled app to install.", "No Apps Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StartSequentialQueue(selected);
        }

        private void StartSequentialQueue(IEnumerable<AppItem> apps)
        {
            var list = apps.ToList();
            if (list.Count == 0) return;

            DownloadPopupPanel.Visibility = Visibility.Visible;
            AppsListBox.Margin = new Thickness(0, 0, 0, 110);

            PopupPauseResumeButton.Visibility = Visibility.Visible;
            PopupPauseResumeButton.Content = "⏸ Pause";
            PopupPauseResumeButton.IsEnabled = true;
            PopupSkipButton.Visibility = Visibility.Visible;
            PopupCancelButton.Visibility = Visibility.Visible;
            PopupDismissButton.Visibility = Visibility.Collapsed;

            DownloadManagerService.Instance.EnqueueRange(list);
            UpdateSelectionUI();
        }

        private void OnDownloadProgressChanged(DownloadProgressInfo info)
        {
            Dispatcher.InvokeAsync(() =>
            {
                DownloadPopupPanel.Visibility = Visibility.Visible;
                AppsListBox.Margin = new Thickness(0, 0, 0, 110);

                PopupAppName.Text = info.App.Name;
                PopupAppIcon.Source = info.App.IconImageSource;
                PopupQueueText.Text = $"App {info.QueueIndex} of {info.QueueTotal}";
                PopupProgressBar.Value = info.Percentage;

                PopupPercentageText.Text = $"{info.Percentage:0}%";
                PopupSpeedText.Text = info.SpeedFormatted;
                PopupEtaText.Text = info.EtaFormatted;
                PopupSizeText.Text = info.SizeFormatted;
                PopupStatusDetail.Text = info.StatusMessage;

                switch (info.State)
                {
                    case DownloadState.Downloading:
                        PopupStateText.Text = "DOWNLOADING";
                        PopupStateBadge.Background = (SolidColorBrush)FindResource("BrushSurface");
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushAccent");
                        PopupPauseResumeButton.Content = "⏸ Pause";
                        PopupPauseResumeButton.IsEnabled = true;
                        break;

                    case DownloadState.Paused:
                        PopupStateText.Text = "PAUSED";
                        PopupStateBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 40, 10));
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                        PopupPauseResumeButton.Content = "▶ Resume";
                        PopupPauseResumeButton.IsEnabled = true;
                        break;

                    case DownloadState.Installing:
                        PopupStateText.Text = "INSTALLING";
                        PopupStateBadge.Background = (SolidColorBrush)FindResource("BrushSurface");
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushAccent");
                        PopupPauseResumeButton.IsEnabled = false;
                        break;

                    case DownloadState.Error:
                        PopupStateText.Text = "CONNECTION ERROR";
                        PopupStateBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 18, 25));
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushError");
                        PopupPauseResumeButton.Content = "🔄 Resume";
                        PopupPauseResumeButton.IsEnabled = true;
                        break;

                    case DownloadState.Completed:
                        PopupStateText.Text = "INSTALLED";
                        PopupStateBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 55, 25));
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                        break;
                }

                UpdateSelectionUI();
            });
        }

        private void OnDownloadQueueCompleted()
        {
            Dispatcher.InvokeAsync(() =>
            {
                PopupStateText.Text = "COMPLETED";
                PopupStateBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 55, 25));
                PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                PopupStatusDetail.Text = "All operations completed successfully! 🎉";
                PopupProgressBar.Value = 100;
                PopupPercentageText.Text = "100%";
                PopupSpeedText.Text = "Done";
                PopupEtaText.Text = "Complete";

                PopupPauseResumeButton.Visibility = Visibility.Collapsed;
                PopupSkipButton.Visibility = Visibility.Collapsed;
                PopupCancelButton.Visibility = Visibility.Collapsed;
                PopupDismissButton.Visibility = Visibility.Visible;

                NotificationService.Notify("Setup Complete", "All queued software deployments have finished.");
                PopupToggleQueueButton.Visibility = Visibility.Collapsed;
                PopupQueueDrawer.Visibility = Visibility.Collapsed;
                UpdateSelectionUI();
            });
        }

        private void OnDownloadQueueChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var dm = DownloadManagerService.Instance;
                var remaining = dm.RemainingQueue;
                PopupToggleQueueButton.Content = $"📋 Queue ({remaining.Count}) {(PopupQueueDrawer.Visibility == Visibility.Visible ? "▴" : "▾")}";
                PopupQueueItemsControl.ItemsSource = remaining;
                PopupNoRemainingQueueText.Visibility = remaining.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateSelectionUI();
            });
        }

        private void PopupToggleQueue_Click(object sender, RoutedEventArgs e)
        {
            var dm = DownloadManagerService.Instance;
            var remaining = dm.RemainingQueue;
            if (PopupQueueDrawer.Visibility == Visibility.Visible)
            {
                PopupQueueDrawer.Visibility = Visibility.Collapsed;
                PopupToggleQueueButton.Content = $"📋 Queue ({remaining.Count}) ▾";
            }
            else
            {
                PopupQueueDrawer.Visibility = Visibility.Visible;
                PopupToggleQueueButton.Content = $"📋 Queue ({remaining.Count}) ▴";
                PopupQueueItemsControl.ItemsSource = remaining;
                PopupNoRemainingQueueText.Visibility = remaining.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void PopupViewDownloads_Click(object sender, RoutedEventArgs e)
        {
            var mw = Window.GetWindow(this) as MainWindow;
            mw?.GoToPage("Downloads");
        }

        private void PopupQueueMoveToTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                DownloadManagerService.Instance.MoveToTop(app);
            }
        }

        private void PopupQueueRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                DownloadManagerService.Instance.RemoveFromQueue(app);
            }
        }

        private void PopupPauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadManagerService.Instance.IsPaused)
            {
                DownloadManagerService.Instance.Resume();
            }
            else
            {
                DownloadManagerService.Instance.Pause();
            }
        }

        private void PopupSkip_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.SkipCurrent();
        }

        private void PopupCancel_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.CancelAll();
            DownloadPopupPanel.Visibility = Visibility.Collapsed;
            AppsListBox.Margin = new Thickness(0);
            UpdateSelectionUI();
        }

        private void PopupDismiss_Click(object sender, RoutedEventArgs e)
        {
            DownloadPopupPanel.Visibility = Visibility.Collapsed;
            AppsListBox.Margin = new Thickness(0);
        }

        private void CategoryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void BundlesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private Point _categoryDragStartPoint;
        private double _categoryDragStartOffset;
        private bool _isCategoryDragging;

        private void CategoryScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer sv && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
            {
                _categoryDragStartPoint = e.GetPosition(sv);
                _categoryDragStartOffset = sv.HorizontalOffset;
                _isCategoryDragging = true;
            }
        }

        private void CategoryScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isCategoryDragging && sender is ScrollViewer sv)
            {
                var current = e.GetPosition(sv);
                var delta = _categoryDragStartPoint.X - current.X;
                if (Math.Abs(delta) > 3)
                {
                    sv.CaptureMouse();
                    sv.ScrollToHorizontalOffset(_categoryDragStartOffset + delta);
                }
            }
        }

        private void CategoryScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isCategoryDragging && sender is ScrollViewer sv)
            {
                _isCategoryDragging = false;
                sv.ReleaseMouseCapture();
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

        private void BundleInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppBundle bundle)
            {
                var targetApps = new List<AppItem>();
                foreach (var id in bundle.WingetIds)
                {
                    var app = _allPackages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (app == null)
                    {
                        app = new AppItem { Id = id, Name = id };
                    }
                    if (!app.IsInstalled)
                    {
                        targetApps.Add(app);
                    }
                }

                if (targetApps.Count == 0)
                {
                    MessageBox.Show($"All applications in '{bundle.Name}' are already installed!", "Bundle Ready", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ActivityLogger.Instance.Log($"Enqueuing bundle deployment: {bundle.Name} ({targetApps.Count} apps)", ActivityType.Info);
                StartSequentialQueue(targetApps);
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
