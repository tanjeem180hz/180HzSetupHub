using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class StartupManagerPage : UserControl
    {
        private bool _isInitialized = false;
        private readonly StartupManagerService _startupService = new();
        private readonly PackageCatalogService _catalogService = new();
        private List<StartupItem> _items = new();
        private List<StartupItem> _displayedItems = new();
        private List<AppItem>? _catalogCache;

        private string _currentFilter = "All"; // "All", "Enabled", "Disabled"
        private string _searchQuery = "";
        private string _currentHistoryFilter = "All"; // "All", "Enabled", "Disabled"

        public StartupManagerPage()
        {
            InitializeComponent();
            _isInitialized = true;

            // Safe programmatic check after all elements are initialized
            if (FilterAllRadio != null) FilterAllRadio.IsChecked = true;
            if (HistoryFilterAllRadio != null) HistoryFilterAllRadio.IsChecked = true;

            Loaded += async (_, _) =>
            {
                if (_items.Count == 0)
                {
                    await LoadItemsAsync();
                }
                RefreshHistory();
            };
        }

        private async Task LoadItemsAsync()
        {
            try
            {
                _items = await Task.Run(() => _startupService.GetStartupItems());

                UpdateStatCounts();
                ApplyFilters();
                RefreshHistory();

                // Background async icon extraction & website discovery in parallel
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _catalogCache ??= await _catalogService.GetAllAsync();

                        await Parallel.ForEachAsync(_items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (item, ct) =>
                        {
                            ImageSource? icon = null;

                            // 1. If target file path exists (e.g. from Startup folder), extract directly
                            if (!string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath))
                            {
                                item.LocalIconPath = item.TargetFilePath;
                                icon = IconCacheService.GetLocalFileIcon(item.TargetFilePath);
                            }

                            // 2. Try extracting local .exe icon from command line
                            if (icon == null)
                            {
                                var exePath = ExtractExecutablePath(item.Command);
                                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                                {
                                    exePath = ResolveFromAppPaths(item.Name) ?? ResolveFromAppPaths(item.Command);
                                }

                                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                                {
                                    exePath = AppMetadataHelper.FindLocalIconPath(item.Name);
                                }

                                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                                {
                                    item.LocalIconPath = exePath;
                                    icon = IconCacheService.GetLocalFileIcon(exePath);
                                }
                            }

                            // 3. Query registry for official website
                            var regInfo = AppMetadataHelper.GetRegistryInfo(item.Name);
                            if (regInfo != null && !string.IsNullOrWhiteSpace(regInfo.WebUrl))
                            {
                                item.WebUrl = regInfo.WebUrl;
                            }

                            // 4. Match against catalog for authentic logo & website
                            if (_catalogCache != null)
                            {
                                var matchedPkg = AppMetadataHelper.FindCatalogMatchForName(item.Name, _catalogCache);
                                if (matchedPkg != null)
                                {
                                    if (string.IsNullOrWhiteSpace(item.WebUrl)) item.WebUrl = matchedPkg.WebUrl;

                                    var targetUrl = matchedPkg.IconUrl;
                                    if (string.IsNullOrWhiteSpace(targetUrl) && !string.IsNullOrWhiteSpace(matchedPkg.WebUrl))
                                    {
                                        targetUrl = IconCacheService.DeriveFaviconUrl(matchedPkg.WebUrl);
                                    }

                                    if (icon == null && (!string.IsNullOrWhiteSpace(targetUrl) || !string.IsNullOrWhiteSpace(matchedPkg.LocalIconPath)))
                                    {
                                        item.IconUrl = targetUrl;
                                        icon = await IconCacheService.GetImageAsync(targetUrl, matchedPkg.LocalIconPath);
                                    }
                                }
                            }

                            // 5. Derive icon from WebUrl if still missing
                            if (icon == null && !string.IsNullOrWhiteSpace(item.WebUrl))
                            {
                                var favUrl = IconCacheService.DeriveFaviconUrl(item.WebUrl);
                                if (!string.IsNullOrWhiteSpace(favUrl))
                                {
                                    icon = await IconCacheService.GetImageAsync(favUrl);
                                }
                            }

                            if (icon != null)
                            {
                                await Dispatcher.InvokeAsync(() => item.IconImageSource = icon);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        ActivityLogger.Instance.Log($"Background icon resolution: {ex.Message}", ActivityType.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to scan startup entries: {ex.Message}", ActivityType.Error);
            }
        }

        private void UpdateStatCounts()
        {
            if (!_isInitialized) return;

            var total = _items.Count;
            var enabledCount = _items.Count(i => i.IsEnabled);
            var disabledCount = total - enabledCount;
            var historyCount = StartupHistoryService.Instance.GetHistory().Count;

            if (StatTotalText != null) StatTotalText.Text = $"{total} Apps";
            if (StatActiveText != null) StatActiveText.Text = $"{enabledCount} Active (ON)";
            if (StatDisabledText != null) StatDisabledText.Text = $"{disabledCount} Disabled (OFF)";
            if (StatHistoryText != null) StatHistoryText.Text = $"{historyCount} History Logs";

            if (FilterAllRadio != null) FilterAllRadio.Content = $"All ({total})";
            if (FilterEnabledRadio != null) FilterEnabledRadio.Content = $"🟢 Active ON ({enabledCount})";
            if (FilterDisabledRadio != null) FilterDisabledRadio.Content = $"⚪ Disabled OFF ({disabledCount})";
        }

        private void ApplyFilters()
        {
            if (!_isInitialized || StartupList == null) return;

            IEnumerable<StartupItem> query = _items;

            // 1. Filter by status
            if (_currentFilter == "Enabled")
            {
                query = query.Where(i => i.IsEnabled);
            }
            else if (_currentFilter == "Disabled")
            {
                query = query.Where(i => !i.IsEnabled);
            }

            // 2. Filter by search text
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.Trim();
                query = query.Where(i =>
                    (i.Name != null && i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (i.Command != null && i.Command.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (i.Source != null && i.Source.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            _displayedItems = query.ToList();
            StartupList.ItemsSource = null;
            StartupList.ItemsSource = _displayedItems;

            // Empty state
            if (EmptyNotice != null)
            {
                if (_displayedItems.Count == 0)
                {
                    EmptyNotice.Visibility = Visibility.Visible;
                    if (EmptyNoticeDetail != null)
                    {
                        if (!string.IsNullOrWhiteSpace(_searchQuery))
                        {
                            EmptyNoticeDetail.Text = $"No startup applications matching \"{_searchQuery}\".";
                        }
                        else if (_currentFilter == "Enabled")
                        {
                            EmptyNoticeDetail.Text = "No startup applications are currently enabled (turned ON).";
                        }
                        else if (_currentFilter == "Disabled")
                        {
                            EmptyNoticeDetail.Text = "No startup applications are currently disabled (turned OFF).";
                        }
                        else
                        {
                            EmptyNoticeDetail.Text = "No startup applications found on this system.";
                        }
                    }
                }
                else
                {
                    EmptyNotice.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (FilterAllRadio?.IsChecked == true) _currentFilter = "All";
            else if (FilterEnabledRadio?.IsChecked == true) _currentFilter = "Enabled";
            else if (FilterDisabledRadio?.IsChecked == true) _currentFilter = "Disabled";

            ApplyFilters();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;

            _searchQuery = SearchBox?.Text?.Trim() ?? "";
            if (ClearSearchButton != null)
            {
                ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(_searchQuery) ? Visibility.Collapsed : Visibility.Visible;
            }
            ApplyFilters();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null)
            {
                SearchBox.Text = "";
                SearchBox.Focus();
            }
        }

        private void StatActive_Click(object sender, MouseButtonEventArgs e)
        {
            if (FilterEnabledRadio != null)
            {
                FilterEnabledRadio.IsChecked = true;
            }
        }

        private void StatDisabled_Click(object sender, MouseButtonEventArgs e)
        {
            if (FilterDisabledRadio != null)
            {
                FilterDisabledRadio.IsChecked = true;
            }
        }

        private void StatHistory_Click(object sender, MouseButtonEventArgs e)
        {
            OpenHistory();
        }

        private void OpenHistory_Click(object sender, RoutedEventArgs e)
        {
            OpenHistory();
        }

        private void OpenHistory()
        {
            if (HistoryOverlay != null)
            {
                HistoryOverlay.Visibility = Visibility.Visible;
            }
            RefreshHistory();
        }

        private void CloseHistory_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryOverlay != null)
            {
                HistoryOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void HistoryBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (HistoryOverlay != null)
            {
                HistoryOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshHistory()
        {
            if (!_isInitialized || HistoryList == null) return;

            var history = StartupHistoryService.Instance.GetHistory();

            IEnumerable<StartupHistoryEntry> q = history;
            if (_currentHistoryFilter == "Enabled")
            {
                q = q.Where(h => h.IsEnabledAction);
            }
            else if (_currentHistoryFilter == "Disabled")
            {
                q = q.Where(h => !h.IsEnabledAction);
            }

            var list = q.ToList();
            HistoryList.ItemsSource = null;
            HistoryList.ItemsSource = list;

            if (EmptyHistoryNotice != null)
            {
                EmptyHistoryNotice.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (HistorySummaryText != null)
            {
                HistorySummaryText.Text = $"{history.Count} total modification events recorded";
            }
            if (StatHistoryText != null)
            {
                StatHistoryText.Text = $"{history.Count} History Logs";
            }
        }

        private void HistoryFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (HistoryFilterAllRadio?.IsChecked == true) _currentHistoryFilter = "All";
            else if (HistoryFilterEnabledRadio?.IsChecked == true) _currentHistoryFilter = "Enabled";
            else if (HistoryFilterDisabledRadio?.IsChecked == true) _currentHistoryFilter = "Disabled";

            RefreshHistory();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = ThemedMessageBox.Show(
                "Are you sure you want to clear the startup modification history log?",
                "Clear History Log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                StartupHistoryService.Instance.ClearHistory();
                RefreshHistory();
                ActivityLogger.Instance.Log("Cleared startup manager modification history.", ActivityType.Info);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadItemsAsync();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is StartupItem item)
            {
                var newEnabled = toggle.IsChecked == true;

                try
                {
                    _startupService.SetEnabled(item, newEnabled);

                    ActivityLogger.Instance.Log(
                        $"{(newEnabled ? "Enabled (Turned ON)" : "Disabled (Turned OFF)")} startup application: {item.Name}",
                        ActivityType.Info);

                    UpdateStatCounts();
                    RefreshHistory();

                    // If currently filtered to Enabled or Disabled, re-evaluate list
                    if (_currentFilter != "All")
                    {
                        ApplyFilters();
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Revert toggle
                    item.IsEnabled = !newEnabled;
                    toggle.IsChecked = !newEnabled;

                    ThemedMessageBox.Show(ex.Message, "Administrator Permission Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ActivityLogger.Instance.Log($"Permission denied modifying {item.Name} startup entry.", ActivityType.Warning);
                }
                catch (Exception ex)
                {
                    item.IsEnabled = !newEnabled;
                    toggle.IsChecked = !newEnabled;

                    ThemedMessageBox.Show($"Could not update startup item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ActivityLogger.Instance.Log($"Failed to toggle {item.Name}: {ex.Message}", ActivityType.Error);
                }
            }
        }

        private void LaunchApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is StartupItem item)
            {
                try
                {
                    var target = !string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath)
                        ? item.TargetFilePath
                        : ExtractExecutablePath(item.Command);

                    if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = target,
                            UseShellExecute = true
                        });
                        ActivityLogger.Instance.Log($"Launched startup application: {item.Name}", ActivityType.Info);
                    }
                    else if (!string.IsNullOrWhiteSpace(item.Command))
                    {
                        var clean = Environment.ExpandEnvironmentVariables(item.Command.Trim('\"'));
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = clean,
                            UseShellExecute = true
                        });
                        ActivityLogger.Instance.Log($"Launched startup application: {item.Name}", ActivityType.Info);
                    }
                    else
                    {
                        ThemedMessageBox.Show($"Could not resolve launch target for {item.Name}.", "Launch App", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    ThemedMessageBox.Show($"Could not open application: {ex.Message}", "Open Application", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is StartupItem item)
            {
                try
                {
                    var target = !string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath)
                        ? item.TargetFilePath
                        : ExtractExecutablePath(item.Command);

                    if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{target}\"",
                            UseShellExecute = true
                        });
                    }
                    else if (!string.IsNullOrWhiteSpace(item.Command))
                    {
                        var clean = Environment.ExpandEnvironmentVariables(item.Command.Trim('\"'));
                        var dir = Path.GetDirectoryName(clean);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = dir,
                                UseShellExecute = true
                            });
                        }
                    }
                }
                catch { }
            }
        }

        public static string? ExtractExecutablePath(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            var trimmed = command.Trim();
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);

            // Case A: Quoted string e.g. "C:\Program Files\App\app.exe" --arg
            if (trimmed.StartsWith("\""))
            {
                var nextQuote = trimmed.IndexOf('\"', 1);
                if (nextQuote > 1)
                {
                    var candidate = trimmed.Substring(1, nextQuote - 1).Trim();
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // Case B: Unquoted path ending with .exe / .lnk / .bat
            var extensions = new[] { ".exe", ".lnk", ".bat", ".cmd" };
            foreach (var ext in extensions)
            {
                var idx = 0;
                while ((idx = trimmed.IndexOf(ext, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var candidate = trimmed.Substring(0, idx + ext.Length).Trim('\"', ' ');
                    if (File.Exists(candidate)) return candidate;
                    idx += ext.Length;
                }
            }

            // Case C: First whitespace token
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var candidate = trimmed.Substring(0, spaceIdx).Trim('\"', ' ');
                if (File.Exists(candidate)) return candidate;
            }

            // Case D: Entire string
            var clean = trimmed.Trim('\"', ' ');
            if (File.Exists(clean)) return clean;

            return null;
        }

        private static string? ResolveFromAppPaths(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            var clean = query.Trim('\"', ' ');
            var tokens = new List<string> { clean };
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(clean + ".exe");
            }

            var firstWord = clean.Split(' ', '-', '_').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstWord))
            {
                tokens.Add(firstWord);
                if (!firstWord.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(firstWord + ".exe");
                }
            }

            foreach (var token in tokens)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{token}")
                                 ?? Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{token}");
                    var raw = key?.GetValue("")?.ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var path = raw.Trim('\"', ' ');
                        if (File.Exists(path)) return path;
                    }
                }
                catch { }
            }

            return null;
        }

        private void OfficialLinkContainer_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ContentControl cc && cc.DataContext is StartupItem item && cc.Content == null)
            {
                cc.Content = RowHelpers.BuildStartupLinkButton(item);
            }
        }
    }
}
