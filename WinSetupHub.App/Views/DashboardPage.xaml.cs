using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class DashboardPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private readonly WingetService _winget = new();
        private readonly SystemStatsService _stats = new();
        private readonly DispatcherTimer _statsTimer;

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

        private void AdaptLayout()
        {
            double width = ActualWidth;
            if (width <= 0) return;

            if (SystemStatsSection != null)
            {
                SystemStatsSection.Columns = width < 880 ? 1 : 3;
            }

            if (ModulesGrid != null)
            {
                if (width < 800)
                {
                    ModulesGrid.Columns = 1;
                    ModulesGrid.Rows = 6;
                }
                else if (width < 1120)
                {
                    ModulesGrid.Columns = 2;
                    ModulesGrid.Rows = 3;
                }
                else
                {
                    ModulesGrid.Columns = 3;
                    ModulesGrid.Rows = 2;
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
            UpdateSummaryText.Text = count == 0
                ? "Everything is up to date."
                : $"{count} update-ready app(s) found.";

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
                BtnEngineStatus.Tag = "ENGINE READY";
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

        private void TileSetupApps_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("SetupApps");
        private void TileUpdateCenter_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("UpdateCenter");
        private void TileUninstaller_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Uninstaller");
        private void TileCleanup_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Cleanup");
        private void TileActivity_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Activity");
        private void TileStorage_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Storage");
    }
}
