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

            // Show advanced warning if any advanced tweaks are visible
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

        private async void BtnApplyRecommendation_Click(object sender, RoutedEventArgs e)
        {
            var recommended = _allTweaks.Where(t => t.IsRecommended).ToList();
            if (recommended.Count == 0)
            {
                MessageBox.Show("No recommended tweaks found in configuration.",
                    "180Hz Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var t in _allTweaks)
                t.IsSelected = t.IsRecommended;

            var result = MessageBox.Show(
                $"This will apply {recommended.Count} recommended optimizations for peak desktop velocity & system cleanliness:\n\n" +
                string.Join("\n", recommended.Select(t => $"• {t.Name}")) +
                "\n\nApply these optimizations now?",
                "⚡ Apply Recommended Optimizations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            await RunTweaksAsync(recommended, undo: false);
        }

        private async void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will revert all 67 system tweaks and restore standard Windows defaults (services, registry keys, and preferences).\n\nAre you sure you want to restore defaults?",
                "Restore All Default Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            foreach (var t in _allTweaks)
                t.IsSelected = false;

            await RunTweaksAsync(_allTweaks, undo: true);
        }

        private async void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            var advanced = _allTweaks.Where(t => t.IsAdvanced).ToList();
            string msg = $"This will apply ALL {_allTweaks.Count} system optimizations across all categories";
            if (advanced.Count > 0)
            {
                msg += $", including {advanced.Count} Advanced Tweaks marked CAUTION.\n\nMake sure to create a System Restore point.\n\nProceed with full optimization?";
            }
            else
            {
                msg += ".\n\nProceed?";
            }

            var result = MessageBox.Show(msg, "Apply All Optimizations",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            foreach (var t in _allTweaks)
                t.IsSelected = true;

            await RunTweaksAsync(_allTweaks, undo: false);
        }

        private async void BtnApplySelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allTweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("No tweaks selected. Toggle the tweaks you want to apply first.",
                    "180Hz Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var advanced = selected.Where(t => t.IsAdvanced).ToList();
            if (advanced.Count > 0)
            {
                var msg = $"You have selected {advanced.Count} advanced tweak(s) marked with CAUTION:\n\n" +
                          string.Join("\n", advanced.Take(5).Select(t => $"• {t.Name}")) +
                          (advanced.Count > 5 ? $"\n...and {advanced.Count - 5} more" : "") +
                          "\n\nThese modify system-level settings and may affect Windows stability.\n" +
                          "Create a System Restore point before continuing.\n\nProceed?";

                var result = MessageBox.Show(msg, "⚠ Advanced Tweaks Warning",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            await RunTweaksAsync(selected, undo: false);
        }

        private async void BtnUndoSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allTweaks.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("No tweaks selected to undo.",
                    "180Hz Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RunTweaksAsync(selected, undo: true);
        }

        private async Task RunTweaksAsync(List<TweakItem> tweaks, bool undo)
        {
            SetButtonsEnabled(false);

            StatusBar.Visibility = Visibility.Visible;
            StatusLottie.Visibility = Visibility.Visible;
            string action = undo ? "Restoring" : "Applying";

            try
            {
                int done = 0;
                foreach (var tweak in tweaks)
                {
                    TxtStatus.Text = $"{action} ({done + 1}/{tweaks.Count}): {tweak.Name}";
                    await Task.Delay(30);

                    try
                    {
                        if (undo)
                            await _tweakService.UndoAsync(tweak);
                        else
                            await _tweakService.ApplyAsync(tweak);
                    }
                    catch (Exception ex)
                    {
                        TxtStatus.Text = $"Error on '{tweak.Name}': {ex.Message}";
                        await Task.Delay(1200);
                    }
                    done++;
                }

                TxtStatus.Text = undo
                    ? $"✓ Restored {done} tweak(s) to Windows defaults."
                    : $"✓ Applied {done} tweak(s) successfully. Some changes require a reboot.";

                StatusLottie.Visibility = Visibility.Collapsed;

                if (!undo && tweaks.Any(t => t.RequiresReboot))
                {
                    var r = MessageBox.Show(
                        "Some tweaks require a system restart to take full effect.\n\nRestart now?",
                        "Restart Required", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Yes)
                        System.Diagnostics.Process.Start("shutdown", "/r /t 30 /c \"180Hz Setup Hub: Optimization tweaks applied.\"");
                }
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            BtnApplyRecommendation.IsEnabled = enabled;
            BtnRestoreDefault.IsEnabled = enabled;
            BtnApplyAll.IsEnabled = enabled;
            BtnUndoSelected.IsEnabled = enabled;
            BtnSelectAll.IsEnabled = enabled;
            BtnDeselectAll.IsEnabled = enabled;
            BtnSelectRecommended.IsEnabled = enabled;
        }

        private IEnumerable<TweakItem> GetCurrentItems()
        {
            if (TweaksItemsControl.ItemsSource is IEnumerable<TweakItem> items)
                return items;
            return _allTweaks;
        }
    }
}
