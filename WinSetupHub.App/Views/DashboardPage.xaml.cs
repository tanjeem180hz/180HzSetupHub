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

            Loaded += async (_, _) =>
            {
                UpdateStats();
                _statsTimer.Start();
                await LoadSummaryAsync();
            };

            Unloaded += (_, _) =>
            {
                _statsTimer.Stop();
            };
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

        private async System.Threading.Tasks.Task LoadSummaryAsync()
        {
            RefreshSummaryButton.IsEnabled = false;
            UpdateSummaryText.Text = "Checking for updates…";

            if (!await _winget.IsAvailableAsync())
            {
                UpdateSummaryText.Text = "winget not found on this system.";
                EngineStatusText.Text = "WINGET MISSING";
                RefreshSummaryButton.IsEnabled = true;
                return;
            }

            EngineStatusText.Text = "ENGINE READY";

            var upgradable = await _winget.GetUpgradableAppsAsync();
            UpdateSummaryText.Text = upgradable.Count == 0
                ? "Everything is up to date."
                : $"{upgradable.Count} update-ready app(s) found.";

            _mainWindow.SetStatus(upgradable.Count == 0 ? "Up to date" : $"{upgradable.Count} update(s) available");
            RefreshSummaryButton.IsEnabled = true;
        }

        private async void RefreshSummary_Click(object sender, RoutedEventArgs e) => await LoadSummaryAsync();

        private void TileSetupApps_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("SetupApps");
        private void TileUpdateCenter_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("UpdateCenter");
        private void TileUninstaller_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Uninstaller");
        private void TileCleanup_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Cleanup");
        private void TileActivity_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Activity");
        private void TileStorage_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Storage");
    }
}
