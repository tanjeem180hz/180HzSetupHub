using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class LeftoverCleanupDialog : Window
    {
        private readonly AppItem _app;
        private readonly DeepUninstallService _deepUninstall = new();
        private readonly ObservableCollection<LeftoverViewModel> _viewModels = new();

        public LeftoverCleanupDialog(AppItem app, List<LeftoverItem> leftovers)
        {
            InitializeComponent();
            _app = app;

            DialogTitleText.Text = $"Residual Traces: {app.Name}";
            DialogSubtitleText.Text = $"Detected {leftovers.Count} registry and filesystem leftover(s) for {app.Name}.";

            foreach (var item in leftovers)
            {
                var vm = new LeftoverViewModel(item, app.Name);
                vm.PropertyChanged += (_, _) => UpdateCountDisplay();
                _viewModels.Add(vm);
            }

            LeftoversListBox.ItemsSource = _viewModels;
            UpdateCountDisplay();

            MouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        private void UpdateCountDisplay()
        {
            int total = _viewModels.Count;
            int selected = _viewModels.Count(v => v.IsSelected);
            CountSummaryText.Text = $"{selected} of {total} items selected for deletion";
            DeleteButton.Content = selected > 0 ? $"🗑️ Delete Selected ({selected})" : "🗑️ Delete Selected";
            DeleteButton.IsEnabled = selected > 0;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var vm in _viewModels)
            {
                vm.IsSelected = true;
            }
            UpdateCountDisplay();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var vm in _viewModels)
            {
                vm.IsSelected = false;
            }
            UpdateCountDisplay();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _viewModels.Where(v => v.IsSelected).Select(v => v.Item).ToList();
            if (selected.Count == 0) return;

            DeleteButton.IsEnabled = false;
            StatusLabel.Text = "Removing leftover items…";

            var (deleted, failed) = await _deepUninstall.DeleteAsync(selected);

            NotificationService.Notify(
                "Leftovers Cleaned",
                $"Removed {deleted} leftover trace(s) for {_app.Name}.");

            MessageBox.Show(
                $"Deep Clean Completed!\n\n• Removed: {deleted} item(s)\n• Skipped: {failed} item(s)",
                "Cleanup Finished",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Close();
        }
    }
}
