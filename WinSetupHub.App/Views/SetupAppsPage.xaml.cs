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
    public partial class SetupAppsPage : UserControl, IRealtimeRefreshable
    {
        public void RefreshRealtime() => SyncDownloadPopupState();

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
                    string normQuery = AppMetadataHelper.NormalizeAppName(idOrName);
                    string cleanQuery = AppMetadataHelper.CleanPackageId(idOrName);

                    var currentItems = (AppsListBox.ItemsSource as IEnumerable<AppItem>) ?? Enumerable.Empty<AppItem>();
                    var pool = _allPackages.Concat(currentItems).Distinct().ToList();

                    var matches = pool.Where(p =>
                        string.Equals(p.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(cleanQuery) && string.Equals(AppMetadataHelper.CleanPackageId(p.Id), cleanQuery, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(normQuery) && string.Equals(AppMetadataHelper.NormalizeAppName(p.Name), normQuery, StringComparison.OrdinalIgnoreCase)) ||
                        PackageCatalogService.AreAliases(p.Id, idOrName) ||
                        PackageCatalogService.AreAliases(p.Name, idOrName)).ToList();

                    foreach (var p in matches)
                    {
                        p.IsInstalled = isInstalled;
                        if (isInstalled)
                        {
                            if (UpdateMonitorService.Instance.IsRecentlyUpdated(p.Id, p.Name))
                            {
                                p.IsRecentlyUpdated = true;
                                p.AvailableVersion = "";
                                p.IsUpgrade = false;
                                p.Status = "✓ Up to Date";
                            }
                            else if (!p.HasUpdate)
                            {
                                p.Status = "✓ Up to Date";
                            }
                        }
                        else
                        {
                            p.IsRecentlyUpdated = false;
                            p.AvailableVersion = "";
                            p.IsUpgrade = false;
                            p.Status = "Install";
                        }
                    }

                    if (matches.Count > 0 && string.IsNullOrWhiteSpace(SearchBox.Text))
                    {
                        ApplyFilter();
                    }
                });
            };

            UpdateMonitorService.Instance.UpdatesChanged += (_, _) =>
            {
                _ = Task.Run(async () =>
                {
                    if (_allPackages.Count > 0)
                    {
                        await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);
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

            DateTime lastInstalledCheck = DateTime.MinValue;
            IsVisibleChanged += async (_, e) =>
            {
                if ((bool)e.NewValue)
                {
                    SyncDownloadPopupState();
                    if (_allPackages.Count > 0 && (DateTime.UtcNow - lastInstalledCheck).TotalSeconds >= 10)
                    {
                        lastInstalledCheck = DateTime.UtcNow;
                        await Task.Run(async () =>
                        {
                            await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (string.IsNullOrWhiteSpace(SearchBox.Text))
                                {
                                    ApplyFilter();
                                }
                            });
                        });
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

                // Instant pass via AppMetadataHelper (takes <20ms) so initial render already has installed apps marked
                foreach (var item in _allPackages)
                {
                    if (AppMetadataHelper.IsAppInstalled(item))
                    {
                        item.IsInstalled = true;
                        item.Status = "✓ Up to Date";
                    }
                }

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

            // 1. Immediately start high-res icon fetching in parallel (does not wait for winget CLI)
            _ = Task.Run(async () =>
            {
                await Parallel.ForEachAsync(_allPackages, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (pkg, ct) =>
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
            });

            // 2. Background winget comprehensive status + update check
            _ = Task.Run(async () =>
            {
                _ = _winget.EnsureSourcesUpdatedAsync();
                _ = UpdateMonitorService.Instance.RefreshAsync();

                await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrWhiteSpace(SearchBox.Text))
                    {
                        ApplyFilter();
                    }
                });
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
            var pool = _allPackages.Concat(PackageCatalogService.GetDiscoveredPackages());
            var filtered = pool.AsEnumerable();

            if (string.IsNullOrWhiteSpace(query) && !string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(p => string.Equals(p.Category, _activeCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (p.DetectionNames != null && p.DetectionNames.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase))));
            }

            var results = PackageCatalogService.DeduplicatePackages(
                filtered
                .OrderBy(GetAppSortRank)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            );

            CatalogCountText.Text = $"{results.Count} Packages Ready";

            if (results.Count == 0)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(query)
                    ? "No packages match your category filter."
                    : $"Searching global software repositories for '{query}'…";
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
            var matches = PackageCatalogService.DeduplicatePackages(
                _allPackages
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
            )
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
                // 200ms debounce before invoking winget CLI
                await Task.Delay(200, ct);
                if (ct.IsCancellationRequested) return;

                List<AppItem>? onlineList;
                if (!_onlineSuggestCache.TryGetValue(query, out onlineList))
                {
                    onlineList = await _winget.SearchAsync(query, ct);
                    if (onlineList != null)
                    {
                        onlineList = PackageCatalogService.DeduplicatePackages(onlineList);
                        _onlineSuggestCache[query] = onlineList;
                    }
                }

                if (ct.IsCancellationRequested) return;

                if (onlineList == null || onlineList.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!string.Equals(SearchBox.Text.Trim(), query, StringComparison.OrdinalIgnoreCase)) return;
                        var currentMain = (AppsListBox.ItemsSource as IEnumerable<AppItem>)?.ToList();
                        if (currentMain == null || currentMain.Count == 0)
                        {
                            StatusText.Text = $"No local or online packages found for '{query}'.";
                            StatusText.Visibility = Visibility.Visible;
                        }
                    });
                    return;
                }

                // Take top 20 online packages, enrich metadata, infer category, and auto-detect Installed status (<1ms via Registry/AppX)
                var topOnline = onlineList.Take(20).ToList();
                foreach (var app in topOnline)
                {
                    AppMetadataHelper.EnrichAppItem(app, _allPackages);
                    if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General" || app.Category == "Online")
                    {
                        app.Category = PackageCatalogService.InferCategory(app.Id, app.Name, app.Description);
                    }
                    bool isInst = AppMetadataHelper.IsAppInstalled(app);
                    app.IsInstalled = isInst;
                    app.Status = isInst ? "✓ Up to Date" : "Install";
                }

                PackageCatalogService.RegisterDiscoveredPackages(topOnline);
                if (ct.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (!string.Equals(SearchBox.Text.Trim(), query, StringComparison.OrdinalIgnoreCase)) return;

                    var currentList = (SuggestionsListBox.ItemsSource as IEnumerable<AppItem>)?.ToList() ?? new List<AppItem>();

                    // Merge online packages that aren't already present in currentList (popup suggestions)
                    bool added = false;
                    foreach (var onlineApp in topOnline.Take(6))
                    {
                        string normOnline = PackageCatalogService.NormalizeAppKey(onlineApp.Name);
                        if (!currentList.Any(m => string.Equals(m.Id, onlineApp.Id, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(PackageCatalogService.NormalizeAppKey(m.Name), normOnline, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentList.Add(onlineApp);
                            added = true;
                        }
                    }

                    // Directly merge matching local packages and top online packages into AppsListBox!
                    var localMatches = _allPackages.Where(p =>
                        p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (p.DetectionNames != null && p.DetectionNames.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)))).ToList();

                    var combined = PackageCatalogService.DeduplicatePackages(localMatches.Concat(topOnline));
                    AppsListBox.ItemsSource = combined;
                    CatalogCountText.Text = $"{combined.Count} Packages Ready";
                    StatusText.Visibility = Visibility.Collapsed;

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
                    }

                    // Load icons & resolve exact official website links for online items in background
                    _ = Task.Run(async () =>
                    {
                        foreach (var item in topOnline.Take(12))
                        {
                            if (string.IsNullOrWhiteSpace(item.WebUrl) && !string.IsNullOrWhiteSpace(item.Id))
                            {
                                var meta = await _winget.GetMetadataAsync(item.Id);
                                if (meta?.BestLink != null)
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        item.WebUrl = meta.BestLink;
                                        if (string.IsNullOrWhiteSpace(item.IconUrl))
                                        {
                                            item.IconUrl = IconCacheService.DeriveFaviconUrl(item.WebUrl) ?? "";
                                        }
                                    });
                                }
                            }

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
                _onlineSearchCts?.Cancel();
                TriggerDirectSearch(SearchBox.Text.Trim());
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
                        _ = DisplaySingleOrOnlinePackageAsync(selected);
                    }
                }
                finally
                {
                    _isSelectingFromPopup = false;
                }
            }
        }

        private async Task DisplaySingleOrOnlinePackageAsync(AppItem app)
        {
            try
            {
                AppMetadataHelper.EnrichAppItem(app, _allPackages);
                if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General" || app.Category == "Online")
                {
                    app.Category = PackageCatalogService.InferCategory(app.Id, app.Name, app.Description);
                }
                bool isInst = AppMetadataHelper.IsAppInstalled(app);
                app.IsInstalled = isInst;
                app.Status = isInst ? "✓ Up to Date" : "Install";

                PackageCatalogService.RegisterDiscoveredPackages(new[] { app });

                var list = new List<AppItem> { app };
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying online package: {ex.Message}");
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
                _onlineSearchCts?.Cancel();
                TriggerDirectSearch(SearchBox.Text.Trim());
                e.Handled = true;
                return;
            }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            SearchSuggestionsPopup.IsOpen = false;
            _searchDebounceTimer.Stop();
            _onlineSearchCts?.Cancel();
            TriggerDirectSearch(SearchBox.Text.Trim());
        }

        private void WingetSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchSuggestionsPopup.IsOpen = false;
            SearchGhostText.Text = "";
            TabHintBadge.Visibility = Visibility.Collapsed;
            _searchDebounceTimer.Stop();
            _onlineSearchCts?.Cancel();

            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                SearchBox.Focus();
                StatusText.Text = "Please enter an application name in the search box to search online.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            TriggerDirectSearch(query);
        }

        private async void TriggerDirectSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ApplyFilter(resetScroll: true);
                return;
            }

            // Immediately display local matches
            ApplyFilter(resetScroll: true);

            // If already cached, show immediately
            if (_onlineSuggestCache.TryGetValue(query, out var cachedOnline) && cachedOnline.Count > 0)
            {
                foreach (var app in cachedOnline)
                {
                    AppMetadataHelper.EnrichAppItem(app, _allPackages);
                    if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General" || app.Category == "Online")
                    {
                        app.Category = PackageCatalogService.InferCategory(app.Id, app.Name, app.Description);
                    }
                    bool isInst = AppMetadataHelper.IsAppInstalled(app);
                    app.IsInstalled = isInst;
                    app.Status = isInst ? "✓ Up to Date" : "Install";
                }
                PackageCatalogService.RegisterDiscoveredPackages(cachedOnline);

                var localMatches = _allPackages.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (p.DetectionNames != null && p.DetectionNames.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)))).ToList();

                var combined = PackageCatalogService.DeduplicatePackages(localMatches.Concat(cachedOnline));
                AppsListBox.ItemsSource = combined;
                CatalogCountText.Text = $"{combined.Count} Packages Ready for '{query}'";
                StatusText.Visibility = Visibility.Collapsed;
                ResetAppsListScrollToTop();
                return;
            }

            StatusText.Text = $"Searching online software repositories for '{query}'…";
            StatusText.Visibility = Visibility.Visible;
            WingetSearchButton.IsEnabled = false;

            try
            {
                var onlineResults = PackageCatalogService.DeduplicatePackages(await _winget.SearchAsync(query));
                if (onlineResults.Count > 0)
                {
                    _onlineSuggestCache[query] = onlineResults;
                    foreach (var app in onlineResults)
                    {
                        AppMetadataHelper.EnrichAppItem(app, _allPackages);
                        if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General" || app.Category == "Online")
                        {
                            app.Category = PackageCatalogService.InferCategory(app.Id, app.Name, app.Description);
                        }
                        bool isInst = AppMetadataHelper.IsAppInstalled(app);
                        app.IsInstalled = isInst;
                        app.Status = isInst ? "✓ Up to Date" : "Install";
                    }

                    PackageCatalogService.RegisterDiscoveredPackages(onlineResults);

                    var localMatches = _allPackages.Where(p =>
                        p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (p.DetectionNames != null && p.DetectionNames.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)))).ToList();

                    var combined = PackageCatalogService.DeduplicatePackages(localMatches.Concat(onlineResults));
                    AppsListBox.ItemsSource = combined;
                    CatalogCountText.Text = $"{combined.Count} Packages Found for '{query}'";
                    StatusText.Visibility = Visibility.Collapsed;
                    ResetAppsListScrollToTop();

                    // Background parallel icon extraction
                    _ = Task.Run(async () =>
                    {
                        foreach (var app in onlineResults.Take(16))
                        {
                            if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(app.Id))
                            {
                                var meta = await _winget.GetMetadataAsync(app.Id);
                                if (meta?.BestLink != null)
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        app.WebUrl = meta.BestLink;
                                        if (string.IsNullOrWhiteSpace(app.IconUrl))
                                        {
                                            app.IconUrl = IconCacheService.DeriveFaviconUrl(app.WebUrl) ?? "";
                                        }
                                    });
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
                        }
                    });
                }
                else
                {
                    var currentItems = (AppsListBox.ItemsSource as IEnumerable<AppItem>)?.ToList() ?? new List<AppItem>();
                    if (currentItems.Count == 0)
                    {
                        var fallbackApp = new AppItem
                        {
                            Id = $"Web.{PackageCatalogService.NormalizeAppKey(query)}",
                            Name = query,
                            Category = "🌐 Web Download",
                            Description = $"Search and download the latest official version of {query} directly from official web sources.",
                            Source = "WEB",
                            Status = "🌐 Download from Web",
                            WebUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query + " official download")}",
                            IconUrl = IconCacheService.DeriveFaviconUrl("https://www.google.com") ?? ""
                        };
                        var fallbackList = new List<AppItem> { fallbackApp };
                        AppsListBox.ItemsSource = fallbackList;
                        CatalogCountText.Text = $"1 Web Match for '{query}'";
                        StatusText.Text = $"No Winget package found for '{query}'. Click 'Download from Web' to get it directly.";
                        StatusText.Visibility = Visibility.Visible;
                        ResetAppsListScrollToTop();
                    }
                    else
                    {
                        StatusText.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Search error: {ex.Message}";
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
                // Web Fallback: If item is a web search package, open official download in browser
                if (string.Equals(app.Source, "WEB", StringComparison.OrdinalIgnoreCase) ||
                    (app.Id != null && app.Id.StartsWith("Web.", StringComparison.OrdinalIgnoreCase)))
                {
                    var targetUrl = !string.IsNullOrWhiteSpace(app.WebUrl) ? app.WebUrl : app.EffectiveWebUrl;
                    if (string.IsNullOrWhiteSpace(targetUrl))
                    {
                        targetUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(app.Name + " official download")}";
                    }
                    try
                    {
                        Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error launching web url: {ex.Message}");
                    }
                    return;
                }
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

                // If user clicks Update for an app with a newer version available
                if (app.HasUpdate || app.IsUpgrade || app.Status == "Update")
                {
                    app.IsUpgrade = true;
                    app.Status = "Update";
                    StartSequentialQueue(new[] { app });
                    return;
                }

                // 1. Check if already installed on system
                bool isInstalled = app.IsInstalled || CheckIfAppAlreadyInstalled(app);
                if (isInstalled)
                {
                    app.IsInstalled = true;
                    if (app.HasUpdate || app.IsUpgrade)
                    {
                        app.IsUpgrade = true;
                        app.Status = "Update";
                        StartSequentialQueue(new[] { app });
                        return;
                    }
                    app.Status = "✓ Up to Date";
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

        private List<AppItem> GetSelectedApps()
        {
            var currentItems = (AppsListBox.ItemsSource as IEnumerable<AppItem>) ?? Enumerable.Empty<AppItem>();
            return _allPackages
                .Concat(currentItems)
                .Where(p => p.IsSelected)
                .DistinctBy(p => p.Id ?? p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void UpdateSelectionUI()
        {
            var selected = GetSelectedApps();
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
                    if (!item.IsInstalled || item.HasUpdate) item.IsSelected = true;
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
            if (AppsListBox.ItemsSource is IEnumerable<AppItem> currentItems)
            {
                foreach (var item in currentItems)
                {
                    item.IsSelected = false;
                }
            }
            UpdateSelectionUI();
        }

        private void InstallSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedApps();
            if (selected.Count == 0)
            {
                ThemedMessageBox.Show("Please select at least one app to install.", "No Apps Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var uninstalled = new List<AppItem>();
            var alreadyInstalled = new List<AppItem>();

            foreach (var app in selected)
            {
                if (app.HasUpdate || app.IsUpgrade || app.Status == "Update")
                {
                    app.IsUpgrade = true;
                    app.Status = "Update";
                    uninstalled.Add(app);
                }
                else if (CheckIfAppAlreadyInstalled(app))
                {
                    app.IsInstalled = true;
                    app.Status = "✓ Up to Date";
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
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ThemedMessageBox.Show($"Could not open website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void ModalOfficialSite_Click(object sender, RoutedEventArgs e)
        {
            if (_currentModalApp == null) return;
            string url = _currentModalApp.EffectiveWebUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ThemedMessageBox.Show($"Could not open website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    if (app.HasUpdate || app.IsUpgrade || app.Status == "Update")
                    {
                        app.IsUpgrade = true;
                        app.Status = "Update";
                        targetApps.Add(app);
                    }
                    else if (CheckIfAppAlreadyInstalled(app))
                    {
                        app.IsInstalled = true;
                        app.Status = "✓ Up to Date";
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
                        ThemedMessageBox.Show("No valid applications found in JSON file.", "Import Applications", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var confirm = ThemedMessageBox.Show($"Found {importedApps.Count} applications in list.\n\nDo you want to sequentially install missing packages now?", "Confirm Import & Install", MessageBoxButton.YesNo, MessageBoxImage.Question);
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

                        bool success = await _winget.InstallAsync(app.Id, app.Source, app.Name);
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
                    ThemedMessageBox.Show($"Import & Install completed!\n{ok} of {total} packages succeeded.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Import error: {ex.Message}", ActivityType.Error);
                    ThemedMessageBox.Show($"Failed to import file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #region Already Installed Modal & App Launcher

        private AppItem? _currentModalApp;

        private bool CheckIfAppAlreadyInstalled(AppItem app)
        {
            if (app.IsInstalled) return true;
            bool detected = AppMetadataHelper.IsAppInstalled(app);
            if (detected)
            {
                app.IsInstalled = true;
                app.Status = "✓ Up to Date";
                PackageCatalogService.NotifyStatusChanged(app.Id, true);
            }
            return detected;
        }

        private void ShowAlreadyInstalledModal(AppItem app)
        {
            _currentModalApp = app;

            ModalAppName.Text = app.Name;
            ModalAppIcon.Source = app.IconImageSource;
            ModalAppVersion.Text = !string.IsNullOrWhiteSpace(app.FormattedVersion) ? app.FormattedVersion : "✓ Latest";
            ModalAppSize.Text = !string.IsNullOrWhiteSpace(app.FormattedSize) ? app.FormattedSize : "Installed";
            ModalPackageId.Text = !string.IsNullOrWhiteSpace(app.Id) ? app.Id : app.Name;
            ModalCategory.Text = !string.IsNullOrWhiteSpace(app.Category) ? app.Category : "General";
            ModalOfficialSiteButton.ToolTip = $"Open Official Website: {app.EffectiveWebUrl}";

            var reg = AppMetadataHelper.GetRegistryInfo(app.Name, app.Id);
            string displayVer = !string.IsNullOrWhiteSpace(reg?.DisplayVersion)
                ? reg!.DisplayVersion
                : (!string.IsNullOrWhiteSpace(app.Version) ? app.Version : "Latest");

            if (app.HasUpdate || app.IsUpgrade)
            {
                ModalInstalledBadgeText.Text = "UPDATE AVAILABLE";
                ModalStatusTitle.Text = "Newer Version Detected";
                ModalReinstallButton.Content = $"⚡ Update to Latest ({app.AvailableVersion ?? "Latest"})";
                ModalStatusMessage.Text = $"{app.Name} has a newer version available: v{app.AvailableVersion} (Installed: v{displayVer}). Click Update to automatically fetch and install the latest official release.";
            }
            else
            {
                ModalInstalledBadgeText.Text = "ALREADY UPDATED • LATEST VERSION";
                ModalStatusTitle.Text = "Already Updated — You are in Latest Version";
                ModalReinstallButton.Content = "🔄 Reinstall Latest";
                ModalStatusMessage.Text = $"Already Updated! {app.Name} (v{displayVer}) is verified on this PC and you are in the latest version.";
            }

            AlreadyInstalledOverlay.Visibility = Visibility.Visible;

            if (!app.HasUpdate && !app.IsUpgrade && !string.IsNullOrWhiteSpace(app.Id))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var info = await _winget.GetInstallerInfoAsync(app.Id, forceRefresh: true, name: app.Name);
                        if (info != null && !string.IsNullOrWhiteSpace(info.Version))
                        {
                            var installedVer = !string.IsNullOrWhiteSpace(reg?.DisplayVersion) ? reg!.DisplayVersion : app.Version;
                            if (!string.IsNullOrWhiteSpace(installedVer) &&
                                AppMetadataHelper.IsNewerVersion(installedVer.Trim(), info.Version.Trim()))
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (_currentModalApp == app && AlreadyInstalledOverlay.Visibility == Visibility.Visible)
                                    {
                                        app.AvailableVersion = info.Version;
                                        app.IsUpgrade = true;
                                        ModalInstalledBadgeText.Text = "UPDATE AVAILABLE";
                                        ModalStatusTitle.Text = "Newer Version Detected";
                                        ModalReinstallButton.Content = $"⚡ Update to Latest ({info.Version})";
                                        ModalStatusMessage.Text = $"{app.Name} has a newer version available: v{info.Version} (Installed: v{installedVer}). Click Update to automatically fetch and install the latest official release.";
                                    }
                                });
                            }
                            else
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (_currentModalApp == app && AlreadyInstalledOverlay.Visibility == Visibility.Visible)
                                    {
                                        app.Version = info.Version;
                                        ModalAppVersion.Text = app.FormattedVersion;
                                        ModalInstalledBadgeText.Text = "ALREADY UPDATED • LATEST VERSION";
                                        ModalStatusTitle.Text = "Already Updated — You are in Latest Version";
                                        ModalStatusMessage.Text = $"Already Updated! {app.Name} (v{info.Version}) is verified on this PC and you are in the latest version.";
                                    }
                                });
                            }
                        }
                    }
                    catch { }
                });
            }
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
                if (_currentModalApp.HasUpdate || _currentModalApp.IsUpgrade)
                {
                    _currentModalApp.IsUpgrade = true;
                    _currentModalApp.Status = "Update";
                }
                else
                {
                    _currentModalApp.IsUpgrade = true; // force reinstall will fetch the latest version
                    _currentModalApp.IsInstalled = false;
                    _currentModalApp.Status = "Install";
                }
                StartSequentialQueue(new[] { _currentModalApp });
            }
        }

        private void LaunchInstalledApp(AppItem app)
        {
            try
            {
                bool launched = AppMetadataHelper.LaunchApp(app);
                if (launched)
                {
                    ActivityLogger.Instance.Log($"Successfully launched {app.Name}.", ActivityType.Info);
                    return;
                }

                // If launch failed, check whether it is verified installed on PC:
                bool isInstalled = AppMetadataHelper.IsAppInstalled(app);
                if (isInstalled)
                {
                    ThemedMessageBox.Show(
                        $"Unable to automatically launch {app.Name}.\nYou can launch it directly from your Windows Start Menu.",
                        $"Open {app.Name}",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // App executable was truly not found on system (uninstalled or moved).
                app.IsInstalled = false;
                app.Status = "Install";

                var result = ThemedMessageBox.Show(
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
