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

            // Reset scroll position to top whenever category is changed
            TweaksScrollViewer?.ScrollToTop();
            TweaksScrollViewer?.ScrollToVerticalOffset(0);

            bool hasAdvanced = list.Any(t => t.IsAdvanced);
            AdvancedWarningBanner.Visibility = hasAdvanced ? Visibility.Visible : Visibility.Collapsed;

            int total = _allTweaks.Count;
            int currentCount = list.Count;
            int currentRec = list.Count(t => t.IsRecommended);

            if (string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                TxtSubtitle.Text = $"{total} tweaks available • {_allTweaks.Count(t => t.IsRecommended)} recommended safe";
                BtnSelectRecommended.ToolTip = "Select recommended tweaks across all categories";
                BtnApplyRecommendation.ToolTip = "Apply all recommended optimizations instantly";
            }
            else
            {
                TxtSubtitle.Text = $"{_activeCategory} • {currentCount} tweaks ({currentRec} recommended)";
                BtnSelectRecommended.ToolTip = $"Select recommended tweaks in {_activeCategory}";
                BtnApplyRecommendation.ToolTip = $"Apply recommended optimizations for {_activeCategory}";
            }
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in GetCurrentItems())
                t.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            // Category-wise deselect if in a category, otherwise all
            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var t in GetCurrentItems())
                    t.IsSelected = false;
            }
            else
            {
                foreach (var t in _allTweaks)
                    t.IsSelected = false;
            }
        }

        private void BtnSelectRecommended_Click(object sender, RoutedEventArgs e)
        {
            var currentItems = GetCurrentItems().ToList();
            var recItems = currentItems.Where(t => t.IsRecommended).ToList();

            if (recItems.Count > 0)
            {
                foreach (var t in currentItems)
                    t.IsSelected = t.IsRecommended;
            }
            else
            {
                // If a category has no items specifically tagged, select non-advanced safe items
                foreach (var t in currentItems)
                    t.IsSelected = !t.IsAdvanced;
            }
        }

        private void BtnApplyRecommendation_Click(object sender, RoutedEventArgs e)
        {
            List<TweakItem> recommended;
            string categoryLabel = "";

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                var currentItems = GetCurrentItems().ToList();
                recommended = currentItems.Where(t => t.IsRecommended).ToList();
                if (recommended.Count == 0)
                    recommended = currentItems.Where(t => !t.IsAdvanced).ToList();
                categoryLabel = $" ({_activeCategory})";
            }
            else
            {
                recommended = _allTweaks.Where(t => t.IsRecommended).ToList();
            }

            if (recommended.Count == 0)
            {
                ThemedMessageBox.Show("No recommended tweaks found for the current selection.",
                    "180Hz Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var t in GetCurrentItems())
                t.IsSelected = recommended.Contains(t);

            var dialog = new OptimizationDialog(
                title: $"Apply Recommended Optimizations{categoryLabel}",
                subtitle: $"This will apply {recommended.Count} recommended optimizations for {(_activeCategory == "All" ? "peak system velocity & responsiveness" : _activeCategory)}:",
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
            List<TweakItem> targetTweaks;
            string scopeText;

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                targetTweaks = GetCurrentItems().ToList();
                scopeText = $"these {targetTweaks.Count} tweaks in {_activeCategory}";
            }
            else
            {
                targetTweaks = _allTweaks;
                scopeText = $"all {_allTweaks.Count} system tweaks";
            }

            var dialog = new OptimizationDialog(
                title: "Restore Default Settings",
                subtitle: $"This will revert {scopeText} back to standard Windows factory defaults:",
                tweaks: targetTweaks,
                isUndo: true,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var t in targetTweaks)
                    t.IsSelected = false;

                TxtStatus.Text = $"✓ Restored {targetTweaks.Count} tweak(s) to Windows defaults.";
                StatusBar.Visibility = Visibility.Visible;
            }
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            List<TweakItem> targetTweaks;
            string scopeText;

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                targetTweaks = GetCurrentItems().ToList();
                scopeText = $"all {targetTweaks.Count} tweaks in {_activeCategory}";
            }
            else
            {
                targetTweaks = _allTweaks;
                scopeText = $"all {_allTweaks.Count} optimizations across all categories";
            }

            var dialog = new OptimizationDialog(
                title: "Apply Optimizations",
                subtitle: $"This will apply {scopeText} to your system:",
                tweaks: targetTweaks,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var t in targetTweaks)
                    t.IsSelected = true;

                TxtStatus.Text = $"✓ Applied {targetTweaks.Count} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(targetTweaks);
            }
        }

        private void BtnApplySelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allTweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ThemedMessageBox.Show("No tweaks selected. Toggle the tweaks you want to apply first.",
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
                ThemedMessageBox.Show("No tweaks selected to undo.",
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
                var r = ThemedMessageBox.Show(
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
