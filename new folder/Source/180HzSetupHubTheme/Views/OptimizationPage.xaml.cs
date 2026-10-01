using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class OptimizationPage : UserControl
    {
        private readonly TweakService _tweakService = new();
        private List<TweakItem> _allTweaks = new();
        private string _activeCategory = "All";

        public OptimizationPage()
        {
            InitializeComponent();

            Loaded += async (_, _) =>
            {
                if (_allTweaks.Count == 0)
                    await InitializeAsync();
            };
        }

        private async Task InitializeAsync()
        {
            _allTweaks = await _tweakService.GetAllAsync();

            // Build category chips
            var categories = _allTweaks
                .Select(t => t.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => _allTweaks.First(t => t.Category == c).CategoryOrder)
                .ToList();

            CategoryChipsPanel.Children.Clear();

            // "All" chip
            var allChip = new RadioButton
            {
                Style = (Style)FindResource("TweakCategoryChip"),
                Content = "All",
                IsChecked = true,
            };
            allChip.Checked += (_, _) => { _activeCategory = "All"; ApplyFilter(); };
            CategoryChipsPanel.Children.Add(allChip);

            foreach (var cat in categories)
            {
                var chip = new RadioButton
                {
                    Style = (Style)FindResource("TweakCategoryChip"),
                    Content = cat,
                };
                var catCapture = cat;
                chip.Checked += (_, _) => { _activeCategory = catCapture; ApplyFilter(); };
                CategoryChipsPanel.Children.Add(chip);
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<TweakItem> filtered = _allTweaks;

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(t => string.Equals(t.Category, _activeCategory, StringComparison.OrdinalIgnoreCase));

            var list = filtered.OrderBy(t => t.Name).ToList();

            TweaksItemsControl.ItemsSource = list;
            EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            bool hasAdvanced = list.Any(t => t.IsAdvanced);
            AdvancedWarningBanner.Visibility = hasAdvanced ? Visibility.Visible : Visibility.Collapsed;

            int total = _allTweaks.Count;
            int recCount = _allTweaks.Count(t => t.IsRecommended);
            TxtSubtitle.Text = $"{total} tweaks available • {recCount} recommended safe";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in GetCurrentItems())
                t.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _allTweaks)
                t.IsSelected = false;
        }

        private void BtnSelectRecommended_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _allTweaks)
                t.IsSelected = t.IsRecommended;
        }

        private void BtnApplyRecommendation_Click(object sender, RoutedEventArgs e)
        {
            var recommended = _allTweaks.Where(t => t.IsRecommended).ToList();
            if (recommended.Count == 0) return;

            foreach (var t in _allTweaks)
                t.IsSelected = t.IsRecommended;

            var dialog = new OptimizationDialog(
                title: "Apply Recommended Optimizations",
                subtitle: $"This will apply {recommended.Count} recommended optimizations for peak desktop velocity & system cleanliness:",
                tweaks: recommended,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStatus.Text = $"✓ Applied {recommended.Count} recommended optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(recommended);
            }
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OptimizationDialog(
                title: "Restore Default Settings",
                subtitle: $"This will revert all {_allTweaks.Count} system tweaks and restore standard Windows defaults:",
                tweaks: _allTweaks,
                isUndo: true,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var t in _allTweaks)
                    t.IsSelected = false;

                TxtStatus.Text = $"✓ Restored all {_allTweaks.Count} tweaks to Windows defaults.";
                StatusBar.Visibility = Visibility.Visible;
            }
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OptimizationDialog(
                title: "Apply All Optimizations",
                subtitle: $"This will apply all {_allTweaks.Count} optimizations across all categories to your system:",
                tweaks: _allTweaks,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var t in _allTweaks)
                    t.IsSelected = true;

                TxtStatus.Text = $"✓ Applied all {_allTweaks.Count} optimizations successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(_allTweaks);
            }
        }

        private void BtnApplySelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allTweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("No tweaks selected. Toggle the tweaks you want to apply first.",
                    "180Hz Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OptimizationDialog(
                title: "Apply Selected Optimizations",
                subtitle: $"This will apply {selected.Count} selected optimization(s):",
                tweaks: selected,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStatus.Text = $"✓ Applied {selected.Count} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(selected);
            }
        }

        private void BtnUndoSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allTweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("No tweaks selected to undo.",
                    "180Hz Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OptimizationDialog(
                title: "Undo Selected Optimizations",
                subtitle: $"This will revert {selected.Count} selected optimization(s) back to standard Windows defaults:",
                tweaks: selected,
                isUndo: true,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStatus.Text = $"✓ Undone {selected.Count} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
            }
        }

        private void CheckRebootNeeded(List<TweakItem> tweaks)
        {
            if (tweaks.Any(t => t.RequiresReboot))
            {
                var r = MessageBox.Show(
                    "Some tweaks require a system restart to take full effect.\n\nRestart now?",
                    "Restart Required", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("shutdown", "/r /t 30 /c \"180Hz Setup Hub: Optimization tweaks applied.\"");
            }
        }

        private IEnumerable<TweakItem> GetCurrentItems()
        {
            if (TweaksItemsControl.ItemsSource is IEnumerable<TweakItem> items)
                return items;
            return _allTweaks;
        }
    }
}
