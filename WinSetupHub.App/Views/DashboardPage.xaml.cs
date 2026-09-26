using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class DashboardPage : UserControl
    {
        private readonly MainWindow _mainWindow;
        private readonly WingetService _winget = new();

        public DashboardPage(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += async (_, _) => await LoadSummaryAsync();
        }

        private async System.Threading.Tasks.Task LoadSummaryAsync()
        {
            if (!await _winget.IsAvailableAsync())
            {
                UpdateSummaryText.Text = "winget not found on this system.";
                return;
            }

            var upgradable = await _winget.GetUpgradableAppsAsync();
            UpdateSummaryText.Text = upgradable.Count == 0
                ? "Everything is up to date."
                : $"{upgradable.Count} update-ready app(s) found.";

            _mainWindow.SetStatus(upgradable.Count == 0 ? "Up to date" : $"{upgradable.Count} update(s) available");
        }

        private void TileSetupApps_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("SetupApps");
        private void TileUpdateCenter_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("UpdateCenter");
        private void TileUninstaller_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Uninstaller");
        private void TileCleanup_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Cleanup");
        private void TileActivity_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Activity");
        private void TileStorage_Click(object sender, RoutedEventArgs e) => _mainWindow.GoToPage("Storage");
    }
}
