using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class CleanupPage : UserControl, IRealtimeRefreshable
    {
        private readonly CleanupService _cleanup = CleanupService.Instance;
        private readonly Dictionary<CheckBox, CleanupTarget> _rowMap = new();

        public CleanupPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                _cleanup.ScanCompleted -= OnScanCompleted;
                _cleanup.ScanCompleted += OnScanCompleted;

                if (_cleanup.CachedTargets is { } cached && cached.Count > 0)
                {
                    UpdateDisplay(cached);
                }
                else
                {
                    SummaryText.Text = "Scanning…";
                    CleanSelectedButton.IsEnabled = false;
                    _ = _cleanup.ScanAsync(force: false);
                }
            };

            Unloaded += (_, _) =>
            {
                _cleanup.ScanCompleted -= OnScanCompleted;
            };
        }

        public void RefreshRealtime()
        {
            if (_cleanup.CachedTargets is { } cached && cached.Count > 0)
            {
                UpdateDisplay(cached);
            }
            else if (!_cleanup.IsScanning)
            {
                _ = _cleanup.ScanAsync(force: false);
            }
        }

        private void OnScanCompleted(List<CleanupTarget> targets)
        {
            Dispatcher.InvokeAsync(() => UpdateDisplay(targets));
        }

        private void UpdateDisplay(List<CleanupTarget> targets)
        {
            // Preserve user selections if items are currently loaded
            var checkedStates = new Dictionary<string, bool>();
            foreach (var kv in _rowMap)
            {
                checkedStates[kv.Value.Name] = kv.Key.IsChecked ?? true;
            }

            TargetsList.Items.Clear();
            _rowMap.Clear();

            long totalBytes = targets.Sum(t => t.SizeBytes);
            SummaryText.Text = $"{FormatSize(totalBytes)} can potentially be freed.";
            CleanSelectedButton.IsEnabled = true;

            foreach (var target in targets)
            {
                bool isChecked = true;
                if (checkedStates.TryGetValue(target.Name, out bool wasChecked))
                {
                    isChecked = wasChecked;
                }
                TargetsList.Items.Add(BuildRow(target, isChecked));
            }
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            SummaryText.Text = "Scanning…";
            CleanSelectedButton.IsEnabled = false;
            var targets = await _cleanup.ScanAsync(force: true);
            UpdateDisplay(targets);
        }

        private async void CleanSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _rowMap.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            if (selected.Count == 0) return;

            CleanSelectedButton.IsEnabled = false;
            CleanSelectedButton.Tag = "Cleaning…";

            long freed = await _cleanup.CleanAllAsync(selected);

            ActivityLogger.Instance.Log($"Cleanup freed {FormatSize(freed)}.", ActivityType.Success);
            NotificationService.Notify("Cleanup Complete", $"Cleanup freed {FormatSize(freed)} of disk space.");

            CleanSelectedButton.Tag = "Clean Selected";
            CleanSelectedButton.IsEnabled = true;

            SummaryText.Text = "Updating sizes…";
            var targets = await _cleanup.ScanAsync(force: true);
            UpdateDisplay(targets);
        }

        private Border BuildRow(CleanupTarget target, bool isChecked = true)
        {
            var checkBox = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _rowMap[checkBox] = target;

            var nameText = new TextBlock
            {
                Text = target.Name,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushTextPrimary"],
                FontWeight = FontWeights.SemiBold,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFont"],
            };
            var pathText = new TextBlock { Text = target.Path, Style = (Style)Application.Current.Resources["TextBody"] };

            var textPanel = new StackPanel();
            textPanel.Children.Add(nameText);
            textPanel.Children.Add(pathText);

            var sizeText = new TextBlock
            {
                Text = FormatSize(target.SizeBytes),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushAccent"],
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(textPanel, 1);
            Grid.SetColumn(sizeText, 2);
            grid.Children.Add(checkBox);
            grid.Children.Add(textPanel);
            grid.Children.Add(sizeText);

            return new Border { Style = (Style)Application.Current.Resources["ListRow"], Child = grid };
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            return mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
        }
    }
}
