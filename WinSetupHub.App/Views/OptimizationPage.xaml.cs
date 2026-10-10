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
        private List<TweakItem> _systemTweaks = new();
        private List<TweakItem> _registryTweaks = new();

        private string _activeSection = "System"; // "System" or "Registry"
        private string _activeCategory = "All";
        private string _searchQuery = "";

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
            _systemTweaks = await _tweakService.GetSystemTweaksAsync();
            _registryTweaks = await _tweakService.GetRegistryTweaksAsync();

            BuildCategoryChips();
            ApplyFilter();
        }

        private void SectionTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string section)
            {
                _activeSection = section;
                _activeCategory = "All";

                if (string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase))
                {
                    TxtSectionDescription.Text = "Deep Windows Registry tweaks for latency, ping, scheduling, and hardware acceleration.";
                }
                else
                {
                    TxtSectionDescription.Text = "System, gaming presets, and desktop environment optimizations.";
                }

                BuildCategoryChips();
                ApplyFilter();
            }
        }

        private void BuildCategoryChips()
        {
            var sourceList = GetCurrentSectionItems();
            var categories = sourceList
                .Select(t => t.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => sourceList.FirstOrDefault(t => string.Equals(t.Category, c, StringComparison.OrdinalIgnoreCase))?.CategoryOrder ?? 99)
                .ToList();

            CategoryChipsPanel.Children.Clear();

            // "All" chip
            var allChip = new RadioButton
            {
                Style = (Style)FindResource("TweakCategoryChip"),
                Content = "All",
                IsChecked = string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase),
            };
            allChip.Checked += (_, _) => { _activeCategory = "All"; ApplyFilter(); };
            CategoryChipsPanel.Children.Add(allChip);

            foreach (var cat in categories)
            {
                var chip = new RadioButton
                {
                    Style = (Style)FindResource("TweakCategoryChip"),
                    Content = cat,
                    IsChecked = string.Equals(_activeCategory, cat, StringComparison.OrdinalIgnoreCase),
                };
                var catCapture = cat;
                chip.Checked += (_, _) => { _activeCategory = catCapture; ApplyFilter(); };
                CategoryChipsPanel.Children.Add(chip);
            }
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = FilterBox.Text.Trim();
            ClearFilterButton.Visibility = string.IsNullOrWhiteSpace(_searchQuery)
                ? Visibility.Collapsed
                : Visibility.Visible;

            ApplyFilter();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FilterBox.Text = string.Empty;
        }

        private void ApplyFilter()
        {
            var sourceList = GetCurrentSectionItems();
            IEnumerable<TweakItem> filtered = sourceList;

            // 1. Category Filter
            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(t => string.Equals(t.Category, _activeCategory, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Search Text Filter
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                filtered = filtered.Where(t =>
                    t.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    t.Description.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    t.Category.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    t.Registry.Any(r =>
                        r.Path.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                        r.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)));
            }

            var list = filtered.OrderBy(t => t.CategoryOrder).ThenBy(t => t.Name).ToList();

            TweaksItemsControl.ItemsSource = list;
            EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Reset scroll position to top
            TweaksScrollViewer?.ScrollToTop();

            bool hasAdvanced = list.Any(t => t.IsAdvanced);
            AdvancedWarningBanner.Visibility = hasAdvanced ? Visibility.Visible : Visibility.Collapsed;

            int totalSectionCount = sourceList.Count;
            int totalRecommended = sourceList.Count(t => t.IsRecommended);
            int currentCount = list.Count;
            int currentRec = list.Count(t => t.IsRecommended);

            string sectionName = string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase)
                ? "Registry Optimizations"
                : "System Tweaks";

            if (string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(_searchQuery))
            {
                TxtSubtitle.Text = $"{sectionName} • {totalSectionCount} tweaks available • {totalRecommended} recommended safe";
                BtnSelectRecommended.ToolTip = $"Select recommended safe tweaks in {sectionName}";
                if (BtnApplySelected != null) BtnApplySelected.ToolTip = $"Apply checked optimizations for {sectionName}";
            }
            else
            {
                string scope = string.IsNullOrWhiteSpace(_searchQuery) ? _activeCategory : $"Search: \"{_searchQuery}\"";
                TxtSubtitle.Text = $"{sectionName} › {scope} • {currentCount} tweaks ({currentRec} recommended)";
                BtnSelectRecommended.ToolTip = $"Select recommended safe tweaks in {scope}";
                if (BtnApplySelected != null) BtnApplySelected.ToolTip = $"Apply checked optimizations for {scope}";
            }
        }

        private List<TweakItem> GetCurrentSectionItems()
        {
            return string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase)
                ? _registryTweaks
                : _systemTweaks;
        }

        private IEnumerable<TweakItem> GetCurrentItems()
        {
            if (TweaksItemsControl.ItemsSource is IEnumerable<TweakItem> items)
                return items;
            return GetCurrentSectionItems();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in GetCurrentItems())
                t.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(_searchQuery))
            {
                foreach (var t in GetCurrentItems())
                    t.IsSelected = false;
            }
            else
            {
                foreach (var t in GetCurrentSectionItems())
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
                foreach (var t in currentItems)
                    t.IsSelected = !t.IsAdvanced;
            }
        }

        private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            BtnCreateRestorePoint.IsEnabled = false;
            TxtStatus.Text = "Creating Windows System Restore point checkpoint…";
            StatusBar.Visibility = Visibility.Visible;

            bool ok = await DeepUninstallService.CreateRestorePointAsync("180Hz Setup Hub: Optimization Safeguard");
            BtnCreateRestorePoint.IsEnabled = true;

            if (ok)
            {
                TxtStatus.Text = "✓ System Restore Point checkpoint created successfully.";
                ThemedMessageBox.Show("Windows System Restore Point created successfully.\nYour system state is safely checkpointed.",
                    "Safeguard Created", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtStatus.Text = "ℹ️ System Restore is disabled or unavailable on this system.";
                ThemedMessageBox.Show("Windows System Restore is disabled or unconfigured on this drive.\n(Safe to proceed with individual tweaks).",
                    "System Restore Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnApplySelected_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = GetCurrentSectionItems();
            var selected = sourceList.Where(t => t.IsSelected).ToList();
            string sectionLabel = string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase) ? "Registry" : "System";

            if (selected.Count == 0)
            {
                var prompt = ThemedMessageBox.Show(
                    $"No {sectionLabel.ToLowerInvariant()} optimizations are currently selected.\n\nWould you like to select and apply the RECOMMENDED optimizations now?",
                    $"Apply {sectionLabel} Optimizations", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (prompt == MessageBoxResult.Yes)
                {
                    BtnSelectRecommended_Click(sender, e);
                    selected = sourceList.Where(t => t.IsSelected).ToList();
                    if (selected.Count == 0) return;
                }
                else
                {
                    return;
                }
            }

            var dialog = new OptimizationDialog(
                title: $"Apply {selected.Count} Selected {sectionLabel} Optimizations",
                subtitle: $"This will safely apply {selected.Count} selected optimization(s) to maximize your PC performance:",
                tweaks: selected,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStatus.Text = $"✓ Applied {selected.Count} {sectionLabel.ToLowerInvariant()} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(selected);
            }
        }

        private void BtnApplyRecommendation_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = GetCurrentSectionItems();
            List<TweakItem> recommended;
            string sectionLabel = string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase) ? "Registry" : "System";
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
                recommended = sourceList.Where(t => t.IsRecommended).ToList();
            }

            if (recommended.Count == 0)
            {
                ThemedMessageBox.Show("No recommended tweaks found for the current selection.",
                    $"180Hz {sectionLabel} Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var t in GetCurrentItems())
                t.IsSelected = recommended.Contains(t);

            var dialog = new OptimizationDialog(
                title: $"Apply Recommended {sectionLabel} Optimizations{categoryLabel}",
                subtitle: $"This will apply {recommended.Count} recommended optimizations for peak performance & stability:",
                tweaks: recommended,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtStatus.Text = $"✓ Applied {recommended.Count} recommended {sectionLabel.ToLowerInvariant()} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(recommended);
            }
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = GetCurrentSectionItems();
            List<TweakItem> targetTweaks;
            string sectionLabel = string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase) ? "Registry" : "System";
            string scopeText;

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                targetTweaks = GetCurrentItems().ToList();
                scopeText = $"these {targetTweaks.Count} tweaks in {_activeCategory}";
            }
            else
            {
                targetTweaks = sourceList;
                scopeText = $"all {sourceList.Count} {sectionLabel.ToLowerInvariant()} optimizations";
            }

            var dialog = new OptimizationDialog(
                title: $"Restore Default {sectionLabel} Settings",
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

                TxtStatus.Text = $"✓ Restored {targetTweaks.Count} {sectionLabel.ToLowerInvariant()} tweak(s) to Windows defaults.";
                StatusBar.Visibility = Visibility.Visible;
            }
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = GetCurrentSectionItems();
            List<TweakItem> targetTweaks;
            string sectionLabel = string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase) ? "Registry" : "System";
            string scopeText;

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                targetTweaks = GetCurrentItems().ToList();
                scopeText = $"all {targetTweaks.Count} tweaks in {_activeCategory}";
            }
            else
            {
                targetTweaks = sourceList;
                scopeText = $"all {sourceList.Count} {sectionLabel.ToLowerInvariant()} optimizations";
            }

            var dialog = new OptimizationDialog(
                title: $"Apply {sectionLabel} Optimizations",
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

                TxtStatus.Text = $"✓ Applied {targetTweaks.Count} {sectionLabel.ToLowerInvariant()} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                CheckRebootNeeded(targetTweaks);
            }
        }

        private void BtnUndoSelected_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = GetCurrentSectionItems();
            var selected = sourceList.Where(t => t.IsSelected).ToList();
            string sectionLabel = string.Equals(_activeSection, "Registry", StringComparison.OrdinalIgnoreCase) ? "Registry" : "System";

            if (selected.Count == 0)
            {
                ThemedMessageBox.Show("No tweaks selected to undo in this section.",
                    $"180Hz {sectionLabel} Optimization", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OptimizationDialog(
                title: $"Undo Selected {sectionLabel} Optimizations",
                subtitle: $"This will revert {selected.Count} selected {sectionLabel.ToLowerInvariant()} optimization(s) back to standard Windows defaults:",
                tweaks: selected,
                isUndo: true,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var t in selected)
                    t.IsSelected = false;

                TxtStatus.Text = $"✓ Undone {selected.Count} {sectionLabel.ToLowerInvariant()} optimization(s) successfully.";
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
    }
}
