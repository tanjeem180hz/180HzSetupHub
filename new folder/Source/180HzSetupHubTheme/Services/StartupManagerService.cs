using System;
using System.Collections.Generic;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class StartupManagerService
    {
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string DisabledSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run180HzDisabled";

        private const string RunSubKeyWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string DisabledSubKeyWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run180HzDisabled";

        public List<StartupItem> GetStartupItems()
        {
            var list = new List<StartupItem>();

            // 1. Current User
            ReadFromHive(Registry.CurrentUser, RunSubKey, "Registry (Current User)", true, list);
            ReadFromHive(Registry.CurrentUser, DisabledSubKey, "Registry (Current User)", false, list);

            // 2. Local Machine
            ReadFromHive(Registry.LocalMachine, RunSubKey, "Registry (Machine)", true, list);
            ReadFromHive(Registry.LocalMachine, DisabledSubKey, "Registry (Machine)", false, list);

            // 3. WOW6432Node Local Machine
            ReadFromHive(Registry.LocalMachine, RunSubKeyWow64, "Registry (Machine WOW64)", true, list);
            ReadFromHive(Registry.LocalMachine, DisabledSubKeyWow64, "Registry (Machine WOW64)", false, list);

            return list;
        }

        private static void ReadFromHive(RegistryKey root, string subKeyPath, string sourceName, bool isEnabled, List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath, false);
                if (key == null) return;

                foreach (var valueName in key.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(valueName)) continue;
                    var command = key.GetValue(valueName)?.ToString() ?? "";

                    list.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = command,
                        Source = sourceName,
                        IsEnabled = isEnabled
                    });
                }
            }
            catch { }
        }

        public void SetEnabled(StartupItem item, bool enabled)
        {
            var isMachine = item.Source.Contains("Machine", StringComparison.OrdinalIgnoreCase);
            var isWow64 = item.Source.Contains("WOW64", StringComparison.OrdinalIgnoreCase);
            var root = isMachine ? Registry.LocalMachine : Registry.CurrentUser;

            var runPath = isWow64 ? RunSubKeyWow64 : RunSubKey;
            var disabledPath = isWow64 ? DisabledSubKeyWow64 : DisabledSubKey;

            var fromKeyPath = enabled ? disabledPath : runPath;
            var toKeyPath = enabled ? runPath : disabledPath;

            try
            {
                using var fromKey = root.OpenSubKey(fromKeyPath, true);
                var val = fromKey?.GetValue(item.Name)?.ToString() ?? item.Command;

                using var toKey = root.CreateSubKey(toKeyPath, true);
                toKey?.SetValue(item.Name, val);

                fromKey?.DeleteValue(item.Name, false);
                item.IsEnabled = enabled;
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(
                    $"Administrator permissions are required to modify startup items in {item.Source}. Please restart 180Hz Setup Hub as Administrator.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update startup item: {ex.Message}", ex);
            }
        }
    }
}
