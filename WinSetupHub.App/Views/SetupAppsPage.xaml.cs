using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class SetupAppsPage : UserControl
    {
        private readonly PackageCatalogService _catalog = new();
        private readonly WingetService _winget = new();
        private List<AppItem> _allPackages = new();
        private string _activeCategory = "All";

        public SetupAppsPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await InitializeCatalogAsync();
        }

        private async Task InitializeCatalogAsync()
        {
            StatusText.Text = "Loading application catalog…";
            StatusText.Visibility = Visibility.Visible;

            _allPackages = await _catalog.GetAllAsync();
            CatalogCountText.Text = $"{_allPackages.Count} Packages Ready";

            BuildCategoryChips();
            ApplyFilter();

            // Background check for installed packages without blocking UI
            _ = Task.Run(async () =>
            {
                await _catalog.CheckInstalledStatusAsync(_winget, _allPackages);
                await Dispatcher.InvokeAsync(ApplyFilter);
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
            RenderList(results);
        }

        private void RenderList(List<AppItem> items)
        {
            ResultsList.Items.Clear();

            if (items.Count == 0)
            {
                StatusText.Text = "No packages match your search or category filter.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            StatusText.Visibility = Visibility.Collapsed;
            foreach (var app in items)
            {
                ResultsList.Items.Add(BuildRow(app));
            }
        }

        private Border BuildRow(AppItem app)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. Logo / Monogram
            var logoBorder = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("BrushBackground"),
                BorderBrush = (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            var logoGrid = new Grid();

            // Fallback monogram
            var initial = !string.IsNullOrWhiteSpace(app.Name) ? app.Name[0].ToString().ToUpperInvariant() : "•";
            var fallbackText = new TextBlock
            {
                Text = initial,
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushAccent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoGrid.Children.Add(fallbackText);

            if (!string.IsNullOrWhiteSpace(app.IconUrl) && Uri.TryCreate(app.IconUrl, UriKind.Absolute, out var uri))
            {
                try
                {
                    var img = new Image
                    {
                        Width = 32,
                        Height = 32,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uri;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.DecodePixelHeight = 64;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();

                    img.Source = bitmap;
                    logoGrid.Children.Add(img);
                }
                catch
                {
                    // Fallback to initial
                }
            }

            logoBorder.Child = logoGrid;
            Grid.SetColumn(logoBorder, 0);
            grid.Children.Add(logoBorder);

            // 2. Info Panel
            var infoPanel = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };

            // Title row
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            var nameText = new TextBlock
            {
                Text = app.Name,
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(nameText);

            // Category tag
            var catBorder = new Border
            {
                Background = (Brush)FindResource("BrushBackground"),
                BorderBrush = (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            catBorder.Child = new TextBlock
            {
                Text = app.Category,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextSecondary")
            };
            titleRow.Children.Add(catBorder);

            if (app.Essential)
            {
                var starBorder = new Border
                {
                    Background = (Brush)FindResource("BrushBackground"),
                    BorderBrush = (Brush)FindResource("BrushAccent"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                starBorder.Child = new TextBlock
                {
                    Text = "★ Essential",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("BrushAccent")
                };
                titleRow.Children.Add(starBorder);
            }

            infoPanel.Children.Add(titleRow);

            // Description row
            if (!string.IsNullOrWhiteSpace(app.Description))
            {
                var descText = new TextBlock
                {
                    Text = app.Description,
                    Style = (Style)FindResource("TextBody"),
                    FontSize = 12,
                    LineHeight = 16,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                infoPanel.Children.Add(descText);
            }

            // Metadata row: Version, Size, Web link, ID
            var metaRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Version Pill
            var versionPill = CreateMetaPill($"🏷️ {app.Version}");
            metaRow.Children.Add(versionPill);

            // Size Pill
            var sizePill = CreateMetaPill($"💾 {app.Size}");
            metaRow.Children.Add(sizePill);

            // Web link button (Opens in Chrome / Default browser)
            if (!string.IsNullOrWhiteSpace(app.WebUrl))
            {
                var webBtn = new Button
                {
                    Content = "🌐 Official Website",
                    Style = (Style)FindResource("WebLinkButton"),
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = $"Open {app.WebUrl} in browser"
                };
                webBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = app.WebUrl,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Ignore browser launch failure
                    }
                };
                metaRow.Children.Add(webBtn);
            }

            // ID Text
            var idText = new TextBlock
            {
                Text = app.Id,
                Style = (Style)FindResource("TextBody"),
                FontSize = 11,
                Foreground = (Brush)FindResource("BrushTextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            metaRow.Children.Add(idText);

            infoPanel.Children.Add(metaRow);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // 3. Actions Panel
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var installButton = new Button
            {
                Content = app.IsInstalled ? "Installed ✓" : "Install",
                Style = (Style)FindResource(app.IsInstalled ? "OutlineButton" : "AccentButton"),
                Padding = new Thickness(18, 8, 18, 8),
                MinHeight = 36,
                IsEnabled = !app.IsInstalled && !app.IsBusy
            };

            installButton.Click += async (_, _) =>
            {
                if (app.IsBusy || app.IsInstalled) return;

                app.IsBusy = true;
                installButton.IsEnabled = false;
                installButton.Content = "Installing…";

                var success = await _winget.InstallAsync(app.Id);

                app.IsBusy = false;
                if (success)
                {
                    app.IsInstalled = true;
                    installButton.Content = "Installed ✓";
                    installButton.Style = (Style)FindResource("OutlineButton");
                    ActivityLogger.Instance.Log($"Successfully installed {app.Name}.", ActivityType.Success);
                }
                else
                {
                    installButton.Content = "Retry Install";
                    installButton.IsEnabled = true;
                    ActivityLogger.Instance.Log($"Failed to install {app.Name}.", ActivityType.Error);
                }
            };

            actionsPanel.Children.Add(installButton);

            Grid.SetColumn(actionsPanel, 2);
            grid.Children.Add(actionsPanel);

            return new Border
            {
                Style = (Style)FindResource("ListRow"),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid
            };
        }

        private Border CreateMetaPill(string text)
        {
            var border = new Border
            {
                Background = (Brush)FindResource("BrushBackground"),
                BorderBrush = (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextSecondary")
            };
            return border;
        }

        private void Search_Click(object sender, RoutedEventArgs e) => ApplyFilter();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ApplyFilter();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            ApplyFilter();
        }

        private async void WingetSearch_Click(object sender, RoutedEventArgs e)
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                StatusText.Text = "Please enter an app name to search the web/winget repository.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            WingetSearchButton.IsEnabled = false;
            WingetSearchButton.Content = "Searching…";
            StatusText.Text = $"Searching online winget repository for \"{query}\"…";
            StatusText.Visibility = Visibility.Visible;

            var onlineResults = await _winget.SearchAsync(query);

            WingetSearchButton.IsEnabled = true;
            WingetSearchButton.Content = "🌐 Deep Web Search";

            if (onlineResults.Count == 0)
            {
                StatusText.Text = $"No online packages found matching \"{query}\".";
                return;
            }

            // Format online results with web link and realistic size
            foreach (var item in onlineResults)
            {
                if (string.IsNullOrWhiteSpace(item.Category) || item.Category == "General")
                    item.Category = "Online";
                if (string.IsNullOrWhiteSpace(item.Size))
                    item.Size = "~45 MB";
                if (string.IsNullOrWhiteSpace(item.WebUrl))
                    item.WebUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(item.Name + " download")}";
            }

            RenderList(onlineResults);
            CatalogCountText.Text = $"{onlineResults.Count} Online Results";
        }
    }
}
