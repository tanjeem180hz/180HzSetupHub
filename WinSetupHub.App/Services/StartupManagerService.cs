using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class StartupManagerService
    {
        // Standard Windows Run Keys
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunSubKeyWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

        // Official Windows Task Manager StartupApproved Keys
        private const string StartupApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string StartupApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        private const string StartupApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        // Legacy Disabled Keys (for backwards compatibility migration)
        private const string DisabledSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run180HzDisabled";
        private const string DisabledSubKeyWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run180HzDisabled";

        private static readonly HashSet<string> NonEssentialKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "discord", "steam", "epicgames", "spotify", "opera", "edgeautolaunch", "onedrive", "skype",
            "ccleaner", "teams", "cortana", "battle.net", "riot", "ea desktop", "ubisoft", "bluestacks",
            "telegram", "whatsapp", "viber", "utorrent", "bittorrent", "qbittorrent", "idm", "neatdm",
            "razerappengine", "logioptionsplus", "corsair", "steelseries", "overwolf", "medal", "lightshot",
            "dropbox", "googledrive", "zoom", "slack", "anydesk", "teamviewer"
        };

        public List<StartupItem> GetStartupItems()
        {
            var list = new List<StartupItem>();

            // 1. Registry HKCU Run
            ReadRegistryEntries(
                Registry.CurrentUser,
                RunSubKey,
                StartupApprovedRun,
                "Registry (Current User)",
                StartupItemType.RegistryCurrentUser,
                list);

            // 2. Registry HKLM Run (64-bit / standard)
            ReadRegistryEntries(
                Registry.LocalMachine,
                RunSubKey,
                StartupApprovedRun,
                "Registry (Machine)",
                StartupItemType.RegistryLocalMachine,
                list);

            // 3. Registry HKLM WOW6432Node Run (32-bit)
            ReadRegistryEntries(
                Registry.LocalMachine,
                RunSubKeyWow64,
                StartupApprovedRun32,
                "Registry (Machine WOW64)",
                StartupItemType.RegistryLocalMachineWow64,
                list);

            // 4. User Startup Folder (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup)
            ReadStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Registry.CurrentUser,
                StartupApprovedFolder,
                "Startup Folder (User)",
                StartupItemType.StartupFolderUser,
                list);

            // 5. All Users / Common Startup Folder (%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup)
            ReadStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                Registry.LocalMachine,
                StartupApprovedFolder,
                "Startup Folder (All Users)",
                StartupItemType.StartupFolderCommon,
                list);

            // 6. Check for legacy disabled entries if any exist
            ReadLegacyDisabledEntries(
                Registry.CurrentUser,
                DisabledSubKey,
                "Registry (Current User)",
                StartupItemType.RegistryCurrentUser,
                list);

            ReadLegacyDisabledEntries(
                Registry.LocalMachine,
                DisabledSubKey,
                "Registry (Machine)",
                StartupItemType.RegistryLocalMachine,
                list);

            ReadLegacyDisabledEntries(
                Registry.LocalMachine,
                DisabledSubKeyWow64,
                "Registry (Machine WOW64)",
                StartupItemType.RegistryLocalMachineWow64,
                list);

            // 7. Deduplicate while preserving source accuracy
            var deduplicated = DeduplicateItems(list);

            // 8. Correlate with history & persistent state database
            var historyService = StartupHistoryService.Instance;
            foreach (var item in deduplicated)
            {
                var lastHistory = historyService.GetLastHistory(item.Name);
                if (lastHistory != null)
                {
                    item.LastActionDescription = $"{lastHistory.Action} on {lastHistory.Timestamp:MMM dd, HH:mm}";
                    item.LastActionTime = lastHistory.Timestamp;
                }
                else if (item.LastActionTime.HasValue)
                {
                    item.LastActionDescription = $"{(item.IsEnabled ? "Enabled" : "Disabled")} in Windows ({item.LastActionTime.Value:MMM dd, yyyy})";
                }
                else
                {
                    item.LastActionDescription = item.IsEnabled ? "Active on Windows Startup" : "Disabled";
                }
            }

            // Save snapshot of known states
            historyService.SaveKnownStates(deduplicated);

            return deduplicated.OrderBy(i => !i.IsEnabled).ThenBy(i => i.Name).ToList();
        }

        private static void ReadRegistryEntries(
            RegistryKey root,
            string runPath,
            string approvedPath,
            string sourceName,
            StartupItemType startupType,
            List<StartupItem> list)
        {
            try
            {
                using var runKey = root.OpenSubKey(runPath, false);
                if (runKey == null) return;

                foreach (var valueName in runKey.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(valueName)) continue;
                    var command = runKey.GetValue(valueName)?.ToString() ?? "";

                    var isEnabled = DetermineApprovedState(root, approvedPath, valueName, out var toggleTime);

                    list.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = command,
                        Source = sourceName,
                        StartupType = startupType,
                        IsEnabled = isEnabled,
                        LastActionTime = toggleTime
                    });
                }
            }
            catch { }
        }

        private static void ReadStartupFolder(
            string folderPath,
            RegistryKey approvedRoot,
            string approvedPath,
            string sourceName,
            StartupItemType startupType,
            List<StartupItem> list)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

            try
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is not (".lnk" or ".url" or ".bat" or ".cmd" or ".exe" or ".disabled")) continue;

                    var rawFileName = Path.GetFileName(file);
                    bool isExplicitDisabled = false;
                    var normalizedFileName = rawFileName;

                    if (ext == ".disabled")
                    {
                        isExplicitDisabled = true;
                        normalizedFileName = Path.GetFileNameWithoutExtension(rawFileName);
                    }

                    var displayName = Path.GetFileNameWithoutExtension(normalizedFileName);

                    // Key in StartupApproved is the file name (e.g. "AULA F75.lnk")
                    var isApproved = DetermineApprovedState(approvedRoot, approvedPath, normalizedFileName, out var toggleTime);

                    if (toggleTime == null)
                    {
                        var altEnabled = DetermineApprovedState(approvedRoot, approvedPath, displayName, out var altTime);
                        if (altTime != null)
                        {
                            isApproved = altEnabled;
                            toggleTime = altTime;
                        }
                    }

                    var isEnabled = isExplicitDisabled ? false : isApproved;
                    var command = (normalizedFileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        ? ResolveShortcutTarget(file)
                        : file;

                    list.Add(new StartupItem
                    {
                        Name = displayName,
                        Command = command,
                        Source = sourceName,
                        StartupType = startupType,
                        TargetFilePath = file,
                        IsEnabled = isEnabled,
                        LastActionTime = toggleTime
                    });
                }
            }
            catch { }
        }

        private static void ReadLegacyDisabledEntries(
            RegistryKey root,
            string disabledSubKey,
            string sourceName,
            StartupItemType startupType,
            List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(disabledSubKey, false);
                if (key == null) return;

                foreach (var valueName in key.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(valueName)) continue;
                    if (list.Any(i => string.Equals(i.Name, valueName, StringComparison.OrdinalIgnoreCase))) continue;

                    var command = key.GetValue(valueName)?.ToString() ?? "";

                    list.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = command,
                        Source = sourceName,
                        StartupType = startupType,
                        IsEnabled = false,
                        LastActionDescription = "Disabled (Legacy Hub Storage)"
                    });
                }
            }
            catch { }
        }

        private static bool DetermineApprovedState(RegistryKey root, string approvedPath, string valueName, out DateTime? toggleTime)
        {
            toggleTime = null;
            try
            {
                using var approvedKey = root.OpenSubKey(approvedPath, false);
                if (approvedKey != null)
                {
                    var obj = approvedKey.GetValue(valueName);
                    if (obj is byte[] bytes && bytes.Length > 0)
                    {
                        var isEnabled = (bytes[0] & 1) == 0;
                        if (bytes.Length >= 12)
                        {
                            try
                            {
                                var fileTime = BitConverter.ToInt64(bytes, 4);
                                if (fileTime > 0)
                                {
                                    toggleTime = DateTime.FromFileTime(fileTime);
                                }
                            }
                            catch { }
                        }
                        return isEnabled;
                    }
                }
            }
            catch { }

            return true;
        }

        public static string ResolveShortcutTarget(string shortcutPath)
        {
            if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !shortcutPath.EndsWith(".lnk.disabled", StringComparison.OrdinalIgnoreCase))
            {
                return shortcutPath;
            }

            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic sc = shell.CreateShortcut(shortcutPath);
                        string target = sc.TargetPath;
                        string args = sc.Arguments;
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            return string.IsNullOrWhiteSpace(args) ? target : $"\"{target}\" {args}";
                        }
                    }
                }
            }
            catch { }

            return shortcutPath;
        }

        private static List<StartupItem> DeduplicateItems(List<StartupItem> items)
        {
            var unique = new List<StartupItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var key = $"{item.Name}|{item.Source}";
                if (seen.Add(key))
                {
                    unique.Add(item);
                }
            }

            return unique;
        }

        public void SetEnabled(StartupItem item, bool enabled)
        {
            try
            {
                switch (item.StartupType)
                {
                    case StartupItemType.RegistryCurrentUser:
                        WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedRun, item.Name, enabled);
                        MigrateLegacyIfPresent(Registry.CurrentUser, RunSubKey, DisabledSubKey, item.Name, item.Command);
                        break;

                    case StartupItemType.RegistryLocalMachine:
                        try
                        {
                            WriteStartupApprovedFlag(Registry.LocalMachine, StartupApprovedRun, item.Name, enabled);
                            MigrateLegacyIfPresent(Registry.LocalMachine, RunSubKey, DisabledSubKey, item.Name, item.Command);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Standard user fallback: HKCU StartupApproved overrides HKLM for the current user
                            WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedRun, item.Name, enabled);
                        }
                        // Always sync to HKCU for double enforcement
                        try { WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedRun, item.Name, enabled); } catch { }
                        break;

                    case StartupItemType.RegistryLocalMachineWow64:
                        try
                        {
                            WriteStartupApprovedFlag(Registry.LocalMachine, StartupApprovedRun32, item.Name, enabled);
                            MigrateLegacyIfPresent(Registry.LocalMachine, RunSubKeyWow64, DisabledSubKeyWow64, item.Name, item.Command);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedRun, item.Name, enabled);
                        }
                        try { WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedRun, item.Name, enabled); } catch { }
                        break;

                    case StartupItemType.StartupFolderUser:
                        var rawNameUser = !string.IsNullOrWhiteSpace(item.TargetFilePath)
                            ? Path.GetFileName(item.TargetFilePath)
                            : item.Name + ".lnk";
                        var fileNameUser = rawNameUser.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                            ? rawNameUser.Substring(0, rawNameUser.Length - ".disabled".Length)
                            : rawNameUser;

                        WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedFolder, fileNameUser, enabled);

                        // File rename enforcement: .lnk <-> .lnk.disabled
                        if (!string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath))
                        {
                            try
                            {
                                if (!enabled && !item.TargetFilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                                {
                                    var newPath = item.TargetFilePath + ".disabled";
                                    File.Move(item.TargetFilePath, newPath, true);
                                    item.TargetFilePath = newPath;
                                }
                                else if (enabled && item.TargetFilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                                {
                                    var newPath = item.TargetFilePath.Substring(0, item.TargetFilePath.Length - ".disabled".Length);
                                    File.Move(item.TargetFilePath, newPath, true);
                                    item.TargetFilePath = newPath;
                                }
                            }
                            catch { }
                        }
                        break;

                    case StartupItemType.StartupFolderCommon:
                        var rawNameCommon = !string.IsNullOrWhiteSpace(item.TargetFilePath)
                            ? Path.GetFileName(item.TargetFilePath)
                            : item.Name + ".lnk";
                        var fileNameCommon = rawNameCommon.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                            ? rawNameCommon.Substring(0, rawNameCommon.Length - ".disabled".Length)
                            : rawNameCommon;

                        try
                        {
                            WriteStartupApprovedFlag(Registry.LocalMachine, StartupApprovedFolder, fileNameCommon, enabled);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedFolder, fileNameCommon, enabled);
                        }
                        break;

                    default:
                        WriteStartupApprovedFlag(Registry.CurrentUser, StartupApprovedRun, item.Name, enabled);
                        break;
                }

                // Update item model
                item.IsEnabled = enabled;
                item.LastActionTime = DateTime.Now;
                item.LastActionDescription = $"Turned {(enabled ? "ON" : "OFF")} just now";

                // Persist into History and States JSON
                StartupHistoryService.Instance.RecordAction(item.Name, enabled, item.Source, item.Command);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to force update startup item {item.Name}: {ex.Message}", ex);
            }
        }

        public void SetEnabledBatch(IEnumerable<StartupItem> items, bool enabled)
        {
            foreach (var item in items)
            {
                try
                {
                    if (item.IsEnabled != enabled)
                    {
                        SetEnabled(item, enabled);
                    }
                }
                catch { }
            }
        }

        public bool IsNonEssential(StartupItem item)
        {
            var text = $"{item.Name} {item.Command} {item.Source}";
            return NonEssentialKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        public List<StartupItem> GetNonEssentialItems(IEnumerable<StartupItem> items)
        {
            return items.Where(i => i.IsEnabled && IsNonEssential(i)).ToList();
        }

        private static void WriteStartupApprovedFlag(RegistryKey root, string approvedPath, string valueName, bool enabled)
        {
            using var key = root.CreateSubKey(approvedPath, true);
            if (key == null)
            {
                throw new InvalidOperationException($"Unable to open or create registry subkey: {approvedPath}");
            }

            byte[] bytes = new byte[12];
            if (key.GetValue(valueName) is byte[] existing && existing.Length >= 12)
            {
                Array.Copy(existing, bytes, 12);
            }

            bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
            var timeBytes = BitConverter.GetBytes(DateTime.UtcNow.ToFileTime());
            Array.Copy(timeBytes, 0, bytes, 4, 8);

            key.SetValue(valueName, bytes, RegistryValueKind.Binary);
        }

        private static void MigrateLegacyIfPresent(RegistryKey root, string runPath, string disabledPath, string valueName, string command)
        {
            try
            {
                using var disKey = root.OpenSubKey(disabledPath, true);
                if (disKey != null && disKey.GetValue(valueName) != null)
                {
                    using var runKey = root.CreateSubKey(runPath, true);
                    if (runKey != null && runKey.GetValue(valueName) == null)
                    {
                        runKey.SetValue(valueName, command);
                    }
                    disKey.DeleteValue(valueName, false);
                }
            }
            catch { }
        }
    }
}
