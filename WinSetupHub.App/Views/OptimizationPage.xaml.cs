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
            TxtSubtitle.Text = $"{total} tweaks available — toggle to select, then Apply or Undo";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in GetCurrentItems())
                t.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in GetCurrentItems())
                t.IsSelected = false;
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
            BtnApplySelected.IsEnabled = false;
            BtnUndoSelected.IsEnabled = false;
            BtnSelectAll.IsEnabled = false;
            BtnDeselectAll.IsEnabled = false;

            StatusBar.Visibility = Visibility.Visible;
            StatusLottie.Visibility = Visibility.Visible;
            string action = undo ? "Undoing" : "Applying";

            try
            {
                int done = 0;
                foreach (var tweak in tweaks)
                {
                    TxtStatus.Text = $"{action} ({done + 1}/{tweaks.Count}): {tweak.Name}";
                    await Task.Delay(30); // yield to UI

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
                    ? $"✓ Undone {done} tweak(s) successfully."
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
                BtnApplySelected.IsEnabled = true;
                BtnUndoSelected.IsEnabled = true;
                BtnSelectAll.IsEnabled = true;
                BtnDeselectAll.IsEnabled = true;
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
