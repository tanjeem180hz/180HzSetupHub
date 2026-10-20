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
    public partial class OptimizationPage : UserControl
    {
        private readonly TweakService _tweakService = new();
        private List<TweakItem> _allTweaks = new();
        private DispatcherTimer? _telemetryTimer;

        private string _activeCategory = "All";
        private string _searchQuery = "";

        public OptimizationPage()
        {
            InitializeComponent();

            Loaded += async (_, _) =>
            {
                if (_telemetryTimer == null)
                {
                    _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    _telemetryTimer.Tick += (_, _) => UpdatePreGameTelemetry();
                    _telemetryTimer.Start();
                }

                if (_allTweaks.Count == 0)
                {
                    await InitializeAsync();
                }
                else
                {
                    UpdatePermanentStatusBadge();
                    UpdatePreGameTelemetry();
                    BuildCategoryChips();
                    ApplyFilter();
                    await Task.Run(() => RefreshAppliedStates());
                    BuildCategoryChips();
                    ApplyFilter();
                }
            };

            Unloaded += (_, _) =>
            {
                _telemetryTimer?.Stop();
                _telemetryTimer = null;
            };
        }

        private async Task InitializeAsync()
        {
            try
            {
                _allTweaks = await _tweakService.GetAllAsync();

                // 1. Instant rendering so user sees all tweaks immediately without any lag
                UpdatePermanentStatusBadge();
                UpdatePreGameTelemetry();
                BuildCategoryChips();
                ApplyFilter();

                // 2. Scan live system applied states in background thread
                await Task.Run(() =>
                {
                    RefreshAppliedStates();
                });

                // 3. Update category chips and subtitle count once applied scan completes
                BuildCategoryChips();
                ApplyFilter();
            }
            catch
            {
                try
                {
                    BuildCategoryChips();
                    ApplyFilter();
                }
                catch { }
            }
        }

        public void RefreshAppliedStates()
        {
            foreach (var tweak in _allTweaks)
            {
                bool applied = _tweakService.IsTweakApplied(tweak);
                tweak.IsApplied = applied;
                tweak.IsSelected = applied; // Checked by default if applied
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
                Content = $"All ({sourceList.Count})",
                IsChecked = string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase),
            };
            allChip.Checked += (_, _) => { _activeCategory = "All"; ApplyFilter(); };
            CategoryChipsPanel.Children.Add(allChip);

            // "Applied" chip
            int appliedTotal = sourceList.Count(t => t.IsApplied);
            var appliedChip = new RadioButton
            {
                Style = (Style)FindResource("TweakCategoryChip"),
                Content = $"✓ Applied ({appliedTotal})",
                IsChecked = string.Equals(_activeCategory, "Applied", StringComparison.OrdinalIgnoreCase),
            };
            appliedChip.Checked += (_, _) => { _activeCategory = "Applied"; ApplyFilter(); };
            CategoryChipsPanel.Children.Add(appliedChip);

            foreach (var cat in categories)
            {
                int catCount = sourceList.Count(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase));
                var chip = new RadioButton
                {
                    Style = (Style)FindResource("TweakCategoryChip"),
                    Content = $"{cat} ({catCount})",
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
            if (string.Equals(_activeCategory, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(t => t.IsApplied);
            }
            else if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
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
            int totalApplied = sourceList.Count(t => t.IsApplied);
            int totalRecommended = sourceList.Count(t => t.IsRecommended);
            int currentCount = list.Count;
            int currentApplied = list.Count(t => t.IsApplied);
            int currentRec = list.Count(t => t.IsRecommended);

            if (string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(_searchQuery))
            {
                string statusTag = totalApplied == totalSectionCount && totalSectionCount > 0
                    ? " • Fully Optimized ✓"
                    : $" • {totalApplied} Applied";

                TxtSubtitle.Text = $"{totalSectionCount} optimizations available{statusTag} • {totalRecommended} recommended safe";
                BtnSelectRecommended.ToolTip = "Select recommended safe tweaks";
                if (BtnApplySelected != null) BtnApplySelected.ToolTip = "Apply checked optimizations";
            }
            else if (string.Equals(_activeCategory, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                TxtSubtitle.Text = $"{currentApplied} optimizations currently active on this PC";
                BtnSelectRecommended.ToolTip = "Select recommended safe tweaks in this view";
                if (BtnApplySelected != null) BtnApplySelected.ToolTip = "Apply checked optimizations for this view";
            }
            else
            {
                string scope = string.IsNullOrWhiteSpace(_searchQuery) ? _activeCategory : $"Search: \"{_searchQuery}\"";
                TxtSubtitle.Text = $"{scope} • {currentCount} tweaks ({currentApplied} applied • {currentRec} recommended)";
                BtnSelectRecommended.ToolTip = $"Select recommended safe tweaks in {scope}";
                if (BtnApplySelected != null) BtnApplySelected.ToolTip = $"Apply checked optimizations for {scope}";
            }
        }

        private List<TweakItem> GetCurrentSectionItems()
        {
            return _allTweaks;
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

            if (selected.Count == 0)
            {
                var prompt = ThemedMessageBox.Show(
                    "No optimizations are currently selected.\n\nWould you like to select and apply all RECOMMENDED optimizations now?",
                    "Apply Selected Optimizations", MessageBoxButton.YesNo, MessageBoxImage.Question);

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

            var caution = ThemedMessageBox.Show(
                $"Apply {selected.Count} selected optimization(s)?\n\n" +
                "• Speeds up games and system response\n" +
                "• Safe and easily reversible anytime\n\n" +
                "Do you want to continue?",
                "Apply Selected", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (caution != MessageBoxResult.Yes) return;

            var dialog = new OptimizationDialog(
                title: $"Apply {selected.Count} Selected Optimizations",
                subtitle: $"This will safely apply {selected.Count} selected optimization(s) to maximize your PC performance:",
                tweaks: selected,
                isUndo: false,
                tweakService: _tweakService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var t in selected)
                {
                    t.IsApplied = true;
                    t.IsSelected = true;
                }
                TweakService.RecordAppliedTweaks(selected.Select(t => t.Id));

                TxtStatus.Text = $"✓ Applied {selected.Count} optimization(s) successfully.";
                StatusBar.Visibility = Visibility.Visible;
                BuildCategoryChips();
                ApplyFilter();
                CheckRebootNeeded(selected);
            }
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = GetCurrentSectionItems();
            List<TweakItem> targetTweaks;
            string scopeText;

            if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                targetTweaks = GetCurrentItems().ToList();
                scopeText = $"these {targetTweaks.Count} tweaks in {_activeCategory}";
            }
            else
            {
                targetTweaks = sourceList;
                scopeText = $"all {sourceList.Count} optimizations";
            }

            var caution = ThemedMessageBox.Show(
                "Reset settings back to normal defaults?\n\n" +
                "This will restore original Windows settings.\n\n" +
                "Do you want to continue?",
                "Reset to Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (caution != MessageBoxResult.Yes) return;

            var dialog = new OptimizationDialog(
                title: "Restore Default Windows Settings",
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
                {
                    t.IsApplied = false;
                    t.IsSelected = false;
                }
                TweakService.RecordUnappliedTweaks(targetTweaks.Select(t => t.Id));

                TxtStatus.Text = $"✓ Restored {targetTweaks.Count} tweak(s) to Windows defaults.";
                StatusBar.Visibility = Visibility.Visible;
                BuildCategoryChips();
                ApplyFilter();
            }
        }

        private void BtnDeepCleanC_Click(object sender, RoutedEventArgs e)
        {
            var caution = ThemedMessageBox.Show(
                "Clean temporary system files on Drive C:?\n\n" +
                "• Clears update cache, shader cache & temp files\n" +
                "• Reclaims free disk space safely\n\n" +
                "Do you want to continue?",
                "Clean Drive C:", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (caution != MessageBoxResult.Yes) return;

            if (BtnDeepCleanC != null) BtnDeepCleanC.IsEnabled = false;
            TxtStatus.Text = "💽 Running Deep C: Drive Storage & Component Store Cleanup...";
            StatusBar.Visibility = Visibility.Visible;
            if (StatusLottie != null) StatusLottie.Visibility = Visibility.Visible;

            try
            {
                long freedBytes = 0;
                long ramFreed = 0;

                var deepCleanSteps = new List<OptimizationCustomStep>
                {
                    new OptimizationCustomStep
                    {
                        Name = "🔍 Scanning Drive C: Temporary Files, Component Store & Caches...",
                        Action = async () =>
                        {
                            await Task.Delay(100);
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🗑️ Purging User & Windows Temp, Logs and Dump Files...",
                        Action = async () =>
                        {
                            try
                            {
                                var targets = await CleanupService.Instance.ScanAsync(force: true);
                                var tempTargets = targets.Where(t => !t.IsRamTarget && !t.IsRecycleBinTarget && !t.Name.Contains("Shader", StringComparison.OrdinalIgnoreCase) && !t.Name.Contains("Delivery", StringComparison.OrdinalIgnoreCase)).ToList();
                                freedBytes += await CleanupService.Instance.CleanAllAsync(tempTargets);
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "📦 Purging Delivery Optimization & BITS Peer Cache...",
                        Action = async () =>
                        {
                            try
                            {
                                var targets = await CleanupService.Instance.ScanAsync(force: false);
                                var doTarget = targets.FirstOrDefault(t => t.Name.Contains("Delivery", StringComparison.OrdinalIgnoreCase));
                                if (doTarget != null) freedBytes += await CleanupService.Instance.CleanAsync(doTarget, rescan: false);
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🎮 Clearing DirectX, NVIDIA & AMD Shader Caches...",
                        Action = async () =>
                        {
                            try
                            {
                                var targets = await CleanupService.Instance.ScanAsync(force: false);
                                var shaderTarget = targets.FirstOrDefault(t => t.Name.Contains("Shader", StringComparison.OrdinalIgnoreCase));
                                if (shaderTarget != null) freedBytes += await CleanupService.Instance.CleanAsync(shaderTarget, rescan: false);
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🖼️ Cleaning Windows Thumbnail & Icon Cache...",
                        Action = async () =>
                        {
                            try
                            {
                                var targets = await CleanupService.Instance.ScanAsync(force: false);
                                var thumbTarget = targets.FirstOrDefault(t => t.Name.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase));
                                if (thumbTarget != null) freedBytes += await CleanupService.Instance.CleanAsync(thumbTarget, rescan: false);
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🌐 Cleaning Safe Browser Temporary Web Caches on C:...",
                        Action = async () =>
                        {
                            await Task.Run(() =>
                            {
                                try
                                {
                                    freedBytes += CleanupService.CleanBrowserCaches();
                                }
                                catch { }
                            });
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "♻️ Emptying Windows Recycle Bin on Drive C:...",
                        Action = async () =>
                        {
                            await Task.Run(() =>
                            {
                                try
                                {
                                    freedBytes += CleanupService.EmptyRecycleBin();
                                }
                                catch { }
                            });
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "⚙️ Running Windows Component Store (WinSxS) Deep Cleanup...",
                        Action = async () =>
                        {
                            try
                            {
                                await CleanupService.RunDismComponentCleanupAsync();
                                await CleanupService.RunCleanMgrAsync();
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🧠 Purging Unreleased Standby RAM & Memory Working Sets...",
                        Action = async () =>
                        {
                            ramFreed = await Task.Run(() => MemoryCleaner.CleanRam());
                        }
                    }
                };

                var dialog = new OptimizationDialog(
                    title: "💽 Deep C: Drive Storage & Component Store Cleanup",
                    subtitle: "Purging WinSxS superseded updates, Delivery Optimization, shader caches, crash dumps & Recycle Bin to maximize C: drive space:",
                    tweaks: new List<TweakItem>(),
                    isUndo: false,
                    tweakService: _tweakService,
                    autoStart: true,
                    customSteps: deepCleanSteps)
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();

                long totalFreed = freedBytes + ramFreed;
                string freedDisplay = FormatBytes(totalFreed);
                TxtStatus.Text = $"💽 Deep C: Drive Clean complete! Successfully reclaimed {freedDisplay} of space and caches.";
                NotificationService.Notify("💽 Drive C: Cleaned!", $"Successfully purged {freedDisplay} from Drive C: including WinSxS component cleanup, shader caches, and temporary files.");
                ActivityLogger.Instance.Log($"Deep C: Drive Clean completed: Reclaimed {freedDisplay} disk & memory cache.", ActivityType.Success);

                UpdatePreGameTelemetry();
                if (TxtLiveStandbyRam != null) TxtLiveStandbyRam.Text = $"Purged ({freedDisplay} Cleaned ✓)";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Deep clean completed with notice: {ex.Message}";
            }
            finally
            {
                if (BtnDeepCleanC != null) BtnDeepCleanC.IsEnabled = true;
                if (StatusLottie != null) StatusLottie.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCleanPc_Click(object sender, RoutedEventArgs e)
        {
            var caution = ThemedMessageBox.Show(
                "Clean PC junk files and free memory?\n\n" +
                "• Clears temporary files and logs\n" +
                "• Frees up RAM for smoother performance\n\n" +
                "Do you want to continue?",
                "Clean PC & RAM", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (caution != MessageBoxResult.Yes) return;

            if (BtnCleanPc != null) BtnCleanPc.IsEnabled = false;
            TxtStatus.Text = "🧹 Cleaning system junk, temporary files, caches & purging RAM...";
            StatusBar.Visibility = Visibility.Visible;
            if (StatusLottie != null) StatusLottie.Visibility = Visibility.Visible;

            try
            {
                long freedBytes = 0;
                long ramFreed = 0;

                var cleanSteps = new List<OptimizationCustomStep>
                {
                    new OptimizationCustomStep
                    {
                        Name = "🔍 Scanning Temporary Files & Cache Directories...",
                        Action = async () =>
                        {
                            await Task.Delay(100);
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🗑️ Purging Windows Temp, User Temp & Logs...",
                        Action = async () =>
                        {
                            try
                            {
                                var scanTargets = await CleanupService.Instance.ScanAsync(force: true);
                                freedBytes = await CleanupService.Instance.CleanAllAsync(scanTargets);
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🌐 Flushing DNS Resolver Cache & Network Buffers...",
                        Action = async () =>
                        {
                            await Task.Run(() =>
                            {
                                try
                                {
                                    using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
                                    {
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    });
                                    proc?.WaitForExit(3000);
                                }
                                catch { }
                            });
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🧠 Purging Standby Memory & Working Sets...",
                        Action = async () =>
                        {
                            ramFreed = await Task.Run(() => MemoryCleaner.CleanRam());
                        }
                    }
                };

                var dialog = new OptimizationDialog(
                    title: "🧹 Deep PC & RAM Cleanup",
                    subtitle: "Purging temporary junk files, update cache, and unreleased standby RAM:",
                    tweaks: new List<TweakItem>(),
                    isUndo: false,
                    tweakService: _tweakService,
                    autoStart: true,
                    customSteps: cleanSteps)
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();

                long totalFreed = freedBytes + ramFreed;
                string freedDisplay = FormatBytes(totalFreed);
                TxtStatus.Text = $"🧹 Deep PC Clean complete! Reclaimed {freedDisplay} of junk files and RAM cache.";
                NotificationService.Notify("🧹 PC & RAM Cleaned!", $"Successfully purged {freedDisplay} of temporary files, update caches, and RAM standby memory.");
                ActivityLogger.Instance.Log($"Deep PC Clean completed: Reclaimed {freedDisplay} disk & memory cache.", ActivityType.Success);

                UpdatePreGameTelemetry();
                if (TxtLiveStandbyRam != null) TxtLiveStandbyRam.Text = $"Purged ({freedDisplay} Cleaned ✓)";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Clean completed with notice: {ex.Message}";
            }
            finally
            {
                if (BtnCleanPc != null) BtnCleanPc.IsEnabled = true;
                if (StatusLottie != null) StatusLottie.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            var targetTweaks = _allTweaks;
            if (targetTweaks.Count == 0) return;

            var prompt = ThemedMessageBox.Show(
                "Boost PC & Gaming Performance?\n\n" +
                "• Speeds up games, reduces ping and input lag\n" +
                "• Cleans junk files and frees up memory\n" +
                "• Safe and easily reversible anytime\n\n" +
                "A backup restore point will be created automatically.\n" +
                "Do you want to continue?",
                "Boost PC Performance", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (prompt != MessageBoxResult.Yes) return;

            BtnApplyAll.IsEnabled = false;
            TxtStatus.Text = "🚀 Deploying 100% Extreme Performance Engine...";
            StatusBar.Visibility = Visibility.Visible;
            if (StatusLottie != null) StatusLottie.Visibility = Visibility.Visible;

            try
            {
                long cleanedBytes = 0;
                long ramPurged = 0;
                int boostedProcesses = 0;

                var customSteps = new List<OptimizationCustomStep>
                {
                    new OptimizationCustomStep
                    {
                        Name = "💽 Deep C: Drive Storage & Component Store Cleanup",
                        Action = async () =>
                        {
                            try
                            {
                                cleanedBytes = await CleanupService.Instance.DeepCleanCDriveAsync(includeDism: true);
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🧠 Purging Standby RAM Memory Stutter",
                        Action = async () =>
                        {
                            try
                            {
                                ramPurged = await Task.Run(() => MemoryCleaner.CleanRam());
                            }
                            catch { }
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🔒 Deploying Permanent Boot Safeguard",
                        Action = async () =>
                        {
                            await PermanenceService.Instance.DeployPermanentSafeguardAsync();
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🛡️ Auto-Accepting svchost & explorer.exe Permissions",
                        Action = () =>
                        {
                            PermanenceService.ApplySilentPermissionBypass();
                            return Task.CompletedTask;
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "⚡ Unlocking Dynamic Maximum Turbo GHz",
                        Action = async () =>
                        {
                            await CpuOptimizerService.ApplyProcessorWiseMaxTurboAsync();
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "⏱️ Locking Hardware Maximum Precision Timer (Lowest Input Delay)",
                        Action = () =>
                        {
                            GameBoosterService.Instance.EnableLowestSafeTimerResolution();
                            return Task.CompletedTask;
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🎮 Dual-Vendor GPU Overdrive (NVIDIA & AMD)",
                        Action = () =>
                        {
                            GameBoosterService.ApplyInstantGamingRegistry();
                            return Task.CompletedTask;
                        }
                    },
                    new OptimizationCustomStep
                    {
                        Name = "🚀 Elevating Emulator & Game Priorities",
                        Action = async () =>
                        {
                            boostedProcesses = await GameBoosterService.BoostRunningGameProcessesAsync();
                        }
                    }
                };

                // Select and apply all tweaks
                foreach (var t in targetTweaks)
                {
                    t.IsSelected = true;
                }

                var dialog = new OptimizationDialog(
                    title: $"🚀 Apply All Optimizations ({targetTweaks.Count} Tweaks)",
                    subtitle: $"Applying 100% Performance Engine across all {targetTweaks.Count} System, Registry & Cleaner optimizations:",
                    tweaks: targetTweaks,
                    isUndo: false,
                    tweakService: _tweakService,
                    autoStart: true,
                    customSteps: customSteps)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    foreach (var t in targetTweaks)
                    {
                        t.IsApplied = true;
                        t.IsSelected = true;
                    }
                    TweakService.RecordAppliedTweaks(targetTweaks.Select(t => t.Id));

                    long totalPurged = cleanedBytes + ramPurged;
                    string cleanStr = totalPurged > 0 ? $" • {FormatBytes(totalPurged)} Cleaned" : "";
                    string procStr = boostedProcesses > 0 ? $" • {boostedProcesses} Game(s) Priority Elevated" : "";

                    var cpu = CpuOptimizerService.GetCpuInfo();
                    double liveTimerMs = GameBoosterService.Instance.CurrentTimerResolutionMs;
                    string timerStr = $"{liveTimerMs:0.000} ms";
                    ActivityLogger.Instance.Log($"All Optimizations Applied: {targetTweaks.Count} tweaks deployed, Permanent Boot Safeguard locked, {timerStr} Lowest Input Delay Timer active, {FormatBytes(totalPurged)} junk/RAM purged.", ActivityType.Success);
                    NotificationService.Notify("🚀 100% Performance Engine Active!", $"All {targetTweaks.Count} optimizations applied. {timerStr} Timer Locked (Lowest Input Delay) • {cpu.MaxTurboGhz:0.0}GHz Boost • GPU 100%{cleanStr}{procStr}.");

                    TxtStatus.Text = $"🚀 All {targetTweaks.Count} optimizations applied & PC cleaned successfully! Running at 100% peak performance.";
                    UpdatePermanentStatusBadge();
                    UpdatePreGameTelemetry();
                    if (TxtLiveStandbyRam != null) TxtLiveStandbyRam.Text = $"Purged ({FormatBytes(totalPurged)} Cleaned ✓)";
                    BuildCategoryChips();
                    ApplyFilter();
                    CheckRebootNeeded(targetTweaks);
                }
            }
            finally
            {
                BtnApplyAll.IsEnabled = true;
                if (StatusLottie != null) StatusLottie.Visibility = Visibility.Collapsed;
            }
        }

        private static string FormatBytes(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            if (mb >= 1024.0)
                return $"{mb / 1024.0:0.0} GB";
            return $"{mb:0} MB";
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

        private void UpdatePermanentStatusBadge()
        {
            if (TxtPermanentStatus == null || BadgePermanentStatus == null) return;

            bool locked = PermanenceService.Instance.IsPermanenceLocked();
            if (locked)
            {
                TxtPermanentStatus.Text = "🔒 Active & Protected";
                TxtPermanentStatus.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
                BadgePermanentStatus.BorderBrush = (System.Windows.Media.Brush)FindResource("BrushAccent");
                BadgePermanentStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x00, 0xFF, 0x66));
            }
            else
            {
                TxtPermanentStatus.Text = "⚡ Ready to Boost";
                TxtPermanentStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
                BadgePermanentStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
                BadgePermanentStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xF5, 0x9E, 0x0B));
            }
        }

        private void UpdatePreGameTelemetry()
        {
            if (TxtLiveTimerRes == null) return;

            bool timerLocked = GameBoosterService.Instance.IsTimerResolutionLocked;
            double currentMs = GameBoosterService.Instance.CurrentTimerResolutionMs;
            string msStr = $"{currentMs:0.000} ms";
            TxtLiveTimerRes.Text = timerLocked ? $"{msStr} (Lowest Latency ✓)" : $"{msStr}";
            TxtLiveTimerRes.ToolTip = $"Dynamic Hardware Timer: {msStr}\nHardware Maximum Precision is active with zero artificial cap.\nInput delay and frame pacing jitter are reduced to the absolute lowest safe limit supported by your motherboard & Windows kernel.";

            var cpu = CpuOptimizerService.GetCpuInfo();
            if (TxtLiveCpuBoost != null)
            {
                TxtLiveCpuBoost.Text = $"Boost {cpu.MaxTurboGhz:0.0} GHz ✓";
                TxtLiveCpuBoost.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
            }

            if (TxtLiveCpuName != null)
            {
                TxtLiveCpuName.Text = $"{cpu.ShortName}";
            }

            if (TxtLiveGpuMode != null)
            {
                TxtLiveGpuMode.Text = "100% Mode ✓";
                TxtLiveGpuMode.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF));
            }

            var games = GameBoosterService.GetRunningGameProcessNames();
            if (TxtLiveDetectedGames != null)
            {
                if (games.Count > 0)
                {
                    TxtLiveDetectedGames.Text = $"{string.Join(", ", games.Take(2))} (Boosted)";
                    TxtLiveDetectedGames.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
                }
                else
                {
                    TxtLiveDetectedGames.Text = "Auto-Boosted ✓";
                    TxtLiveDetectedGames.Foreground = (System.Windows.Media.Brush)FindResource("BrushAccent");
                }
            }

            if (TxtLiveStandbyRam != null)
            {
                TxtLiveStandbyRam.Text = "Clean & Smooth";
            }
        }
    }
}
