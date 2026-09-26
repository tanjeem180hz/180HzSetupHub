using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class SettingsPage : UserControl
    {
        private bool _isInitializing = true;
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRunName = "180HzSetupHub";

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadSettings();
        }

        private void LoadSettings()
        {
            _isInitializing = true;
            var settings = SettingsService.Instance.Current;

            ChkAutoStart.IsChecked = settings.AutoStartWithWindows;
            ChkNotifications.IsChecked = settings.ShowNotifications;
            ChkRequireAdmin.IsChecked = settings.RequireAdminForActions;

            // Select ComboBoxItem matching frequency
            foreach (ComboBoxItem item in CmbUpdateFrequency.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out var hours) && hours == settings.UpdateCheckFrequencyHours)
                {
                    CmbUpdateFrequency.SelectedItem = item;
                    break;
                }
            }

            if (CmbUpdateFrequency.SelectedItem == null && CmbUpdateFrequency.Items.Count > 2)
            {
                CmbUpdateFrequency.SelectedIndex = 2; // Daily (24h)
            }

            _isInitializing = false;
        }

        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkAutoStart.IsChecked == true;
            SettingsService.Instance.Current.AutoStartWithWindows = enabled;
            SettingsService.Instance.Save();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    if (enabled)
                    {
                        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(exePath))
                        {
                            key.SetValue(AppRunName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppRunName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Could not update Windows startup registry: {ex.Message}", ActivityType.Warning);
            }

            ActivityLogger.Instance.Log(
                enabled ? "Enabled auto-start with Windows." : "Disabled auto-start with Windows.",
                ActivityType.Info);
        }

        private void ChkNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkNotifications.IsChecked == true;
            SettingsService.Instance.Current.ShowNotifications = enabled;
            SettingsService.Instance.Save();

            ActivityLogger.Instance.Log(
                enabled ? "Enabled Windows toast notifications." : "Disabled Windows toast notifications.",
                ActivityType.Info);
        }

        private void CmbUpdateFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (CmbUpdateFrequency.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out var hours))
            {
                SettingsService.Instance.Current.UpdateCheckFrequencyHours = hours;
                SettingsService.Instance.Save();

                ActivityLogger.Instance.Log($"Set automated update frequency to {hours} hours.", ActivityType.Info);

                // Sync background update monitor and task scheduler
                try
                {
                    UpdateMonitorService.Instance.UpdateInterval();
                    TaskSchedulerService.SyncScheduledTask();
                }
                catch { }
            }
        }

        private void ChkRequireAdmin_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var enabled = ChkRequireAdmin.IsChecked == true;
            SettingsService.Instance.Current.RequireAdminForActions = enabled;
            SettingsService.Instance.Save();

            ActivityLogger.Instance.Log(
                enabled ? "Require Administrator privilege enabled." : "Require Administrator privilege disabled.",
                ActivityType.Info);
        }
    }
}
