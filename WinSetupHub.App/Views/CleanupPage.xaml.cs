using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class CleanupPage : UserControl
    {
        private readonly CleanupService _cleanup = new();
        private readonly Dictionary<CheckBox, CleanupTarget> _rowMap = new();

        public CleanupPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await ScanAsync();
        }

        private async System.Threading.Tasks.Task ScanAsync()
        {
            TargetsList.Items.Clear();
            _rowMap.Clear();
            SummaryText.Text = "Scanning…";
            CleanSelectedButton.IsEnabled = false;

            var targets = await _cleanup.ScanAsync();
            long totalBytes = targets.Sum(t => t.SizeBytes);

            SummaryText.Text = $"{FormatSize(totalBytes)} can potentially be freed.";
            CleanSelectedButton.IsEnabled = true;

            foreach (var target in targets)
                TargetsList.Items.Add(BuildRow(target));
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

        private async void CleanSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _rowMap.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            if (selected.Count == 0) return;

            CleanSelectedButton.IsEnabled = false;
            CleanSelectedButton.Content = "Cleaning…";

            long freed = await _cleanup.CleanAllAsync(selected);

            ActivityLogger.Instance.Log($"Cleanup freed {FormatSize(freed)}.", ActivityType.Success);
            NotificationService.Notify("Cleanup Complete", $"Cleanup freed {FormatSize(freed)} of disk space.");

            CleanSelectedButton.Content = "Clean Selected";
            await ScanAsync();
        }

        private Border BuildRow(CleanupTarget target)
        {
            var checkBox = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
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
