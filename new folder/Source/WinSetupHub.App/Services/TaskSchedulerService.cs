using System;
using System.Diagnostics;
using System.IO;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class TaskSchedulerService
    {
        private const string TaskName = "180HzSetupHub_AutoCheck";

        public static void CreateOrUpdateTask(int frequencyHours)
        {
            if (frequencyHours <= 0)
            {
                DeleteTask();
                return;
            }

            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

                var args = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" --auto-check\" /sc HOURLY /mo {frequencyHours} /f";

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to register scheduled task: {ex.Message}", ActivityType.Warning);
            }
        }

        public static void DeleteTask() => RemoveScheduledTask();

        public static void SyncScheduledTask()
        {
            var hours = SettingsService.Instance.Current.UpdateCheckFrequencyHours;
            CreateOrUpdateTask(hours);
        }

        public static void RemoveScheduledTask()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{TaskName}\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }
        }
    }
}
