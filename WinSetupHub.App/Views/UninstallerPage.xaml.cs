using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UninstallerPage : UserControl
    {
        private readonly WingetService _winget = new();
        private readonly PackageCatalogService _catalog = new();
        private List<AppItem> _allApps = new();
        private AppItem? _targetApp;
        private Border? _targetRow;

        public UninstallerPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            ResultsList.Items.Clear();
            StatusText.Text = "Scanning installed applications…";
            StatusText.Visibility = Visibility.Visible;
            InstalledCountText.Text = "Scanning…";

            _allApps = await _winget.GetInstalledAppsAsync();
            var presetCatalog = await _catalog.GetAllAsync();

            // Enrich all installed apps with logo, size, and category
            foreach (var app in _allApps)
            {
                AppMetadataHelper.EnrichAppItem(app, presetCatalog);
            }

            InstalledCountText.Text = $"{_allApps.Count} Applications Installed";
            ApplyFilter();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearFilterButton.Visibility = string.IsNullOrWhiteSpace(FilterBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ApplyFilter();
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

            Render(filtered);
        }

        private void Render(List<AppItem> apps)
        {
            ResultsList.Items.Clear();

            if (apps.Count == 0)
            {
                StatusText.Text = "No installed applications match your filter.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            StatusText.Visibility = Visibility.Collapsed;
            foreach (var app in apps)
            {
                ResultsList.Items.Add(BuildRow(app));
            }
        }

        private Border BuildRow(AppItem app)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
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
                BorderThickness = new Thickness(1),
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

            // 2. Info
            var infoPanel = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };

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
                    BorderThickness = new Thickness(1),
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

            var metaRow = new StackPanel { Orientation = Orientation.Horizontal };
            metaRow.Children.Add(CreateMetaPill($"🏷️ {app.Version}"));
            metaRow.Children.Add(CreateMetaPill($"💾 {app.Size}"));

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

            // 3. Uninstall Button
            var uninstallButton = new Button
            {
                Content = "Uninstall",
                Style = (Style)FindResource("DangerOutlineButton"),
                Padding = new Thickness(16, 6, 16, 6),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new Border
            {
                Style = (Style)FindResource("ListRow"),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid
            };

            uninstallButton.Click += (_, _) =>
            {
                ShowUninstallWarning(app, row);
            };

            Grid.SetColumn(uninstallButton, 2);
            grid.Children.Add(uninstallButton);

            return row;
        }

        private void ShowUninstallWarning(AppItem app, Border row)
        {
            _targetApp = app;
            _targetRow = row;

            DialogAppNameText.Text = app.Name;
            DialogVersionText.Text = $"🏷️ {app.Version}";
            DialogSizeText.Text = $"💾 {app.Size}";
            DialogIdText.Text = app.Id;

            DialogLogoGrid.Children.Clear();
            var initial = !string.IsNullOrWhiteSpace(app.Name) ? app.Name[0].ToString().ToUpperInvariant() : "•";
            var fallback = new TextBlock
            {
                Text = initial,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushAccent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            DialogLogoGrid.Children.Add(fallback);

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
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uri;
                    bitmap.DecodePixelWidth = 60;
                    bitmap.DecodePixelHeight = 60;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    img.Source = bitmap;
                    DialogLogoGrid.Children.Add(img);
                }
                catch { }
            }

            ConfirmUninstallButton.IsEnabled = true;
            ConfirmUninstallButton.Content = "Yes, Uninstall";
            WarningOverlay.Visibility = Visibility.Visible;
        }

        private void CancelUninstall_Click(object sender, RoutedEventArgs e)
        {
            WarningOverlay.Visibility = Visibility.Collapsed;
            _targetApp = null;
            _targetRow = null;
        }

        private async void ConfirmUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_targetApp == null) return;

            var appToUninstall = _targetApp;
            var rowToRemove = _targetRow;

            ConfirmUninstallButton.IsEnabled = false;
            ConfirmUninstallButton.Content = "Removing…";

            var success = await _winget.UninstallAsync(appToUninstall.Id);

            WarningOverlay.Visibility = Visibility.Collapsed;

            if (success)
            {
                ActivityLogger.Instance.Log($"Uninstalled {appToUninstall.Name}.", ActivityType.Success);
                _allApps.Remove(appToUninstall);
                if (rowToRemove != null)
                {
                    ResultsList.Items.Remove(rowToRemove);
                }
                InstalledCountText.Text = $"{_allApps.Count} Applications Installed";
            }
            else
            {
                ActivityLogger.Instance.Log($"Failed to uninstall {appToUninstall.Name}.", ActivityType.Error);
                MessageBox.Show($"Failed to uninstall {appToUninstall.Name}. Please try running as Administrator.",
                    "Uninstall Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _targetApp = null;
            _targetRow = null;
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
    }
}
