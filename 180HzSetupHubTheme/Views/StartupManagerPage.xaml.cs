using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class StartupManagerPage : UserControl
    {
        private readonly StartupManagerService _startupService = new();
        private readonly PackageCatalogService _catalogService = new();
        private List<StartupItem> _items = new();
        private List<AppItem>? _catalogCache;

        public StartupManagerPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadItemsAsync();
        }

        private async Task LoadItemsAsync()
        {
            SummaryText.Text = "Scanning startup entries…";

            _items = await Task.Run(() => _startupService.GetStartupItems());

            var enabledCount = _items.Count(i => i.IsEnabled);
            SummaryText.Text = $"{_items.Count} startup apps registered ({enabledCount} enabled).";
            StartupList.ItemsSource = null;
            StartupList.ItemsSource = _items;

            // Background async icon extraction
            _ = Task.Run(async () =>
            {
                _catalogCache ??= await _catalogService.GetAllAsync();

                foreach (var item in _items)
                {
                    if (item.IconImageSource != null) continue;

                    ImageSource? icon = null;

                    // 1. Try extracting local .exe icon from command line
                    var exePath = ExtractExecutablePath(item.Command);
                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    {
                        // Try resolving from App Paths registry (e.g. "spotify.exe", "discord.exe")
                        exePath = ResolveFromAppPaths(item.Name) ?? ResolveFromAppPaths(item.Command);
                    }

                    if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                    {
                        item.LocalIconPath = exePath;
                        icon = IconCacheService.GetLocalFileIcon(exePath);
                    }

                    // 2. If no local icon extracted, match against catalog
                    if (icon == null && _catalogCache != null)
                    {
                        var matchedPkg = _catalogCache.FirstOrDefault(p =>
                            string.Equals(p.Name, item.Name, StringComparison.OrdinalIgnoreCase) ||
                            item.Name.Contains(p.Name, StringComparison.OrdinalIgnoreCase) ||
                            p.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(p.Id) && item.Name.Contains(p.Id.Split('.').Last(), StringComparison.OrdinalIgnoreCase)));

                        if (matchedPkg != null && !string.IsNullOrWhiteSpace(matchedPkg.IconUrl))
                        {
                            item.IconUrl = matchedPkg.IconUrl;
                            icon = await IconCacheService.GetImageAsync(matchedPkg.IconUrl, matchedPkg.LocalIconPath);
                        }
                    }

                    if (icon != null)
                    {
                        await Dispatcher.InvokeAsync(() => item.IconImageSource = icon);
                    }
                }
            });
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

            // Also check first word
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

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadItemsAsync();

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is StartupItem item)
            {
                var newEnabled = toggle.IsChecked == true;

                try
                {
                    _startupService.SetEnabled(item, newEnabled);

                    ActivityLogger.Instance.Log(
                        $"{(newEnabled ? "Enabled" : "Disabled")} startup application: {item.Name}",
                        ActivityType.Info);

                    var enabledCount = _items.Count(i => i.IsEnabled);
                    SummaryText.Text = $"{_items.Count} startup apps registered ({enabledCount} enabled).";
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Revert toggle
                    item.IsEnabled = !newEnabled;
                    toggle.IsChecked = !newEnabled;

                    MessageBox.Show(ex.Message, "Permission Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ActivityLogger.Instance.Log($"Permission denied modifying {item.Name} startup entry.", ActivityType.Warning);
                }
                catch (Exception ex)
                {
                    item.IsEnabled = !newEnabled;
                    toggle.IsChecked = !newEnabled;

                    MessageBox.Show($"Could not update startup item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ActivityLogger.Instance.Log($"Failed to toggle {item.Name}: {ex.Message}", ActivityType.Error);
                }
            }
        }
    }
}
