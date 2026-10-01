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
                Interval = TimeSpan.FromSeconds(2)
            };
            _statsTimer.Tick += (_, _) => UpdateStats();

            UpdateMonitorService.Instance.UpgradableApps.CollectionChanged += (_, _) => UpdateSummary();

            SizeChanged += (_, _) => AdaptLayout();

            Loaded += async (_, _) =>
            {
                AdaptLayout();
                UpdateStats();
                _statsTimer.Start();
                await CheckEngineAsync();
                UpdateSummary();
            };

            Unloaded += (_, _) =>
            {
                _statsTimer.Stop();
            };
        }

        public void RefreshRealtime()
        {
            if (!_statsTimer.IsEnabled)
            {
                _statsTimer.Start();
            }
            UpdateStats();
            UpdateSummary();
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

        private void UpdateStats()
        {
            try
            {
                var snap = _stats.GetSnapshot();

                // CPU
                CpuPercentText.Text = $"{snap.CpuPercent:0.0}%";
                CpuProgressBar.Value = Math.Clamp(snap.CpuPercent, 0, 100);

                // RAM
                RamUsageText.Text = $"{snap.RamUsedGb:0.0} / {snap.RamTotalGb:0.0} GB";
                RamPercentText.Text = $" ({snap.RamPercent:0}%)";
                RamProgressBar.Value = Math.Clamp(snap.RamPercent, 0, 100);

                // Disk (C:)
                DiskUsageText.Text = $"{snap.DiskFreeGb:0.0} GB Free";
                DiskTotalText.Text = $" of {snap.DiskTotalGb:0.0} GB";
                DiskProgressBar.Value = Math.Clamp(snap.DiskPercent, 0, 100);
            }
            catch { }
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
            TxtQuickBoostBtn.Text = "OPTIMIZING...";

            try
            {
                long freedBytes = 0;
                var targets = await _cleanup.ScanAsync();
                foreach (var t in targets.Where(x => x.Name.Contains("Temp", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Recent", StringComparison.OrdinalIgnoreCase)))
                {
                    freedBytes += await _cleanup.CleanAsync(t);
                }

                UpdateStats();

                TxtQuickBoostBtn.Text = "BOOSTED!";

                await Task.Delay(2500);
                TxtQuickBoostBtn.Text = "OPTIMIZE";
            }
            catch
            {
                TxtQuickBoostBtn.Text = "OPTIMIZE";
            }
            finally
            {
                BtnQuickBoost.IsEnabled = true;
                _isBoosting = false;
            }
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
            UpdateStats();

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
