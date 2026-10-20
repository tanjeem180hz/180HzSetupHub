using SetupHub180Hz.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class DashboardPage : UserControl, IRealtimeRefreshable
    {
        private readonly MainWindow _mainWindow;
        private readonly WingetService _winget = new();
        private readonly SystemStatsService _stats = new();
        private readonly CleanupService _cleanup = CleanupService.Instance;
        private readonly DispatcherTimer _statsTimer;
        private bool _isBoosting = false;

        public DashboardPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _statsTimer.Tick += (_, _) => _ = UpdateStatsAsync();

            UpdateMonitorService.Instance.UpgradableApps.CollectionChanged += (_, _) => UpdateSummary();

            SizeChanged += (_, _) => AdaptLayout();

            Loaded += async (_, _) =>
            {
                AdaptLayout();
                _ = UpdateStatsAsync();
                _statsTimer.Start();
                await CheckEngineAsync();
                UpdateSummary();

                if (TxtPermanentSafeguardBtn != null)
                {
                    bool isLocked = PermanenceService.Instance.IsPermanenceLocked();
                    TxtPermanentSafeguardBtn.Text = isLocked ? "✓ PERMANENTLY LOCKED" : "LOCK PERMANENT (SURVIVES REBOOT)";
                }
            };

            Unloaded += (_, _) =>
            {
                _statsTimer.Stop();
            };

            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue)
                {
                    if (!_statsTimer.IsEnabled) _statsTimer.Start();
                    _ = UpdateStatsAsync();
                }
                else
                {
                    _statsTimer.Stop();
                }
            };
        }

        public void RefreshRealtime()
        {
            if (Visibility == Visibility.Visible)
            {
                if (!_statsTimer.IsEnabled)
                {
                    _statsTimer.Start();
                }
                _ = UpdateStatsAsync();
                UpdateSummary();
            }
        }

        private void AdaptLayout()
        {
            double width = ActualWidth;
            if (width <= 0) return;

            if (HeroContentGrid != null && HeroOverviewPanel != null && HeroTelemetryPod != null)
            {
                if (width < 860)
                {
                    HeroContentGrid.ColumnDefinitions.Clear();
                    HeroContentGrid.RowDefinitions.Clear();
                    HeroContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    HeroContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(HeroOverviewPanel, 0);
                    Grid.SetRow(HeroOverviewPanel, 0);
                    Grid.SetColumn(HeroTelemetryPod, 0);
                    Grid.SetRow(HeroTelemetryPod, 1);
                    HeroTelemetryPod.Margin = new Thickness(0, 16, 0, 0);
                    HeroTelemetryPod.HorizontalAlignment = HorizontalAlignment.Left;
                }
                else
                {
                    HeroContentGrid.RowDefinitions.Clear();
                    HeroContentGrid.ColumnDefinitions.Clear();
                    HeroContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    HeroContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Grid.SetRow(HeroOverviewPanel, 0);
                    Grid.SetColumn(HeroOverviewPanel, 0);
                    Grid.SetRow(HeroTelemetryPod, 0);
                    Grid.SetColumn(HeroTelemetryPod, 1);
                    HeroTelemetryPod.Margin = new Thickness(16, 0, 0, 0);
                    HeroTelemetryPod.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }

            if (SystemStatsSection != null)
            {
                SystemStatsSection.Columns = width < 880 ? 1 : 3;
            }

            if (ModulesGrid != null)
            {
                if (width < 800)
                {
                    ModulesGrid.Columns = 1;
                    ModulesGrid.Rows = 0;
                }
                else if (width < 1150)
                {
                    ModulesGrid.Columns = 2;
                    ModulesGrid.Rows = 0;
                }
                else
                {
                    ModulesGrid.Columns = 3;
                    ModulesGrid.Rows = 0;
                }
            }
        }

        private bool _isUpdatingStats = false;

        private async Task UpdateStatsAsync()
        {
            if (_isUpdatingStats || Visibility != Visibility.Visible) return;
            _isUpdatingStats = true;

            try
            {
                var snap = await Task.Run(() => _stats.GetSnapshot());
                if (Visibility != Visibility.Visible) return;

                // CPU
                if (CpuPercentText != null) CpuPercentText.Text = $"{snap.CpuPercent:0.0}%";
                if (CpuProgressBar != null) CpuProgressBar.Value = Math.Clamp(snap.CpuPercent, 0, 100);

                // RAM
                if (RamUsageText != null) RamUsageText.Text = $"{snap.RamUsedGb:0.0} / {snap.RamTotalGb:0.0} GB";
                if (RamPercentText != null) RamPercentText.Text = $" ({snap.RamPercent:0}%)";
                if (RamProgressBar != null) RamProgressBar.Value = Math.Clamp(snap.RamPercent, 0, 100);

                // Disk (C:)
                if (DiskUsageText != null) DiskUsageText.Text = $"{snap.DiskFreeGb:0.0} GB Free";
                if (DiskTotalText != null) DiskTotalText.Text = $" of {snap.DiskTotalGb:0.0} GB";
                if (DiskProgressBar != null) DiskProgressBar.Value = Math.Clamp(snap.DiskPercent, 0, 100);
            }
            catch { }
            finally
            {
                _isUpdatingStats = false;
            }
        }

        private void UpdateSummary()
        {
            var count = UpdateMonitorService.Instance.UpgradableApps.Count;
            if (UpdateSummaryText != null)
            {
                UpdateSummaryText.Text = count == 0
                    ? "All apps up to date"
                    : $"{count} update(s) available";
            }
            if (HeroUpdateIcon != null)
            {
                HeroUpdateIcon.Text = count == 0 ? "✓" : "⚡";
            }
            if (HeroUpdateArrow != null)
            {
                HeroUpdateArrow.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }

            _mainWindow.SetStatus(count == 0 ? "Up to date" : $"{count} update(s) available");
        }

        private async Task CheckEngineAsync()
        {
            if (!await _winget.IsAvailableAsync())
            {
                UpdateSummaryText.Text = "winget not found on this system.";
                if (BtnEngineStatus != null)
                {
                    BtnEngineStatus.Tag = "WINGET MISSING";
                    BtnEngineStatus.ToolTip = "Winget core engine not detected on system (Click to re-check)";
                }
                if (EngineStatusDot != null)
                {
                    EngineStatusDot.Fill = (System.Windows.Media.Brush)FindResource("BrushError");
                }
                return;
            }

            if (BtnEngineStatus != null)
            {
                BtnEngineStatus.Tag = "180Hz";
                BtnEngineStatus.ToolTip = "Winget Core Engine Active (180Hz Ready)";
            }
            if (EngineStatusDot != null)
            {
                EngineStatusDot.Fill = (System.Windows.Media.Brush)FindResource("BrushSuccess");
            }
        }

        private async void EngineStatus_Click(object sender, RoutedEventArgs e)
        {
            await CheckEngineAsync();
        }

        private async void RefreshSummary_Click(object sender, RoutedEventArgs e)
        {
            RefreshSummaryButton.IsEnabled = false;
            UpdateSummaryText.Text = "Checking for updates…";
            await UpdateMonitorService.Instance.RefreshAsync(force: true);
            UpdateSummary();
            RefreshSummaryButton.IsEnabled = true;
        }

        private void HeroUpdateChip_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("UpdateCenter");
        private void HeroCleanupChip_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Cleanup");

        private async void BtnQuickBoost_Click(object sender, RoutedEventArgs e)
        {
            if (_isBoosting) return;
            _isBoosting = true;
            BtnQuickBoost.IsEnabled = false;
            TxtQuickBoostBtn.Text = "BOOSTING 100%...";

            try
            {
                // 1. Extreme 2X FPS Game Overdrive (0.5ms Timer Lock + Standby RAM Purge + GPU Priority 8 + 180Hz Power Scheme)
                var gameBoost = await GameBoosterService.Instance.Activate2xFpsOverdriveAsync();
                long freedRam = gameBoost.FreedRamBytes;

                // 2. Eradicate Temporary, Recent, Crash Dumps, Prefetch & System Cache
                long freedBytes = 0;
                var targets = await _cleanup.ScanAsync(force: true);
                foreach (var t in targets.Where(x => !x.IsRamTarget &&
                    (x.Name.Contains("Temp", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("Recent", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("Dump", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("Cache", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("Prefetch", StringComparison.OrdinalIgnoreCase))))
                {
                    freedBytes += await _cleanup.CleanAsync(t);
                }

                // 3. CLR Heap & Working Set compaction
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();

                _ = UpdateStatsAsync();

                string ramStr = FormatBytes(freedRam);
                string diskStr = FormatBytes(freedBytes);
                string procStr = gameBoost.BoostedProcessesCount > 0 ? $" • {gameBoost.BoostedProcessesCount} Game(s) Priority Elevated" : "";

                string timerStr = $"{gameBoost.TimerResolutionMs:0.000} ms";
                ActivityLogger.Instance.Log($"2X FPS Extreme Turbo Boost completed: {timerStr} Lowest Input Delay Timer Locked, Flushed {ramStr} RAM cache and wiped {diskStr} junk.{procStr}", ActivityType.Success);
                NotificationService.Notify("🎮 2X FPS Turbo Boost Activated!", $"Locked {timerStr} Timer Resolution (Lowest Input Delay) • Purged {ramStr} Standby Cache • Wiped {diskStr} trash{procStr}. Ultra-smooth gameplay active!");

                TxtQuickBoostBtn.Text = "✓ 2X BOOSTED!";

                await Task.Delay(2500);
                TxtQuickBoostBtn.Text = "1-CLICK PRE-GAME BOOST";
            }
            catch
            {
                TxtQuickBoostBtn.Text = "1-CLICK PRE-GAME BOOST";
            }
            finally
            {
                BtnQuickBoost.IsEnabled = true;
                _isBoosting = false;
            }
        }

        private async void BtnPermanentSafeguard_Click(object sender, RoutedEventArgs e)
        {
            if (BtnPermanentSafeguard == null || TxtPermanentSafeguardBtn == null) return;

            bool isLocked = PermanenceService.Instance.IsPermanenceLocked();
            if (isLocked)
            {
                var viewChoice = ThemedMessageBox.Show(
                    "🔒 PERMANENT BOOT SAFEGUARD IS ACTIVE!\n\n" +
                    "Your system already has the 180Hz Boot Persistence Safeguard installed and running.\n\n" +
                    "• CPU Core Unparking (100% min/max): Active\n" +
                    "• Zero-Ping Network Heuristics: Active\n" +
                    "• MMCSS GPU Priority 8: Active\n" +
                    "• Survives Every Restart: Active\n\n" +
                    "Would you like to open the Permanent Optimization Hub to review or customize tweaks?",
                    "Boot Safeguard Active", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (viewChoice == MessageBoxResult.Yes)
                {
                    _mainWindow.GoToPage("Optimization");
                }
                return;
            }

            var choice = ThemedMessageBox.Show(
                "🔒 LOCK PERMANENT SYSTEM OPTIMIZATIONS\n\n" +
                "This will deploy the 180Hz Boot Persistence Safeguard:\n\n" +
                "• Creates Windows Startup Scheduled Task '180Hz_Permanence_Guard' (Highest Privileges)\n" +
                "• Unparks 100% CPU cores & locks 0ms boost delay permanently on every boot\n" +
                "• Locks TCP Fast Open, RSS, Autotuning & Zero-Ping Heuristics permanently\n" +
                "• Sets MMCSS GPU Priority 8, Priority 6 & SystemResponsiveness 0%\n" +
                "• Locks BCD high precision tick & disables dynamic tick\n\n" +
                "Once locked, your PC will stay 100% optimized after every restart!\n\n" +
                "Lock permanent optimizations now?",
                "Lock Permanent Optimizations", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes) return;

            BtnPermanentSafeguard.IsEnabled = false;
            TxtPermanentSafeguardBtn.Text = "LOCKING PERMANENT...";

            bool ok = await PermanenceService.Instance.DeployPermanentSafeguardAsync();

            if (ok)
            {
                ActivityLogger.Instance.Log("Permanent Boot Safeguard deployed from Dashboard.", ActivityType.Success);
                NotificationService.Notify("🔒 Permanent Optimization Locked!", "180Hz Boot Safeguard active. Unparked CPU cores, 0-ping network & GPU priorities locked across all future restarts.");

                TxtPermanentSafeguardBtn.Text = "✓ PERMANENTLY LOCKED";
                ThemedMessageBox.Show(
                    "✓ PERMANENT SYSTEM OPTIMIZATION LOCKED!\n\n" +
                    "The 180Hz Boot Persistence Safeguard is now active.\n\n" +
                    "• All power, CPU unparking, network heuristics and scheduler settings are locked.\n" +
                    "• The task executes automatically with highest privileges on every Windows startup.\n" +
                    "• You do NOT need to apply optimizations again after restarting your PC!",
                    "Permanent Safeguard Active", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtPermanentSafeguardBtn.Text = "LOCK PERMANENT (SURVIVES REBOOT)";
            }

            BtnPermanentSafeguard.IsEnabled = true;
        }

        private void TileSetupApps_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("SetupApps");
        private void TileDownloads_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Downloads");
        private void TileUpdateCenter_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("UpdateCenter");
        private void TileUninstaller_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Uninstaller");
        private void TileStartup_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Startup");
        private void TileCleanup_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Cleanup");
        private void TileActivity_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Activity");
        private void TileStorage_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Storage");
        private void TileOptimization_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Optimization");
        private void HeroOptimizationChip_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Optimization");

        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DashboardScrollViewer != null && DashboardScrollViewer.ScrollableHeight > 0)
            {
                SmoothScrollHelper.HandleMouseWheel(DashboardScrollViewer, e);
            }
        }
            private async void BtnCleanRam_Click(object sender, RoutedEventArgs e)
        {
            if (BtnCleanRam == null || TxtCleanRamBtn == null) return;

            BtnCleanRam.IsEnabled = false;
            TxtCleanRamBtn.Text = "CLEARING…";

            long freed = await Task.Run(() => MemoryCleaner.CleanRam());

            // Immediately refresh live stats gauge
            _ = UpdateStatsAsync();

            // Refresh CleanupService RAM target in background
            CleanupService.Instance.StartBackgroundScan();

            string freedDisplay = FormatBytes(freed);
            TxtCleanRamBtn.Text = $"✓ {freedDisplay}";

            ActivityLogger.Instance.Log($"Maximum RAM Clear & Cache Purged: Reclaimed {freedDisplay} physical memory with peak performance tuning.", ActivityType.Success);
            NotificationService.Notify("Maximum RAM & Cache Purged", $"Successfully freed {freedDisplay} RAM. System cache cleared & memory performance maximized.");

            await Task.Delay(2500);
            TxtCleanRamBtn.Text = "CLEAR";
            BtnCleanRam.IsEnabled = true;
        }

        private void BtnGoToCleanup_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Cleanup");

        private static string FormatBytes(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            if (mb >= 1024.0)
                return $"{mb / 1024.0:0.0} GB";
            return $"{mb:0} MB";
        }
}
}
