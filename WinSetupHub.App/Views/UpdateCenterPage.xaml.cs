using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UpdateCenterPage : UserControl
    {
        private readonly WingetService _winget = new();
        private readonly PackageCatalogService _catalogService = new();
        private List<AppItem>? _catalog;

        public UpdateCenterPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            ResultsList.Items.Clear();
            SummaryText.Text = "Checking for updates via Winget…";
            UpgradeAllButton.IsEnabled = false;

            _catalog ??= await _catalogService.GetAllAsync();

            var upgradable = await _winget.GetUpgradableAppsAsync();

            if (upgradable.Count == 0)
            {
                SummaryText.Text = "🎉 Everything is up to date! No updates found.";
                return;
            }

            SummaryText.Text = $"🚀 {upgradable.Count} application(s) have updates available.";
            UpgradeAllButton.IsEnabled = true;

            foreach (var app in upgradable)
            {
                AppMetadataHelper.EnrichAppItem(app, _catalog);
                ResultsList.Items.Add(BuildRow(app));
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            UpgradeAllButton.IsEnabled = false;
            UpgradeAllButton.Content = "Upgrading All…";

            var success = await _winget.UpgradeAllAsync();

            ActivityLogger.Instance.Log(
                success ? "Upgraded all available applications." : "Upgrade-all finished with some warnings.",
                success ? ActivityType.Success : ActivityType.Warning);

            UpgradeAllButton.Content = "⚡ Upgrade All";
            await LoadAsync();
        }

        private Border BuildRow(AppItem app)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. Logo / Monogram
            var logoBorder = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("BrushBackground"),
                BorderBrush = (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1, 1, 1, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            var logoGrid = new Grid();
            var initial = !string.IsNullOrWhiteSpace(app.Name) ? app.Name[0].ToString().ToUpperInvariant() : "•";
            var fallbackText = new TextBlock
            {
                Text = initial,
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 16,
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
                        Width = 30,
                        Height = 30,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uri;
                    bitmap.DecodePixelWidth = 60;
                    bitmap.DecodePixelHeight = 60;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();

                    img.Source = bitmap;
                    logoGrid.Children.Add(img);
                }
                catch { }
            }

            logoBorder.Child = logoGrid;
            Grid.SetColumn(logoBorder, 0);
            grid.Children.Add(logoBorder);

            // 2. Info Panel
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(12, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
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

            if (!string.IsNullOrWhiteSpace(app.Source))
            {
                var srcBorder = new Border
                {
                    Background = (Brush)FindResource("BrushBackground"),
                    BorderBrush = (Brush)FindResource("BrushBorder"),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                srcBorder.Child = new TextBlock
                {
                    Text = app.Source.ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("BrushTextSecondary")
                };
                titleRow.Children.Add(srcBorder);
            }

            infoPanel.Children.Add(titleRow);

            // Meta Row (Version transition + Size + ID)
            var metaRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Version Pill: old -> new
            var targetVer = !string.IsNullOrWhiteSpace(app.AvailableVersion) ? app.AvailableVersion : "Latest";
            var verPill = CreateMetaPill($"🏷️ {app.Version}  ➜  {targetVer}", highlightTarget: true);
            metaRow.Children.Add(verPill);

            // Size Pill
            var sizeStr = !string.IsNullOrWhiteSpace(app.Size) ? app.Size : "~50 MB";
            metaRow.Children.Add(CreateMetaPill($"💾 {sizeStr}"));

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

            // 3. Action Button
            var upgradeButton = new Button
            {
                Content = "Upgrade",
                Style = (Style)FindResource("AccentButton"),
                Padding = new Thickness(16, 6, 16, 6),
                VerticalAlignment = VerticalAlignment.Center
            };

            upgradeButton.Click += async (_, _) =>
            {
                upgradeButton.IsEnabled = false;
                upgradeButton.Content = "Upgrading…";

                var success = await _winget.UpgradeAsync(app.Id);

                upgradeButton.Content = success ? "Updated ✓" : "Failed";
                ActivityLogger.Instance.Log(
                    success ? $"Upgraded {app.Name} to {targetVer}." : $"Failed to upgrade {app.Name}.",
                    success ? ActivityType.Success : ActivityType.Error);

                if (!success)
                {
                    upgradeButton.IsEnabled = true;
                    upgradeButton.Content = "Retry";
                }
            };

            Grid.SetColumn(upgradeButton, 2);
            grid.Children.Add(upgradeButton);

            return new Border
            {
                Style = (Style)FindResource("ListRow"),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid
            };
        }

        private Border CreateMetaPill(string text, bool highlightTarget = false)
        {
            var border = new Border
            {
                Background = (Brush)FindResource("BrushBackground"),
                BorderBrush = highlightTarget ? (Brush)FindResource("BrushAccent") : (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1, 1, 1, 1),
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
                Foreground = highlightTarget ? (Brush)FindResource("BrushAccent") : (Brush)FindResource("BrushTextSecondary")
            };
            return border;
        }
    }
}
