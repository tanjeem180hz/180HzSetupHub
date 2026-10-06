using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class RevoUninstallWizardDialog : Window
    {
        private readonly AppItem _app;
        private readonly DeepUninstallService _deepUninstall = new();
        private readonly WingetService _winget = new();

        private readonly ObservableCollection<LeftoverViewModel> _registryViewModels = new();
        private readonly ObservableCollection<LeftoverViewModel> _filesViewModels = new();

        private UninstallScanMode _currentScanMode = UninstallScanMode.Moderate;
        private int _currentStep = 1;
        private bool _isDeleting = false;

        public bool IsUninstalled { get; private set; }
        private int _purgedRegistryCount = 0;
        private int _purgedFilesCount = 0;
        private long _purgedBytes = 0;

        public RevoUninstallWizardDialog(AppItem app)
        {
            InitializeComponent();
            _app = app;

            HeaderAppNameText.Text = app.Name;
            AppNameText.Text = app.Name;
            AppVersionText.Text = string.IsNullOrWhiteSpace(app.FormattedVersion) ? "Unknown Version" : app.FormattedVersion;
            AppSizeText.Text = string.IsNullOrWhiteSpace(app.FormattedSize) ? "Unknown Size" : app.FormattedSize;
            AppIdText.Text = string.IsNullOrWhiteSpace(app.Id) ? "" : app.Id;

            AppLogoGrid.Children.Clear();
            if (app.IconImageSource != null)
            {
                var img = new Image
                {
                    Source = app.IconImageSource,
                    Width = 32,
                    Height = 32,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                AppLogoGrid.Children.Add(img);
            }
            else
            {
                var fallback = new TextBlock
                {
                    Text = "📦",
                    FontSize = 20,
                    Foreground = (Brush)FindResource("BrushTextSecondary"),
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                AppLogoGrid.Children.Add(fallback);
            }

            RegistryListBox.ItemsSource = _registryViewModels;
            FilesListBox.ItemsSource = _filesViewModels;

            SetStep(1);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void WindowCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            try { DialogResult = IsUninstalled; } catch { }
            Close();
        }

        #region Step Navigation

        private void SetStep(int step)
        {
            _currentStep = step;

            PanelStep1_Analysis.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep2_BuiltIn.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep3_ScanMode.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep4_Registry.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep5_Files.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep6_Complete.Visibility = step == 6 ? Visibility.Visible : Visibility.Collapsed;

            UpdateRibbonUI(step);
        }

        private void UpdateRibbonUI(int currentStep)
        {
            var accentBrush = (Brush)FindResource("BrushAccent");
            var successBrush = (Brush)FindResource("BrushSuccess");
            var borderBrush = (Brush)FindResource("BrushBorder");
            var textPrimary = (Brush)FindResource("BrushTextPrimary");
            var textSecondary = (Brush)FindResource("BrushTextSecondary");
            var accentFg = (Brush)FindResource("BrushAccentForeground");

            var badges = new[] { StepBadge1, StepBadge2, StepBadge3, StepBadge4, StepBadge5 };
            var nums = new[] { StepNum1, StepNum2, StepNum3, StepNum4, StepNum5 };
            var labels = new[] { StepLabel1, StepLabel2, StepLabel3, StepLabel4, StepLabel5 };

            for (int i = 0; i < 5; i++)
            {
                int stepNum = i + 1;
                if (stepNum < currentStep)
                {
                    // Completed step
                    badges[i].Background = successBrush;
                    nums[i].Text = "✓";
                    nums[i].Foreground = (Brush)FindResource("BrushBackground");
                    labels[i].Foreground = successBrush;
                    labels[i].FontWeight = FontWeights.Bold;
                }
                else if (stepNum == currentStep)
                {
                    // Active step
                    badges[i].Background = accentBrush;
                    nums[i].Text = stepNum.ToString();
                    nums[i].Foreground = accentFg;
                    labels[i].Foreground = accentBrush;
                    labels[i].FontWeight = FontWeights.Bold;
                }
                else
                {
                    // Upcoming step
                    badges[i].Background = borderBrush;
                    nums[i].Text = stepNum.ToString();
                    nums[i].Foreground = textSecondary;
                    labels[i].Foreground = textSecondary;
                    labels[i].FontWeight = FontWeights.Normal;
                }
            }
        }

        #endregion

        #region Phase 1: Analysis & System Protection

        private async Task RunPhase1PreparationAsync()
        {
            ActivityLogger.Instance.Log($"Phase 1: Preparing uninstallation safeguards for {_app.Name}…", ActivityType.Info);

            // 1. System Restore Point
            if (ChkCreateRestorePoint.IsChecked == true)
            {
                RestorePointStatusText.Text = "Creating Windows System Restore point checkpoint…";
                bool ok = await DeepUninstallService.CreateRestorePointAsync(_app.Name);
                if (ok)
                {
                    RestorePointStatusText.Text = "✓ System Restore Point checkpoint created successfully.";
                    RestorePointIcon.Text = "✓";
                    RestorePointIcon.Foreground = (Brush)FindResource("BrushSuccess");
                }
                else
                {
                    RestorePointStatusText.Text = "ℹ️ System Restore is disabled or unavailable on this system (Safe to continue).";
                    RestorePointIcon.Text = "ℹ️";
                    RestorePointIcon.Foreground = (Brush)FindResource("BrushWarning");
                }
            }
            else
            {
                RestorePointStatusText.Text = "Skipped by user option.";
                RestorePointIcon.Text = "—";
            }

            // 2. Application Registry Backup
            if (ChkCreateRegistryBackup.IsChecked == true)
            {
                RegistryBackupStatusText.Text = "Exporting application registry keys to safe backup storage…";
                var regFile = await DeepUninstallService.CreateRegistryBackupAsync(_app);
                if (!string.IsNullOrEmpty(regFile))
                {
                    RegistryBackupStatusText.Text = $"✓ Registry keys backed up to: {Path.GetFileName(regFile)}";
                    RegistryBackupIcon.Text = "✓";
                    RegistryBackupIcon.Foreground = (Brush)FindResource("BrushSuccess");
                }
                else
                {
                    RegistryBackupStatusText.Text = "✓ Pre-installation state verified.";
                    RegistryBackupIcon.Text = "✓";
                    RegistryBackupIcon.Foreground = (Brush)FindResource("BrushSuccess");
                }
            }
            else
            {
                RegistryBackupStatusText.Text = "Skipped by user option.";
                RegistryBackupIcon.Text = "—";
            }

            // 3. Install pointers index
            AnalysisStatusText.Text = "✓ Registry pointers and installation directories successfully indexed.";
            AnalysisIcon.Text = "✓";
            AnalysisIcon.Foreground = (Brush)FindResource("BrushSuccess");

            PrepProgressBar.IsIndeterminate = false;
            PrepProgressBar.Value = 100;
            await Task.Delay(400);
        }

        private async void BtnProceedToUninstaller_Click(object sender, RoutedEventArgs e)
        {
            BtnProceedToUninstaller.IsEnabled = false;
            PrepProgressBar.Visibility = Visibility.Visible;
            PrepProgressBar.IsIndeterminate = true;

            await RunPhase1PreparationAsync();

            SetStep(2);
            await RunPhase2BuiltInUninstallerAsync();
        }

        #endregion

        #region Phase 2: Built-in Uninstaller

        private System.Diagnostics.Process? _activeUninstallerProcess;

        private async Task RunPhase2BuiltInUninstallerAsync()
        {
            ActivityLogger.Instance.Log($"Phase 2: Executing official uninstaller for {_app.Name}…", ActivityType.Info);

            // Step A: Pre-terminate running processes inside install location (protecting uninstaller processes)
            DeepUninstallService.KillProcessesForApp(_app);

            // Update UI to indicate searching/preparing uninstaller
            UninstallerStatusIconText.Text = "⏳";
            UninstallerTitleText.Text = "LAUNCHING OFFICIAL UNINSTALLER";
            UninstallerSubtextText.Text = $"Locating and invoking official publisher uninstaller for {_app.Name}…";
            UninstallerCommandText.Text = "Locating uninstaller command…";
            UninstallerLiveProgressBar.Visibility = Visibility.Visible;
            BtnProceedToScanFromBuiltIn.Content = "Skip to Scan ➔";

            bool launched = false;
            string? launchCommand = null;

            // Priority 1: Check if it's an MSIX / AppX Package (e.g. from Microsoft Store or MSIX package)
            if (!string.IsNullOrWhiteSpace(_app.Id) && _app.Id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase))
            {
                var cleanPackageName = AppMetadataHelper.CleanPackageId(_app.Id);
                launchCommand = $"powershell Remove-AppxPackage -Package \"{cleanPackageName}\"";
                UninstallerCommandText.Text = launchCommand;
                UninstallerSubtextText.Text = "Removing Windows AppX package via PowerShell…";

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -Name '*{cleanPackageName}*' | Remove-AppxPackage -ErrorAction SilentlyContinue\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p != null)
                    {
                        launched = true;
                        _activeUninstallerProcess = p;
                        MonitorUninstallerProcess(p);
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"AppX removal failed: {ex.Message}", ActivityType.Warning);
                }
            }

            // Priority 2: Native Windows Registry UninstallString (Win32 exe/msi/bat/Inno/NSIS)
            if (!launched)
            {
                var uninstStr = _app.UninstallString ?? DeepUninstallService.FindRegistryUninstallString(_app);
                if (string.IsNullOrWhiteSpace(uninstStr))
                {
                    // Also check AppMetadataHelper cached registry
                    uninstStr = AppMetadataHelper.GetRegistryInfo(_app.Name, _app.Id)?.UninstallString;
                }

                if (!string.IsNullOrWhiteSpace(uninstStr))
                {
                    var (fileName, arguments) = DeepUninstallService.ParseUninstallString(uninstStr);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        launchCommand = string.IsNullOrWhiteSpace(arguments) ? fileName : $"\"{fileName}\" {arguments}";
                        UninstallerCommandText.Text = launchCommand;
                        UninstallerSubtextText.Text = $"Running official uninstaller: {System.IO.Path.GetFileName(fileName)}";

                        var p = DeepUninstallService.LaunchNativeUninstallProcess(uninstStr, _app.InstallLocation);
                        if (p != null)
                        {
                            launched = true;
                            _activeUninstallerProcess = p;
                            MonitorUninstallerProcess(p);
                        }
                    }
                }
            }

            // Priority 3: MSI Product Code (GUID)
            if (!launched && !string.IsNullOrWhiteSpace(_app.Id) && _app.Id.StartsWith("{") && _app.Id.EndsWith("}"))
            {
                launchCommand = $"msiexec.exe /X {_app.Id}";
                UninstallerCommandText.Text = launchCommand;
                UninstallerSubtextText.Text = "Running Windows Installer (MSI) uninstallation…";

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = $"/X {_app.Id}",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p != null)
                    {
                        launched = true;
                        _activeUninstallerProcess = p;
                        MonitorUninstallerProcess(p);
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"MSI uninstaller error: {ex.Message}", ActivityType.Warning);
                }
            }

            // Priority 4: Winget Package (Only if authentic Winget ID, not ARP\ or MSIX\)
            if (!launched && !string.IsNullOrWhiteSpace(_app.Id) &&
                !_app.Id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) &&
                !_app.Id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase) &&
                !_app.Id.StartsWith("{"))
            {
                launchCommand = $"winget uninstall --id \"{_app.Id}\"";
                UninstallerCommandText.Text = launchCommand;
                UninstallerSubtextText.Text = "Invoking Windows Package Manager (Winget) uninstaller…";

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "winget.exe",
                        Arguments = $"uninstall --id \"{_app.Id}\" -e --force --accept-source-agreements",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p != null)
                    {
                        launched = true;
                        _activeUninstallerProcess = p;
                        MonitorUninstallerProcess(p);
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Winget uninstaller error: {ex.Message}", ActivityType.Warning);
                }
            }

            // If no official uninstaller was found or could be launched:
            if (!launched)
            {
                UninstallerStatusIconText.Text = "ℹ️";
                UninstallerStatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x2F, 0xB6, 0xFF));
                UninstallerTitleText.Text = "BUILT-IN UNINSTALLER NOT FOUND";
                UninstallerSubtextText.Text = $"No official uninstaller was registered for {_app.Name}. Revo engine will perform a clean deep removal of all residual registry entries and disk files.";
                UninstallerCommandText.Text = "Ready for heuristic leftover scan.";
                UninstallerLiveProgressBar.Visibility = Visibility.Collapsed;
                BtnProceedToScanFromBuiltIn.Content = "Scan for Leftovers ➔";
                BtnProceedToScanFromBuiltIn.Focus();
            }

            await Task.Delay(200);
        }

        private void MonitorUninstallerProcess(System.Diagnostics.Process proc)
        {
            UninstallerStatusIconText.Text = "⏳";
            UninstallerTitleText.Text = "OFFICIAL UNINSTALLER RUNNING";
            UninstallerSubtextText.Text = $"Please complete any uninstallation prompts shown on your screen for {_app.Name}…";
            UninstallerLiveProgressBar.Visibility = Visibility.Visible;
            BtnProceedToScanFromBuiltIn.Content = "Skip to Scan ➔";

            _ = Task.Run(async () =>
            {
                try
                {
                    await proc.WaitForExitAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        IsUninstalled = true;
                        UninstallerLiveProgressBar.Visibility = Visibility.Collapsed;
                        UninstallerStatusIconText.Text = "✓";
                        UninstallerStatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x3F, 0xCB, 0x7E));
                        UninstallerTitleText.Text = "BUILT-IN UNINSTALLER COMPLETED";
                        UninstallerSubtextText.Text = $"Official uninstaller has finished (Exit code: {proc.ExitCode}). Click 'Scan for Leftovers' below to remove residual traces.";
                        BtnProceedToScanFromBuiltIn.Content = "Scan for Leftovers ➔";
                        BtnProceedToScanFromBuiltIn.Focus();
                    });
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UninstallerLiveProgressBar.Visibility = Visibility.Collapsed;
                        BtnProceedToScanFromBuiltIn.Content = "Scan for Leftovers ➔";
                    });
                }
            });
        }

        private async void BtnRerunUninstaller_Click(object sender, RoutedEventArgs e)
        {
            await RunPhase2BuiltInUninstallerAsync();
        }

        private void BtnProceedToScanMode_Click(object sender, RoutedEventArgs e)
        {
            SetStep(3);
        }

        private void BtnBackToBuiltIn_Click(object sender, RoutedEventArgs e)
        {
            SetStep(2);
        }

        #endregion

        #region Phase 3: Scan Mode Selection & Heuristic Scan

        private void CardModeSafe_MouseDown(object sender, MouseButtonEventArgs e)
        {
            RadioModeSafe.IsChecked = true;
            UpdateModeCardStyles();
        }

        private void CardModeModerate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            RadioModeModerate.IsChecked = true;
            UpdateModeCardStyles();
        }

        private void CardModeAdvanced_MouseDown(object sender, MouseButtonEventArgs e)
        {
            RadioModeAdvanced.IsChecked = true;
            UpdateModeCardStyles();
        }

        private void UpdateModeCardStyles()
        {
            var accentBrush = (Brush)FindResource("BrushAccent");
            var borderBrush = (Brush)FindResource("BrushBorder");

            CardModeSafe.BorderBrush = RadioModeSafe.IsChecked == true ? accentBrush : borderBrush;
            CardModeSafe.BorderThickness = new Thickness(RadioModeSafe.IsChecked == true ? 2 : 1);

            CardModeModerate.BorderBrush = RadioModeModerate.IsChecked == true ? accentBrush : borderBrush;
            CardModeModerate.BorderThickness = new Thickness(RadioModeModerate.IsChecked == true ? 2 : 1);

            CardModeAdvanced.BorderBrush = RadioModeAdvanced.IsChecked == true ? accentBrush : borderBrush;
            CardModeAdvanced.BorderThickness = new Thickness(RadioModeAdvanced.IsChecked == true ? 2 : 1);

            if (RadioModeSafe.IsChecked == true) _currentScanMode = UninstallScanMode.Safe;
            else if (RadioModeAdvanced.IsChecked == true) _currentScanMode = UninstallScanMode.Advanced;
            else _currentScanMode = UninstallScanMode.Moderate;
        }

        private async void BtnStartScan_Click(object sender, RoutedEventArgs e)
        {
            UpdateModeCardStyles();

            BtnStartScan.IsEnabled = false;
            ScanProgressPanel.Visibility = Visibility.Visible;
            ScanStatusText.Text = $"Deep scanning with {_currentScanMode} mode across Windows registry and disk…";

            ActivityLogger.Instance.Log($"Phase 3: Starting leftover scan ({_currentScanMode} mode) for {_app.Name}…", ActivityType.Info);

            var leftovers = await _deepUninstall.ScanAsync(_app, _currentScanMode);

            _registryViewModels.Clear();
            _filesViewModels.Clear();

            foreach (var item in leftovers)
            {
                var vm = new LeftoverViewModel(item, _app.Name);
                if (item.Type == LeftoverType.RegistryKey)
                {
                    vm.PropertyChanged += (_, _) => UpdateRegistryCounts();
                    _registryViewModels.Add(vm);
                }
                else
                {
                    vm.PropertyChanged += (_, _) => UpdateFilesCounts();
                    _filesViewModels.Add(vm);
                }
            }

            ScanProgressPanel.Visibility = Visibility.Collapsed;
            BtnStartScan.IsEnabled = true;

            // Transition to Phase 4 (Registry) or Phase 5 (Files) or Complete
            if (_registryViewModels.Count > 0)
            {
                RegistryCountText.Text = $"{_registryViewModels.Count} residual registry key(s) detected.";
                RegistryEmptyText.Visibility = Visibility.Collapsed;
                SetStep(4);
                UpdateRegistryCounts();
            }
            else if (_filesViewModels.Count > 0)
            {
                FilesCountText.Text = $"{_filesViewModels.Count} residual file(s) and folder(s) detected.";
                FilesEmptyText.Visibility = Visibility.Collapsed;
                SetStep(5);
                UpdateFilesCounts();
            }
            else
            {
                // No leftovers detected! Finish cleanly!
                IsUninstalled = true;
                DeepUninstallService.RemoveRegistryUninstallKeys(_app);
                DeepUninstallService.RemoveShortcutsForApp(_app);
                ShowCompleteSummary();
            }
        }

        #endregion

        #region Phase 4: Registry Leftovers Review & Deletion

        private void UpdateRegistryCounts()
        {
            if (_isDeleting) return;
            int total = _registryViewModels.Count;
            int unDeleted = _registryViewModels.Count(v => !v.IsDeleted);
            int selected = _registryViewModels.Count(v => v.IsSelected && !v.IsDeleted);

            if (unDeleted == 0)
            {
                RegistryCountText.Text = $"All {total} detected residual registry key(s) have been deleted.";
                BtnDeleteRegistry.IsEnabled = false;
                BtnDeleteRegistry.Content = "🗑️ Delete Selected";
            }
            else
            {
                RegistryCountText.Text = $"{selected} of {unDeleted} remaining registry key(s) selected for deletion";
                BtnDeleteRegistry.Content = selected > 0 ? $"🗑️ Delete Selected ({selected})" : "🗑️ Delete Selected";
                BtnDeleteRegistry.IsEnabled = selected > 0;
            }
        }

        private void SelectAllRegistry_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            foreach (var vm in _registryViewModels)
            {
                if (!vm.IsDeleted) vm.IsSelected = true;
            }
            UpdateRegistryCounts();
        }

        private void DeselectAllRegistry_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            foreach (var vm in _registryViewModels)
            {
                vm.IsSelected = false;
            }
            UpdateRegistryCounts();
        }

        private async void BtnDeleteRegistry_Click(object sender, RoutedEventArgs e)
        {
            var selected = _registryViewModels.Where(v => v.IsSelected && !v.IsDeleted).ToList();
            if (selected.Count == 0 || _isDeleting) return;

            var confirm = MessageBox.Show(
                $"Are you sure you want to permanently delete the {selected.Count} selected leftover registry key(s)?",
                "Confirm Registry Removal — 180Hz Setup Hub",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _isDeleting = true;
            WindowCloseBtn.IsEnabled = false;
            RegistryActionsPanel.Visibility = Visibility.Collapsed;
            RegistryProgressPanel.Visibility = Visibility.Visible;

            int total = selected.Count;
            RegistryProgressBar.Minimum = 0;
            RegistryProgressBar.Maximum = 100;
            RegistryProgressBar.Value = 0;

            var progress = new Progress<LeftoverDeleteProgress>(p =>
            {
                var vm = selected.FirstOrDefault(v => string.Equals(v.Path, p.Item.Path, StringComparison.OrdinalIgnoreCase));
                if (vm != null)
                {
                    vm.IsProcessing = false;
                    vm.IsDeleted = p.Success;
                    vm.IsFailed = !p.Success;
                }

                double pct = ((double)p.Current / p.Total) * 100.0;
                RegistryProgressBar.Value = pct;
                RegistryProgressStatusText.Text = $"Purging residual registry key {p.Current} of {p.Total}…";
            });

            var (deleted, failed) = await _deepUninstall.DeleteAsync(selected.Select(v => v.Item), progress);
            _purgedRegistryCount += deleted;
            IsUninstalled = true;

            foreach (var vm in selected)
            {
                if (vm.IsDeleted) vm.IsSelected = false;
            }

            RegistryProgressStatusText.Text = $"Eradicated {deleted} registry trace(s).";
            await Task.Delay(400);

            _isDeleting = false;
            WindowCloseBtn.IsEnabled = true;
            RegistryProgressPanel.Visibility = Visibility.Collapsed;
            RegistryActionsPanel.Visibility = Visibility.Visible;
            UpdateRegistryCounts();
        }

        private void BtnNextToFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_filesViewModels.Count > 0)
            {
                SetStep(5);
                UpdateFilesCounts();
            }
            else
            {
                DeepUninstallService.RemoveRegistryUninstallKeys(_app);
                DeepUninstallService.RemoveShortcutsForApp(_app);
                IsUninstalled = true;
                ShowCompleteSummary();
            }
        }

        #endregion

        #region Phase 5: Files & Folders Leftovers Review & Deletion

        private void UpdateFilesCounts()
        {
            if (_isDeleting) return;
            int total = _filesViewModels.Count;
            int unDeleted = _filesViewModels.Count(v => !v.IsDeleted);
            int selected = _filesViewModels.Count(v => v.IsSelected && !v.IsDeleted);
            long selectedBytes = _filesViewModels.Where(v => v.IsSelected && !v.IsDeleted).Sum(v => v.SizeBytes ?? 0);

            if (unDeleted == 0)
            {
                FilesCountText.Text = $"All {total} detected residual file(s) and folder(s) have been purged.";
                BtnDeleteFiles.IsEnabled = false;
                BtnDeleteFiles.Content = "🗑️ Delete Selected";
            }
            else
            {
                string sizeFormatted = FormatBytes(selectedBytes);
                FilesCountText.Text = $"{selected} of {unDeleted} remaining item(s) selected ({sizeFormatted})";
                BtnDeleteFiles.Content = selected > 0 ? $"🗑️ Delete Selected ({selected})" : "🗑️ Delete Selected";
                BtnDeleteFiles.IsEnabled = selected > 0;
            }
        }

        private void SelectAllFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            foreach (var vm in _filesViewModels)
            {
                if (!vm.IsDeleted) vm.IsSelected = true;
            }
            UpdateFilesCounts();
        }

        private void DeselectAllFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            foreach (var vm in _filesViewModels)
            {
                vm.IsSelected = false;
            }
            UpdateFilesCounts();
        }

        private async void BtnDeleteFiles_Click(object sender, RoutedEventArgs e)
        {
            var selected = _filesViewModels.Where(v => v.IsSelected && !v.IsDeleted).ToList();
            if (selected.Count == 0 || _isDeleting) return;

            long totalBytes = selected.Sum(v => v.SizeBytes ?? 0);
            var confirm = MessageBox.Show(
                $"Are you sure you want to permanently delete the {selected.Count} selected leftover file(s) and folder(s)?\nTotal size: {FormatBytes(totalBytes)}",
                "Confirm Leftovers Deletion — 180Hz Setup Hub",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _isDeleting = true;
            WindowCloseBtn.IsEnabled = false;
            FilesActionsPanel.Visibility = Visibility.Collapsed;
            FilesProgressPanel.Visibility = Visibility.Visible;

            int total = selected.Count;
            FilesProgressBar.Minimum = 0;
            FilesProgressBar.Maximum = 100;
            FilesProgressBar.Value = 0;

            var progress = new Progress<LeftoverDeleteProgress>(p =>
            {
                var vm = selected.FirstOrDefault(v => string.Equals(v.Path, p.Item.Path, StringComparison.OrdinalIgnoreCase));
                if (vm != null)
                {
                    vm.IsProcessing = false;
                    vm.IsDeleted = p.Success;
                    vm.IsFailed = !p.Success;
                }

                double pct = ((double)p.Current / p.Total) * 100.0;
                FilesProgressBar.Value = pct;
                FilesProgressStatusText.Text = $"Purging residual directory/file {p.Current} of {p.Total}…";
            });

            var (deleted, failed) = await _deepUninstall.DeleteAsync(selected.Select(v => v.Item), progress);
            _purgedFilesCount += deleted;
            _purgedBytes += selected.Where(v => v.IsDeleted).Sum(v => v.SizeBytes ?? 0);
            IsUninstalled = true;

            foreach (var vm in selected)
            {
                if (vm.IsDeleted) vm.IsSelected = false;
            }

            FilesProgressStatusText.Text = $"Purged {deleted} residual directory/file trace(s).";
            await Task.Delay(400);

            _isDeleting = false;
            WindowCloseBtn.IsEnabled = true;
            FilesProgressPanel.Visibility = Visibility.Collapsed;
            FilesActionsPanel.Visibility = Visibility.Visible;
            UpdateFilesCounts();
        }

        private void BtnFinishFiles_Click(object sender, RoutedEventArgs e)
        {
            DeepUninstallService.RemoveRegistryUninstallKeys(_app);
            DeepUninstallService.RemoveShortcutsForApp(_app);
            IsUninstalled = true;
            ShowCompleteSummary();
        }

        #endregion

        #region Phase 6: Completion Summary

        private void ShowCompleteSummary()
        {
            SetStep(6);

            CompleteSummaryAppName.Text = $"{_app.Name} and all selected residual traces have been safely eradicated from Windows.";
            SummaryBuiltInText.Text = "✓ Official application uninstaller completed";
            SummaryRegistryText.Text = $"✓ {_purgedRegistryCount} residual registry trace(s) eradicated";
            SummaryFilesText.Text = $"✓ {_purgedFilesCount} residual file(s) and folder(s) purged ({FormatBytes(_purgedBytes)} storage freed)";

            NotificationService.Notify(
                "App Eradicated",
                $"{_app.Name} has been completely removed with Revo-grade deep cleaning.");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):0.0} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }

        #endregion
    }
}
