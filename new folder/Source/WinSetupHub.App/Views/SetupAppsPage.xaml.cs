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
using System.IO;
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
                ApplyFilter(resetScroll: true);
            };

            DownloadManagerService.Instance.ProgressChanged += OnDownloadProgressChanged;
            DownloadManagerService.Instance.QueueChanged += OnDownloadQueueChanged;
            DownloadManagerService.Instance.QueueCompleted += OnDownloadQueueCompleted;
            DownloadManagerService.Instance.QueueCancelled += OnDownloadQueueCancelled;

            PackageCatalogService.AppInstallationStatusChanged += (idOrName, isInstalled) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var matches = _allPackages.Where(p =>
                        string.Equals(p.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();

                    foreach (var p in matches)
                    {
                        p.IsInstalled = isInstalled;
                        p.Status = isInstalled ? "Installed" : "Install";
                    }
                });
            };

            Loaded += async (_, _) =>
            {
                SyncDownloadPopupState();
                if (_allPackages.Count == 0)
                {
                    await InitializeCatalogAsync();
                }
            };

            IsVisibleChanged += async (_, e) =>
            {
                if ((bool)e.NewValue)
                {
                    SyncDownloadPopupState();
                    if (_allPackages.Count > 0)
                    {
                        await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);
                    }
                }
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && AlreadyInstalledOverlay.Visibility == Visibility.Visible)
                {
                    AlreadyInstalledOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
            };
        }

        private static readonly string[] TopBasicDailyApps = new[]
        {
            // Top AI Assistants & Agents
            "9PLM9XGG6VKS", // ChatGPT Desktop
            "Anthropic.Claude", // Claude Desktop
            "ElementLabs.LMStudio", // LM Studio
            "Ollama.Ollama", // Ollama
            "Anysphere.Cursor", // Cursor
            "Codeium.Windsurf", // Windsurf
            "Perplexity.Perplexity", // Perplexity

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
                ("AI", "AI Tools", "🤖"),
                ("Microsoft Store", "Microsoft Store", "🛍️"),
                ("Browsers", "Browsers & Web", "🌐"),
                ("Developer", "Developer Tools", "💻"),
                ("Gaming", "Gaming Tools", "🎮"),
                ("Productivity", "Office & Productivity", "💼"),
                ("Research & Science", "Research & Science", "🔬"),
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
                    ApplyFilter(resetScroll: true);
                };

                chip.Click += (_, _) =>
                {
                    _activeCategory = key;
                    if (!string.IsNullOrEmpty(SearchBox.Text))
                    {
                        SearchBox.Text = string.Empty;
                    }
                    ApplyFilter(resetScroll: true);
                };

                CategoryChipsPanel.Children.Add(chip);
            }
        }

        private void ApplyFilter(bool resetScroll = false)
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

            if (resetScroll)
            {
                ResetAppsListScrollToTop();
            }
        }

        private void ResetAppsListScrollToTop()
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

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var s = SmoothScrollHelper.FindChildScrollViewer(AppsListBox);
                        if (s != null)
                        {
                            SmoothScrollHelper.ScrollToTopImmediate(s);
                        }
                        else if (AppsListBox.Items.Count > 0)
                        {
                            AppsListBox.ScrollIntoView(AppsListBox.Items[0]);
                        }
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch { }
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
            ResetAppsListScrollToTop();

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
            ApplyFilter(resetScroll: true);
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SearchSuggestionsPopup.IsOpen = false;
                SearchGhostText.Text = "";
                TabHintBadge.Visibility = Visibility.Collapsed;
                _searchDebounceTimer.Stop();
                ApplyFilter(resetScroll: true);

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

                // 1. Check if already installed on system
                bool isInstalled = CheckIfAppAlreadyInstalled(app);
                if (isInstalled)
                {
                    app.IsInstalled = true;
                    app.Status = "Installed";
                    ShowAlreadyInstalledModal(app);
                    return;
                }
                else
                {
                    app.IsInstalled = false;
                    app.Status = "Install";
                }

                // 2. Not installed -> proceed with download and installation
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
            var selected = _allPackages.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one app to install.", "No Apps Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var uninstalled = new List<AppItem>();
            var alreadyInstalled = new List<AppItem>();

            foreach (var app in selected)
            {
                if (CheckIfAppAlreadyInstalled(app))
                {
                    app.IsInstalled = true;
                    app.Status = "Installed";
                    alreadyInstalled.Add(app);
                }
                else
                {
                    app.IsInstalled = false;
                    app.Status = "Install";
                    uninstalled.Add(app);
                }
            }

            if (selected.Count == 1 && alreadyInstalled.Count == 1)
            {
                ShowAlreadyInstalledModal(alreadyInstalled[0]);
                return;
            }

            if (uninstalled.Count == 0)
            {
                if (alreadyInstalled.Count > 0)
                {
                    ShowAlreadyInstalledModal(alreadyInstalled[0]);
                }
                return;
            }

            StartSequentialQueue(uninstalled);
        }

        private DispatcherTimer? _popupAutoDismissTimer;

        private void StartPopupAutoDismissTimer()
        {
            _popupAutoDismissTimer?.Stop();
            _popupAutoDismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            _popupAutoDismissTimer.Tick += (_, _) =>
            {
                _popupAutoDismissTimer.Stop();
                DownloadPopupPanel.Visibility = Visibility.Collapsed;
                AppsListBox.Margin = new Thickness(0);
            };
            _popupAutoDismissTimer.Start();
        }

        private void SyncDownloadPopupState()
        {
            var dm = DownloadManagerService.Instance;
            if (dm.IsCancelled || (!dm.IsRunning && !dm.IsPaused))
            {
                _popupAutoDismissTimer?.Stop();
                DownloadPopupPanel.Visibility = Visibility.Collapsed;
                AppsListBox.Margin = new Thickness(0);
                PopupQueueDrawer.Visibility = Visibility.Collapsed;
                return;
            }

            var info = dm.CurrentProgressInfo;
            if (info != null)
            {
                OnDownloadProgressChanged(info);
            }
        }

        private void StartSequentialQueue(IEnumerable<AppItem> apps)
        {
            var list = apps.ToList();
            if (list.Count == 0) return;

            _popupAutoDismissTimer?.Stop();
            DownloadPopupPanel.Visibility = Visibility.Visible;
            AppsListBox.Margin = new Thickness(0, 0, 0, 110);

            PopupPauseResumeButton.Visibility = Visibility.Visible;
            LottieHelper.SetButtonIcon(PopupPauseResumeButton, "pause.json", 14);
            PopupPauseResumeButton.Tag = "Pause";
            PopupPauseResumeButton.IsEnabled = true;
            PopupSkipButton.Visibility = Visibility.Visible;
            LottieHelper.SetButtonIcon(PopupSkipButton, "skip.json", 14);
            PopupCancelButton.Visibility = Visibility.Visible;
            LottieHelper.SetButtonIcon(PopupCancelButton, "cancel.json", 14);
            PopupDismissButton.Visibility = Visibility.Collapsed;

            DownloadManagerService.Instance.EnqueueRange(list);
            UpdateSelectionUI();
        }

        private void OnDownloadProgressChanged(DownloadProgressInfo info)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var dm = DownloadManagerService.Instance;
                if (dm.IsCancelled || (!dm.IsRunning && !dm.IsPaused))
                {
                    _popupAutoDismissTimer?.Stop();
                    DownloadPopupPanel.Visibility = Visibility.Collapsed;
                    AppsListBox.Margin = new Thickness(0);
                    return;
                }

                _popupAutoDismissTimer?.Stop();
                DownloadPopupPanel.Visibility = Visibility.Visible;
                AppsListBox.Margin = new Thickness(0, 0, 0, 110);
                PopupDismissButton.Visibility = Visibility.Collapsed;
                PopupCancelButton.Visibility = Visibility.Visible;
                PopupSkipButton.Visibility = Visibility.Visible;
                PopupPauseResumeButton.Visibility = Visibility.Visible;

                PopupAppName.Text = info.App.Name;
                PopupAppIcon.Source = info.App.IconImageSource;
                PopupQueueText.Text = $"App {info.QueueIndex} of {info.QueueTotal}";
                double displayPct = info.Percentage > 0 ? info.Percentage : (info.App?.DownloadProgress ?? 0);
                double targetPct = Math.Clamp(displayPct, 0, 100);
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = targetPct,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                PopupProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);

                PopupPercentageText.Text = $"{displayPct:0}%";
                PopupSpeedText.Text = info.SpeedFormatted;
                string eta = info.EtaFormatted ?? "Calculating…";
                if (eta.StartsWith("ETA:", StringComparison.OrdinalIgnoreCase))
                {
                    eta = eta.Substring(4).TrimStart();
                }
                PopupEtaText.Text = eta;
                string size = !string.IsNullOrWhiteSpace(info.SizeFormatted) ? info.SizeFormatted : (DownloadManagerService.Instance.CurrentProgressInfo?.SizeFormatted ?? "");
                PopupSizeText.Text = size;
                PopupStatusDetail.Text = info.StatusMessage;

                switch (info.State)
                {
                    case DownloadState.Downloading:
                        PopupStateText.Text = "DOWNLOADING";
                        PopupStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushAccent");
                        LottieHelper.SetButtonIcon(PopupPauseResumeButton, "pause.json", 14);
                        PopupPauseResumeButton.Tag = "Pause";
                        PopupPauseResumeButton.IsEnabled = true;
                        break;

                    case DownloadState.Paused:
                        PopupStateText.Text = "PAUSED";
                        PopupStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                        LottieHelper.SetButtonIcon(PopupPauseResumeButton, "resume.json", 14);
                        PopupPauseResumeButton.Tag = "Resume";
                        PopupPauseResumeButton.IsEnabled = true;
                        break;

                    case DownloadState.Installing:
                        PopupStateText.Text = "INSTALLING";
                        PopupStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                        PopupStateText.Foreground = (Brush)FindResource("BrushAccent");
                        PopupPauseResumeButton.IsEnabled = false;
                        break;

                    case DownloadState.Error:
                        PopupStateText.Text = "CONNECTION ERROR";
                        PopupStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushError");
                        LottieHelper.SetButtonIcon(PopupPauseResumeButton, "resume.json", 14);
                        PopupPauseResumeButton.Tag = "Resume";
                        PopupPauseResumeButton.IsEnabled = true;
                        break;

                    case DownloadState.Completed:
                        PopupStateText.Text = "INSTALLED";
                        PopupStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                        PopupStateText.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                        break;
                }

                UpdateSelectionUI();
            });
        }

        private void OnDownloadQueueCancelled()
        {
            Dispatcher.InvokeAsync(() =>
            {
                _popupAutoDismissTimer?.Stop();
                DownloadPopupPanel.Visibility = Visibility.Collapsed;
                AppsListBox.Margin = new Thickness(0);
                PopupQueueDrawer.Visibility = Visibility.Collapsed;
                UpdateSelectionUI();
            });
        }

        private void OnDownloadQueueCompleted()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var dm = DownloadManagerService.Instance;
                if (dm.IsCancelled || (!dm.IsRunning && !dm.IsPaused && dm.RemainingQueue.Count == 0 && dm.CurrentApp == null && dm.CurrentProgressInfo == null))
                {
                    _popupAutoDismissTimer?.Stop();
                    DownloadPopupPanel.Visibility = Visibility.Collapsed;
                    AppsListBox.Margin = new Thickness(0);
                    return;
                }

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

                StartPopupAutoDismissTimer();
            });
        }

        private void OnDownloadQueueChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var dm = DownloadManagerService.Instance;
                var remaining = dm.RemainingQueue;

                if (dm.IsCancelled || (!dm.IsRunning && !dm.IsPaused && remaining.Count == 0 && PopupStateText.Text != "COMPLETED"))
                {
                    _popupAutoDismissTimer?.Stop();
                    DownloadPopupPanel.Visibility = Visibility.Collapsed;
                    AppsListBox.Margin = new Thickness(0);
                    PopupQueueDrawer.Visibility = Visibility.Collapsed;
                    UpdateSelectionUI();
                    return;
                }

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
            _popupAutoDismissTimer?.Stop();
            DownloadManagerService.Instance.CancelAll();
            DownloadPopupPanel.Visibility = Visibility.Collapsed;
            AppsListBox.Margin = new Thickness(0);
            PopupQueueDrawer.Visibility = Visibility.Collapsed;
            UpdateSelectionUI();
        }

        private void PopupDismiss_Click(object sender, RoutedEventArgs e)
        {
            _popupAutoDismissTimer?.Stop();
            DownloadPopupPanel.Visibility = Visibility.Collapsed;
            AppsListBox.Margin = new Thickness(0);
            PopupQueueDrawer.Visibility = Visibility.Collapsed;
        }

        private void CategoryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv && sv.ScrollableWidth > 0)
            {
                SmoothScrollHelper.HandleMouseWheel(sv, e, forceHorizontal: true);
                e.Handled = true;
            }
        }

        private void BundlesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv && sv.ScrollableWidth > 0)
            {
                SmoothScrollHelper.HandleMouseWheel(sv, e, forceHorizontal: true);
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
                var installedApps = new List<AppItem>();

                foreach (var id in bundle.WingetIds)
                {
                    var app = _allPackages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (app == null)
                    {
                        app = new AppItem { Id = id, Name = id };
                    }
                    if (CheckIfAppAlreadyInstalled(app))
                    {
                        app.IsInstalled = true;
                        app.Status = "Installed";
                        installedApps.Add(app);
                    }
                    else
                    {
                        app.IsInstalled = false;
                        app.Status = "Install";
                        targetApps.Add(app);
                    }
                }

                if (targetApps.Count == 0)
                {
                    if (installedApps.Count > 0)
                    {
                        ShowAlreadyInstalledModal(installedApps[0]);
                    }
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

        #region Already Installed Modal & App Launcher

        private AppItem? _currentModalApp;

        private bool CheckIfAppAlreadyInstalled(AppItem app)
        {
            // 1. Windows Registry uninstall check (covers 99% of win32/x64 software)
            var reg = AppMetadataHelper.GetRegistryInfo(app.Name, app.Id);
            if (reg != null)
            {
                if (!string.IsNullOrWhiteSpace(reg.DisplayVersion))
                {
                    app.Version = reg.DisplayVersion;
                }
                if (!string.IsNullOrWhiteSpace(reg.InstallLocation) && Directory.Exists(reg.InstallLocation))
                {
                    app.InstallLocation = reg.InstallLocation;
                }
                return true;
            }

            // 2. Check local icon path if it points to an installed executable
            if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && app.LocalIconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(app.LocalIconPath))
            {
                return true;
            }

            // 3. Check App InstallLocation if present (must contain executable)
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                try
                {
                    if (Directory.EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly).Any())
                    {
                        return true;
                    }
                }
                catch { }
            }

            // 4. Check standard program directories (strictly requiring an executable)
            string normName = AppMetadataHelper.NormalizeAppName(app.Name);
            string[] baseDirs = {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            foreach (var b in baseDirs)
            {
                if (string.IsNullOrWhiteSpace(b) || !Directory.Exists(b)) continue;
                string dir1 = Path.Combine(b, app.Name);
                string dir2 = Path.Combine(b, normName);
                if (DirectoryContainsExe(dir1) || DirectoryContainsExe(dir2))
                {
                    return true;
                }
            }

            // 5. Start Menu shortcuts
            string[] startDirs = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
            };
            foreach (var dir in startDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var lnks = Directory.GetFiles(dir, $"*{normName}*.lnk", SearchOption.AllDirectories);
                        if (lnks.Length > 0)
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }

            return false;
        }

        private static bool DirectoryContainsExe(string dir)
        {
            if (!Directory.Exists(dir)) return false;
            try
            {
                return Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                return false;
            }
        }

        private void ShowAlreadyInstalledModal(AppItem app)
        {
            _currentModalApp = app;

            ModalAppName.Text = app.Name;
            ModalAppIcon.Source = app.IconImageSource;
            ModalAppVersion.Text = !string.IsNullOrWhiteSpace(app.FormattedVersion) ? app.FormattedVersion : "vLatest";
            ModalAppSize.Text = !string.IsNullOrWhiteSpace(app.FormattedSize) ? app.FormattedSize : "Installed";
            ModalPackageId.Text = !string.IsNullOrWhiteSpace(app.Id) ? app.Id : app.Name;
            ModalCategory.Text = !string.IsNullOrWhiteSpace(app.Category) ? app.Category : "General";

            var reg = AppMetadataHelper.GetRegistryInfo(app.Name, app.Id);
            if (reg != null && !string.IsNullOrWhiteSpace(reg.DisplayVersion))
            {
                ModalStatusMessage.Text = $"{app.Name} (v{reg.DisplayVersion}) is verified and ready to use on this PC.";
            }
            else
            {
                ModalStatusMessage.Text = $"{app.Name} is verified and ready to use on this PC.";
            }

            AlreadyInstalledOverlay.Visibility = Visibility.Visible;
        }

        private void ModalClose_Click(object sender, RoutedEventArgs e)
        {
            AlreadyInstalledOverlay.Visibility = Visibility.Collapsed;
        }

        private void AlreadyInstalledOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
        {
            AlreadyInstalledOverlay.Visibility = Visibility.Collapsed;
        }

        private void AlreadyInstalledCard_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent backdrop click from dismissing
        }

        private void ModalLaunch_Click(object sender, RoutedEventArgs e)
        {
            AlreadyInstalledOverlay.Visibility = Visibility.Collapsed;
            if (_currentModalApp != null)
            {
                LaunchInstalledApp(_currentModalApp);
            }
        }

        private void ModalReinstall_Click(object sender, RoutedEventArgs e)
        {
            AlreadyInstalledOverlay.Visibility = Visibility.Collapsed;
            if (_currentModalApp != null)
            {
                _currentModalApp.IsInstalled = false;
                _currentModalApp.Status = "Install";
                StartSequentialQueue(new[] { _currentModalApp });
            }
        }

        private void LaunchInstalledApp(AppItem app)
        {
            try
            {
                // 1. Direct local executable from icon path
                if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && app.LocalIconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(app.LocalIconPath))
                {
                    Process.Start(new ProcessStartInfo(app.LocalIconPath) { UseShellExecute = true });
                    return;
                }

                // 2. Query registry install location for main executable
                var reg = AppMetadataHelper.GetRegistryInfo(app.Name, app.Id);
                if (reg != null && !string.IsNullOrWhiteSpace(reg.InstallLocation) && Directory.Exists(reg.InstallLocation))
                {
                    var exes = Directory.GetFiles(reg.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                    var mainExe = exes.FirstOrDefault(f => !Path.GetFileName(f).Contains("uninstall", StringComparison.OrdinalIgnoreCase) &&
                                                           !Path.GetFileName(f).Contains("update", StringComparison.OrdinalIgnoreCase) &&
                                                           !Path.GetFileName(f).Contains("helper", StringComparison.OrdinalIgnoreCase)) ?? exes.FirstOrDefault();
                    if (mainExe != null && File.Exists(mainExe))
                    {
                        Process.Start(new ProcessStartInfo(mainExe) { UseShellExecute = true });
                        return;
                    }
                }

                // 3. Search Start Menu shortcuts
                string[] startDirs = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
                };
                string normName = AppMetadataHelper.NormalizeAppName(app.Name);
                foreach (var dir in startDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        var lnks = Directory.GetFiles(dir, $"*{normName}*.lnk", SearchOption.AllDirectories);
                        if (lnks.Length > 0)
                        {
                            Process.Start(new ProcessStartInfo(lnks[0]) { UseShellExecute = true });
                            return;
                        }
                    }
                }

                // 4. Try App Paths registry
                using var appPathsKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (appPathsKey != null)
                {
                    foreach (var sub in appPathsKey.GetSubKeyNames())
                    {
                        if (sub.Contains(app.Name, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(app.Id) && sub.Contains(app.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            using var k = appPathsKey.OpenSubKey(sub);
                            string? exe = k?.GetValue("") as string;
                            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                            {
                                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                                return;
                            }
                        }
                    }
                }

                // 5. App executable was not found on system (uninstalled or moved).
                // Do NOT open the website! Reset status and offer reinstallation prompt.
                app.IsInstalled = false;
                app.Status = "Install";

                var result = MessageBox.Show(
                    $"Could not find the executable for {app.Name} on this PC.\nIt may have been uninstalled or moved.\n\nWould you like to install {app.Name} now?",
                    $"{app.Name} Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    StartSequentialQueue(new[] { app });
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Could not launch {app.Name}: {ex.Message}", ActivityType.Warning);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Refreshing application catalog and verifying installed status…";
                StatusText.Visibility = Visibility.Visible;
                AppMetadataHelper.InvalidateCache();
                await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);
                ApplyFilter();
                StatusText.Visibility = Visibility.Collapsed;
                ActivityLogger.Instance.Log("Setup apps catalog status refreshed successfully.", ActivityType.Info);
            }
            catch (Exception ex)
            {
                StatusText.Visibility = Visibility.Collapsed;
                ActivityLogger.Instance.Log($"Error refreshing setup apps catalog: {ex.Message}", ActivityType.Warning);
            }
        }

        #endregion
    }
}
