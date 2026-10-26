using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class StoragePage : UserControl, IRealtimeRefreshable
    {
        private readonly StorageService _storage = new();
        private DateTime _lastCalculation = DateTime.MinValue;

        public StoragePage()
        {
            InitializeComponent();
            UpdateDirectoryUI();

            StorageService.AppDataFolderChanged += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateDirectoryUI();
                    _ = RefreshSizeAsync(force: true);
                });
            };

            Loaded += async (_, _) =>
            {
                UpdateDirectoryUI();
                if ((DateTime.UtcNow - _lastCalculation).TotalSeconds > 30)
                {
                    await RefreshSizeAsync();
                }
            };
        }

        public void RefreshRealtime()
        {
            if ((DateTime.UtcNow - _lastCalculation).TotalSeconds > 30)
            {
                _ = RefreshSizeAsync();
            }
        }

        private void UpdateDirectoryUI()
        {
            try
            {
                PathText.Text = StorageService.AppDataFolder;

                if (StorageService.IsCustomLocation)
                {
                    LocationBadgeText.Text = "CUSTOM DIRECTORY";
                    if (TryFindResource("BrushAccent") is Brush accentBrush)
                    {
                        LocationBadge.BorderBrush = accentBrush;
                        LocationBadgeText.Foreground = accentBrush;
                    }
                    ResetDefaultButton.Visibility = Visibility.Visible;
                    PathHintText.Text = "Custom data location active. All user settings, activity logs, download cache, and catalogs are saved here.";
                }
                else
                {
                    LocationBadgeText.Text = "DEFAULT DIRECTORY";
                    if (TryFindResource("BrushBorder") is Brush borderBrush) LocationBadge.BorderBrush = borderBrush;
                    if (TryFindResource("BrushTextSecondary") is Brush textSec) LocationBadgeText.Foreground = textSec;
                    ResetDefaultButton.Visibility = Visibility.Collapsed;
                    PathHintText.Text = "Standard AppData location. Contains system logs, user configurations, app catalogs, and downloaded installers.";
                }
            }
            catch { }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSizeAsync(force: true);

        private void OpenStorage_Click(object sender, RoutedEventArgs e) => _storage.OpenInExplorer();

        private void OpenCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.GoToPage("Cleanup");
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(StorageService.AppDataFolder);
                ShowStatusBanner("✓ Directory path copied to clipboard!", isSuccess: true);
            }
            catch (Exception ex)
            {
                StorageSummaryText.Text = $"Notice: {ex.Message}";
            }
        }

        private async void ChangeDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select Custom Application Data Directory - 180Hz Setup Hub",
                    InitialDirectory = StorageService.AppDataFolder,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return;

                string selectedPath = dialog.FolderName;
                if (string.IsNullOrWhiteSpace(selectedPath)) return;
                selectedPath = Path.GetFullPath(selectedPath.Trim());

                if (string.Equals(selectedPath, StorageService.AppDataFolder, StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatusBanner("The selected directory is already the active application data directory.", isSuccess: true);
                    return;
                }

                var promptResult = ThemedMessageBox.Show(
                    $"Would you like to copy and migrate your existing application data (user settings, activity logs, and download cache) to the new location?\n\nNew Location:\n{selectedPath}\n\n• Click 'Yes' to copy existing data to the new location (Recommended).\n• Click 'No' to initialize an empty data directory at this location.\n• Click 'Cancel' to abort.",
                    "Customize Application Data Directory",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (promptResult == MessageBoxResult.Cancel) return;

                bool migrate = (promptResult == MessageBoxResult.Yes);

                ShowStatusBanner("Relocating application data directory...", isSuccess: true);

                var result = await Task.Run(() => StorageService.SetCustomAppDataFolder(selectedPath, migrateExistingData: migrate));

                if (result.success)
                {
                    UpdateDirectoryUI();
                    ShowStatusBanner($"✓ Data directory successfully updated to:\n{StorageService.AppDataFolder}", isSuccess: true);
                    await RefreshSizeAsync(force: true);
                }
                else
                {
                    ThemedMessageBox.Show(result.message, "Directory Selection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShowStatusBanner($"Notice: {result.message}", isSuccess: false);
                }
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Could not customize directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ResetDefault_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var promptResult = ThemedMessageBox.Show(
                    $"Reset application data location back to the default Windows AppData folder?\n\nDefault Location:\n{StorageService.DefaultAppDataFolder}\n\nWould you like to copy your current data files back to the default folder?",
                    "Reset Application Data Directory",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (promptResult == MessageBoxResult.Cancel) return;

                bool migrate = (promptResult == MessageBoxResult.Yes);

                ShowStatusBanner("Restoring default directory...", isSuccess: true);

                var result = await Task.Run(() => StorageService.ResetToDefaultDirectory(migrateExistingData: migrate));

                if (result.success)
                {
                    UpdateDirectoryUI();
                    ShowStatusBanner("✓ Application data location restored to default (%LocalAppData%\\180HzSetupHub).", isSuccess: true);
                    await RefreshSizeAsync(force: true);
                }
                else
                {
                    ThemedMessageBox.Show(result.message, "Reset Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShowStatusBanner($"Notice: {result.message}", isSuccess: false);
                }
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Could not reset directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowStatusBanner(string text, bool isSuccess)
        {
            StatusBannerText.Text = text;
            StatusBannerIcon.Text = isSuccess ? "✓" : "⚠️";
            StatusBanner.Visibility = Visibility.Visible;
        }

        private async Task RefreshSizeAsync(bool force = false)
        {
            try
            {
                if (!force && (DateTime.UtcNow - _lastCalculation).TotalSeconds < 30) return;
                _lastCalculation = DateTime.UtcNow;
                SizeText.Text = "Calculating…";
                StorageSummaryText.Text = "Analyzing storage metrics…";

                long bytes = await Task.Run(() => _storage.GetFolderSizeBytes());
                double mb = bytes / 1024.0 / 1024.0;
                SizeText.Text = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";

                try
                {
                    string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                    var drive = new DriveInfo(sysDrive);
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
            catch (Exception ex)
            {
                StorageSummaryText.Text = $"Notice: {ex.Message}";
            }
        }
    }
}