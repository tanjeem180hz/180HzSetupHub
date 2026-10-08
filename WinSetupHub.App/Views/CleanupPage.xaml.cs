using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class CleanupPage : UserControl, IRealtimeRefreshable
    {
        private readonly CleanupService _cleanup = CleanupService.Instance;
        private readonly Dictionary<CheckBox, CleanupTarget> _rowMap = new();
        private readonly DispatcherTimer _ramTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        public CleanupPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                _cleanup.ScanCompleted -= OnScanCompleted;
                _cleanup.ScanCompleted += OnScanCompleted;

                _ramTimer.Tick -= OnRamTimerTick;
                _ramTimer.Tick += OnRamTimerTick;
                _ramTimer.Start();
                UpdateRamStats();

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
                _ramTimer.Stop();
                _ramTimer.Tick -= OnRamTimerTick;
            };

            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue)
                {
                    if (!_ramTimer.IsEnabled) _ramTimer.Start();
                    UpdateRamStats();
                }
                else
                {
                    _ramTimer.Stop();
                }
            };
        }

        private void OnRamTimerTick(object? sender, EventArgs e)
        {
            UpdateRamStats();
        }

        private void UpdateRamStats()
        {
            try
            {
                long total = MemoryCleaner.GetTotalMemoryBytes();
                long avail = MemoryCleaner.GetAvailableMemoryBytes();
                long used = Math.Max(0, total - avail);

                double pct = total > 0 ? (used / (double)total) * 100.0 : 0;
                double usedGb = used / 1024.0 / 1024.0 / 1024.0;
                double totalGb = total / 1024.0 / 1024.0 / 1024.0;
                double availGb = avail / 1024.0 / 1024.0 / 1024.0;

                if (RamUsageDisplay != null)
                    RamUsageDisplay.Text = $"Memory In Use: {usedGb:0.0} / {totalGb:0.0} GB";

                if (RamPercentDisplay != null)
                    RamPercentDisplay.Text = $" ({pct:0}%)";

                if (RamAvailableDisplay != null)
                    RamAvailableDisplay.Text = $"{availGb:0.0} GB Available";

                if (RamProgressBar != null)
                    RamProgressBar.Value = Math.Clamp(pct, 0, 100);
            }
            catch { }
        }

        public void RefreshRealtime()
        {
            UpdateRamStats();

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
            UpdateRamStats();
            var targets = await _cleanup.ScanAsync(force: true);
            UpdateDisplay(targets);
        }

        private async void QuickCleanRam_Click(object sender, RoutedEventArgs e)
        {
            if (QuickCleanRamButton == null) return;

            QuickCleanRamButton.IsEnabled = false;
            QuickCleanRamButton.Tag = "Clearing RAM…";

            long freed = await Task.Run(() => MemoryCleaner.CleanRam());
            UpdateRamStats();

            string freedDisplay = FormatSize(freed);
            QuickCleanRamButton.Tag = $"✓ {freedDisplay} Freed";

            ActivityLogger.Instance.Log($"RAM Cleaner: Purged cache & reclaimed {freedDisplay} physical memory.", ActivityType.Success);
            NotificationService.Notify("RAM Cleaned", $"Successfully reclaimed {freedDisplay} physical memory & purged cache.");

            // Refresh targets list so RAM Cache size reflects the clean state
            _ = _cleanup.ScanAsync(force: true);

            await Task.Delay(2500);
            QuickCleanRamButton.Tag = "Clean RAM";
            QuickCleanRamButton.IsEnabled = true;
        }

        private async void CleanSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _rowMap.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            if (selected.Count == 0) return;

            CleanSelectedButton.IsEnabled = false;
            CleanSelectedButton.Tag = "Cleaning…";

            long freed = await _cleanup.CleanAllAsync(selected);
            UpdateRamStats();

            ActivityLogger.Instance.Log($"Cleanup freed {FormatSize(freed)} of storage & memory.", ActivityType.Success);
            NotificationService.Notify("Cleanup Complete", $"Cleanup freed {FormatSize(freed)} of storage & memory.");

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

            var nameStack = new StackPanel { Orientation = Orientation.Horizontal };

            var nameText = new TextBlock
            {
                Text = target.Name,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushTextPrimary"],
                FontWeight = FontWeights.SemiBold,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFont"],
                VerticalAlignment = VerticalAlignment.Center
            };
            nameStack.Children.Add(nameText);

            if (target.IsRamTarget)
            {
                var ramBadge = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, 0x00, 0xFF, 0x66)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x4D, 0x00, 0xFF, 0x66)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ramBadge.Child = new TextBlock
                {
                    Text = "⚡ RAM",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushAccent"]
                };
                nameStack.Children.Add(ramBadge);
            }

            var pathText = new TextBlock { Text = target.Path, Style = (Style)Application.Current.Resources["TextBody"] };

            var textPanel = new StackPanel();
            textPanel.Children.Add(nameStack);
            textPanel.Children.Add(pathText);

            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // For the RAM target, provide an individual quick-clear button on the row
            if (target.IsRamTarget)
            {
                var quickRowBtn = new Button
                {
                    Content = "⚡ Clear",
                    Style = (Style)Application.Current.Resources["OutlineButton"],
                    Height = 24,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Purge process working sets & RAM cache immediately"
                };

                quickRowBtn.Click += async (_, _) =>
                {
                    quickRowBtn.IsEnabled = false;
                    quickRowBtn.Content = "Clearing…";

                    long freed = await Task.Run(() => MemoryCleaner.CleanRam());
                    UpdateRamStats();

                    quickRowBtn.Content = $"✓ {FormatSize(freed)}";
                    ActivityLogger.Instance.Log($"RAM Cleaner: Purged cache & reclaimed {FormatSize(freed)} physical memory.", ActivityType.Success);
                    NotificationService.Notify("RAM Cleaned", $"Successfully reclaimed {FormatSize(freed)} physical memory.");

                    _ = _cleanup.ScanAsync(force: true);

                    await Task.Delay(2500);
                    quickRowBtn.Content = "⚡ Clear";
                    quickRowBtn.IsEnabled = true;
                };

                rightPanel.Children.Add(quickRowBtn);
            }

            var sizeText = new TextBlock
            {
                Text = FormatSize(target.SizeBytes),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushAccent"],
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            rightPanel.Children.Add(sizeText);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(textPanel, 1);
            Grid.SetColumn(rightPanel, 2);
            grid.Children.Add(checkBox);
            grid.Children.Add(textPanel);
            grid.Children.Add(rightPanel);

            var rowBorder = new Border { Style = (Style)Application.Current.Resources["ListRow"], Child = grid };

            if (target.IsRamTarget)
            {
                rowBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x00, 0xFF, 0x66));
            }

            return rowBorder;
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            return mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
        }
    }
}
