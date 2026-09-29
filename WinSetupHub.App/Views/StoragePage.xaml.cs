using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class StoragePage : UserControl
    {
        private readonly StorageService _storage = new();

        public StoragePage()
        {
            InitializeComponent();
            PathText.Text = StorageService.AppDataFolder;
            Loaded += async (_, _) => await RefreshSizeAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSizeAsync();

        private void OpenStorage_Click(object sender, RoutedEventArgs e) => _storage.OpenInExplorer();

        private void OpenCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.GoToPage("Cleanup");
            }
        }

        private async Task RefreshSizeAsync()
        {
            SizeText.Text = "Calculating…";
            StorageSummaryText.Text = "Analyzing storage metrics…";

            long bytes = await Task.Run(() => _storage.GetFolderSizeBytes());
            double mb = bytes / 1024.0 / 1024.0;
            SizeText.Text = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";

            try
            {
                var drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    double totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                    double usedPercent = ((totalGb - freeGb) / totalGb) * 100.0;

                    DriveFreeText.Text = $"{freeGb:0.0} GB Free";
                    DriveTotalText.Text = $" of {totalGb:0.0} GB";
                    DriveProgressBar.Value = Math.Clamp(usedPercent, 0, 100);
                    DriveStatusText.Text = freeGb > 20 ? "Drive Capacity Healthy" : "Drive Space Low - Run Cleanup";
                }
            }
            catch
            {
                // Graceful fallback
            }

            StorageSummaryText.Text = "Storage telemetry updated";
        }
    }
}
