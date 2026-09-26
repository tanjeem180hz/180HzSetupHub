using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class StartupManagerPage : UserControl
    {
        private readonly StartupManagerService _startupService = new();
        private List<StartupItem> _items = new();

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
