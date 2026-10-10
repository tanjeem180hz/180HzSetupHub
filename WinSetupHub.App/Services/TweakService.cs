using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    /// <summary>
    /// Loads WinUtil-compatible tweaks and applies/undoes them safely via
    /// Registry edits, Service configuration, and PowerShell script invocation.
    /// </summary>
    public class TweakService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Recommended tweaks curated category-wise
        private static readonly HashSet<string> RecommendedIds = new(StringComparer.OrdinalIgnoreCase)
        {
            // Essential Tweaks (WinUtil Standard Preset)
            "WPFTweaksActivity",
            "WPFTweaksConsumerFeatures",
            "WPFTweaksDisableExplorerAutoDiscovery",
            "WPFTweaksWPBT",
            "WPFTweaksLocation",
            "WPFTweaksServices",
            "WPFTweaksTelemetry",
            "WPFTweaksDeliveryOptimization",
            "WPFTweaksDiskCleanup",
            "WPFTweaksDeleteTempFiles",
            "WPFTweaksEndTaskOnTaskbar",
            "WPFTweaksRestorePoint",

            // Customize Preferences (Standard desktop responsiveness & productivity)
            "WPFToggleDarkMode",
            "WPFToggleShowExt",
            "WPFToggleLongPaths",
            "WPFToggleBingSearch",
            "WPFToggleStartMenuRecommendations",
            "WPFToggleGameMode",
            "WPFToggleNumLock",
            "WPFToggleDetailedBSoD",

            // Performance Plans
            "WPFAddUltPerf",

            // Advanced Tweaks (Safe popular optimizations)
            "WPFTweaksRightClickMenu",
            "WPFTweaksWindowsAI",
            "WPFTweaksLogiBlock",
            "WPFTweaksRazerBlock",
            "WPFTweaksDisplay",

            // 🚀 Emulator 100% Extreme Performance
            "EmulTweakHypervisorVBS",
            "EmulTweakCpuPriority",
            "EmulTweakGpuPreference",
            "EmulTweakPowerThrottling",

            // 🖥️ Graphical & Visual Responsiveness
            "GfxTweakVisualFX",
            "GfxTweakHAGS",
            "GfxTweakShaderCache",
            "GfxTweakFSE",
            "GfxTweakMPO",

            // ⚡ Extreme Low Latency & Kernel Timers
            "KernelTweakDynamicTick",
            "KernelTweakTSCClock",
            "KernelTweakTSCSync",
            "KernelTweakSysResponsiveness",

            // 🧠 CPU, RAM & Power Tuning
            "CpuTweakUnparkCores",
            "CpuTweakWin32Priority",
            "CpuTweakDisablePaging",
            "MemTweakFlushStandby",

            // 🌐 Network & Ping Tuning
            "NetTweakTcpAckNoDelay",
            "NetTweakNetshRss",

            // 💾 Storage & NVMe Throughput
            "DiskTweakNtfsFast",
            "DiskTweakEnableTrim"
        };

        private List<TweakItem>? _cache;

        public async Task<List<TweakItem>> GetAllAsync()
        {
            if (_cache != null) return _cache;
            _cache = await LoadAsync();
            return _cache;
        }

        public async Task<List<TweakItem>> GetSystemTweaksAsync()
        {
            var all = await GetAllAsync();
            return all.Where(t => !string.Equals(t.Section, "Registry", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public async Task<List<TweakItem>> GetRegistryTweaksAsync()
        {
            var all = await GetAllAsync();
            return all.Where(t => string.Equals(t.Section, "Registry", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static async Task<List<TweakItem>> LoadAsync()
        {
            var result = new List<TweakItem>();

            // 1. Load System tweaks (tweaks.default.json)
            var systemTweaks = await LoadJsonFileOrResourceAsync("tweaks.default.json");
            foreach (var t in systemTweaks)
            {
                if (string.IsNullOrWhiteSpace(t.Section))
                    t.Section = "System";
            }
            TagRecommended(systemTweaks);
            result.AddRange(systemTweaks);

            // 2. Load Registry tweaks (registry_tweaks.default.json)
            var registryTweaks = await LoadJsonFileOrResourceAsync("registry_tweaks.default.json");
            foreach (var t in registryTweaks)
            {
                t.Section = "Registry";
            }
            result.AddRange(registryTweaks);

            return result;
        }

        private static async Task<List<TweakItem>> LoadJsonFileOrResourceAsync(string fileName)
        {
            var pathsToTry = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                Path.Combine(Environment.CurrentDirectory, "Configuration", fileName),
            };

            foreach (var p in pathsToTry)
            {
                if (!File.Exists(p)) continue;
                try
                {
                    var json = await File.ReadAllTextAsync(p);
                    var items = JsonSerializer.Deserialize<List<TweakItem>>(json, JsonOptions);
                    if (items != null && items.Count > 0)
                    {
                        return items;
                    }
                }
                catch { }
            }

            // Embedded resource fallback
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        var items = JsonSerializer.Deserialize<List<TweakItem>>(json, JsonOptions);
                        if (items != null && items.Count > 0)
                        {
                            return items;
                        }
                    }
                }
            }
            catch { }

            return new List<TweakItem>();
        }

        private static void TagRecommended(List<TweakItem> items)
        {
            foreach (var item in items)
            {
                if (RecommendedIds.Contains(item.Id))
                {
                    item.IsRecommended = true;
                }
            }
        }

        private static readonly string BackupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "180Hz Setup Hub", "Data", "backups");
        private static readonly string SnapshotFile = Path.Combine(BackupDir, "prestate_snapshots.json");
        private static readonly object CacheLock = new();
        private static Dictionary<string, TweakPreStateSnapshot>? _preStateCache;

        private static void EnsureCacheLoaded()
        {
            lock (CacheLock)
            {
                if (_preStateCache != null) return;
                _preStateCache = new Dictionary<string, TweakPreStateSnapshot>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (File.Exists(SnapshotFile))
                    {
                        var json = File.ReadAllText(SnapshotFile);
                        var list = JsonSerializer.Deserialize<List<TweakPreStateSnapshot>>(json, JsonOptions);
                        if (list != null)
                        {
                            foreach (var s in list)
                            {
                                string key = $"{s.Path}\\{s.Name}";
                                _preStateCache[key] = s;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static void SaveCache()
        {
            lock (CacheLock)
            {
                try
                {
                    if (!Directory.Exists(BackupDir))
                        Directory.CreateDirectory(BackupDir);

                    var list = _preStateCache?.Values.ToList() ?? new List<TweakPreStateSnapshot>();
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SnapshotFile, json);
                }
                catch { }
            }
        }

        public static void OpenBackupsDirectory()
        {
            try
            {
                if (!Directory.Exists(BackupDir))
                    Directory.CreateDirectory(BackupDir);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = BackupDir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public static string ExportPreTweakRegBackup(IEnumerable<TweakItem> tweaks)
        {
            try
            {
                if (!Directory.Exists(BackupDir))
                    Directory.CreateDirectory(BackupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string regFilePath = Path.Combine(BackupDir, $"tweak_backup_{timestamp}.reg");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Windows Registry Editor Version 5.00");
                sb.AppendLine();
                sb.AppendLine("; ================================================================");
                sb.AppendLine("; 180Hz Setup Hub - Pre-Optimization State Backup");
                sb.AppendLine($"; Created on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("; Double-click this file to restore these settings at any time.");
                sb.AppendLine("; ================================================================");
                sb.AppendLine();

                var regEntries = tweaks.SelectMany(t => t.Registry)
                    .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase);

                foreach (var group in regEntries)
                {
                    string path = group.Key;
                    var (hiveEnum, subKey) = ParseHivePath(path);
                    if (!hiveEnum.HasValue || string.IsNullOrWhiteSpace(subKey)) continue;

                    string fullHiveName = hiveEnum.Value switch
                    {
                        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
                        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
                        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
                        RegistryHive.Users => "HKEY_USERS",
                        RegistryHive.CurrentConfig => "HKEY_CURRENT_CONFIG",
                        _ => "HKEY_LOCAL_MACHINE"
                    };

                    sb.AppendLine($"[{fullHiveName}\\{subKey}]");

                    using var baseKey = RegistryKey.OpenBaseKey(hiveEnum.Value, RegistryView.Registry64);
                    using var key = baseKey?.OpenSubKey(subKey, writable: false);

                    foreach (var entry in group)
                    {
                        string valName = entry.Name;
                        object? liveVal = key?.GetValue(valName);

                        if (liveVal == null)
                        {
                            // If value does not exist, registry syntax to delete value is "Name"=-
                            sb.AppendLine($"\"{EscapeRegString(valName)}\"=-");
                        }
                        else
                        {
                            var kind = key!.GetValueKind(valName);
                            sb.AppendLine(FormatRegFileLine(valName, liveVal, kind));
                        }
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(regFilePath, sb.ToString(), System.Text.Encoding.Unicode);
                return regFilePath;
            }
            catch
            {
                return "";
            }
        }

        private static string FormatRegFileLine(string name, object val, RegistryValueKind kind)
        {
            string escapedName = $"\"{EscapeRegString(name)}\"";
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    uint dw = val is int i ? unchecked((uint)i) : Convert.ToUInt32(val);
                    return $"{escapedName}=dword:{dw:x8}";

                case RegistryValueKind.QWord:
                    ulong qw = val is long l ? unchecked((ulong)l) : Convert.ToUInt64(val);
                    byte[] qBytes = BitConverter.GetBytes(qw);
                    return $"{escapedName}=hex(b):{string.Join(",", qBytes.Select(b => $"{b:x2}"))}";

                case RegistryValueKind.Binary:
                    byte[] bytes = val is byte[] bArr ? bArr : Array.Empty<byte>();
                    return $"{escapedName}=hex:{string.Join(",", bytes.Select(b => $"{b:x2}"))}";

                case RegistryValueKind.MultiString:
                    string[] lines = val is string[] sArr ? sArr : new[] { val.ToString() ?? "" };
                    var msBytes = new List<byte>();
                    foreach (var line in lines)
                    {
                        msBytes.AddRange(System.Text.Encoding.Unicode.GetBytes(line));
                        msBytes.AddRange(new byte[] { 0, 0 });
                    }
                    msBytes.AddRange(new byte[] { 0, 0 });
                    return $"{escapedName}=hex(7):{string.Join(",", msBytes.Select(b => $"{b:x2}"))}";

                case RegistryValueKind.ExpandString:
                    byte[] esBytes = System.Text.Encoding.Unicode.GetBytes((val.ToString() ?? "") + "\0");
                    return $"{escapedName}=hex(2):{string.Join(",", esBytes.Select(b => $"{b:x2}"))}";

                case RegistryValueKind.String:
                default:
                    return $"{escapedName}=\"{EscapeRegString(val.ToString() ?? "")}\"";
            }
        }

        private static string EscapeRegString(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void CapturePreState(TweakRegistryEntry reg)
        {
            EnsureCacheLoaded();
            string keyId = $"{reg.Path}\\{reg.Name}";
            if (_preStateCache!.ContainsKey(keyId)) return; // Keep original pre-tweak state!

            var (hiveEnum, subKey) = ParseHivePath(reg.Path);
            if (!hiveEnum.HasValue || string.IsNullOrWhiteSpace(subKey)) return;

            var snap = new TweakPreStateSnapshot
            {
                Path = reg.Path,
                Name = reg.Name,
                Existed = false
            };

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hiveEnum.Value, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subKey, writable: false);
                if (key != null)
                {
                    var liveVal = key.GetValue(reg.Name);
                    if (liveVal != null)
                    {
                        snap.Existed = true;
                        snap.Type = key.GetValueKind(reg.Name).ToString();
                        if (liveVal is byte[] bArr)
                            snap.Value = string.Join(" ", bArr.Select(b => b.ToString("X2")));
                        else if (liveVal is string[] sArr)
                            snap.Value = string.Join("\n", sArr);
                        else
                            snap.Value = liveVal.ToString();
                    }
                }
            }
            catch { }

            _preStateCache[keyId] = snap;
            SaveCache();
        }

        public async Task ApplyAsync(TweakItem tweak)
        {
            if (tweak == null) return;
            try
            {
                if (tweak.Registry != null)
                {
                    foreach (var reg in tweak.Registry)
                    {
                        if (reg != null)
                        {
                            CapturePreState(reg);
                            ApplyRegistryEntry(reg, undo: false);
                        }
                    }
                }

                if (tweak.Service != null)
                {
                    foreach (var svc in tweak.Service)
                    {
                        if (svc != null && !string.IsNullOrWhiteSpace(svc.Name))
                            await SetServiceStartupAsync(svc.Name, svc.StartupType);
                    }
                }

                if (tweak.InvokeScript != null)
                {
                    foreach (var script in tweak.InvokeScript)
                    {
                        if (!string.IsNullOrWhiteSpace(script))
                            await RunPowerShellAsync(script);
                    }
                }
            }
            catch { }
        }

        public async Task UndoAsync(TweakItem tweak)
        {
            if (tweak == null) return;
            try
            {
                if (tweak.Registry != null)
                {
                    foreach (var reg in tweak.Registry)
                    {
                        if (reg != null)
                            ApplyRegistryEntry(reg, undo: true);
                    }
                }

                if (tweak.Service != null)
                {
                    foreach (var svc in tweak.Service)
                    {
                        if (svc != null && !string.IsNullOrWhiteSpace(svc.Name))
                        {
                            string origType = !string.IsNullOrWhiteSpace(svc.OriginalType) ? svc.OriginalType : "Manual";
                            await SetServiceStartupAsync(svc.Name, origType);
                        }
                    }
                }

                if (tweak.UndoScript != null)
                {
                    foreach (var script in tweak.UndoScript)
                    {
                        if (!string.IsNullOrWhiteSpace(script))
                            await RunPowerShellAsync(script);
                    }
                }
            }
            catch { }
        }

        private static void ApplyRegistryEntry(TweakRegistryEntry reg, bool undo)
        {
            try
            {
                string path = reg.Path;
                string name = reg.Name;
                string value = undo ? reg.OriginalValue : reg.Value;
                string type = reg.Type ?? "DWord";

                var (hiveEnum, subKey) = ParseHivePath(path);
                if (!hiveEnum.HasValue || string.IsNullOrWhiteSpace(subKey)) return;

                using var baseKey = RegistryKey.OpenBaseKey(hiveEnum.Value, RegistryView.Registry64);
                if (baseKey == null) return;

                // When undoing, verify against pre-state snapshot if available
                if (undo)
                {
                    EnsureCacheLoaded();
                    string keyId = $"{reg.Path}\\{reg.Name}";
                    if (_preStateCache != null && _preStateCache.TryGetValue(keyId, out var snap))
                    {
                        if (!snap.Existed)
                        {
                            // Value did not exist originally before optimization -> delete it!
                            using var key = baseKey.OpenSubKey(subKey, writable: true);
                            key?.DeleteValue(name, throwOnMissingValue: false);
                            return;
                        }
                        else
                        {
                            // Restore exact original pre-state
                            value = snap.Value ?? "";
                            type = snap.Type ?? reg.Type ?? "DWord";
                        }
                    }
                    else if (value == "<RemoveEntry>" || string.IsNullOrEmpty(value))
                    {
                        using var key = baseKey.OpenSubKey(subKey, writable: true);
                        key?.DeleteValue(name, throwOnMissingValue: false);
                        return;
                    }
                }

                // Value deletion handling for standard remove entries
                if (!undo && value == "<RemoveEntry>")
                {
                    using var key = baseKey.OpenSubKey(subKey, writable: true);
                    key?.DeleteValue(name, throwOnMissingValue: false);
                    return;
                }

                using var regKey = baseKey.CreateSubKey(subKey, writable: true);
                if (regKey == null) return;

                switch (type.ToLowerInvariant())
                {
                    case "dword":
                        if (uint.TryParse(value, out uint uDword))
                            regKey.SetValue(name, unchecked((int)uDword), RegistryValueKind.DWord);
                        else if (int.TryParse(value, out int dword))
                            regKey.SetValue(name, dword, RegistryValueKind.DWord);
                        else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                                 uint.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexUVal))
                            regKey.SetValue(name, unchecked((int)hexUVal), RegistryValueKind.DWord);
                        break;

                    case "qword":
                        if (ulong.TryParse(value, out ulong uQword))
                            regKey.SetValue(name, unchecked((long)uQword), RegistryValueKind.QWord);
                        else if (long.TryParse(value, out long qword))
                            regKey.SetValue(name, qword, RegistryValueKind.QWord);
                        else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                                 ulong.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ulong hexUQVal))
                            regKey.SetValue(name, unchecked((long)hexUQVal), RegistryValueKind.QWord);
                        break;

                    case "binary":
                        byte[] bytes = ParseHexBytes(value);
                        if (bytes.Length > 0)
                            regKey.SetValue(name, bytes, RegistryValueKind.Binary);
                        break;

                    case "string":
                    case "sz":
                        regKey.SetValue(name, value, RegistryValueKind.String);
                        break;

                    case "expandstring":
                    case "expandsz":
                        regKey.SetValue(name, value, RegistryValueKind.ExpandString);
                        break;

                    case "multistring":
                    case "multisz":
                        var lines = value.Split(new[] { "\r\n", "\n", ";" }, StringSplitOptions.RemoveEmptyEntries);
                        regKey.SetValue(name, lines, RegistryValueKind.MultiString);
                        break;

                    default:
                        if (uint.TryParse(value, out uint defUVal))
                            regKey.SetValue(name, unchecked((int)defUVal), RegistryValueKind.DWord);
                        else if (int.TryParse(value, out int def))
                            regKey.SetValue(name, def, RegistryValueKind.DWord);
                        else
                            regKey.SetValue(name, value, RegistryValueKind.String);
                        break;
                }
            }
            catch { }
        }

        private static (RegistryHive? hive, string subKey) ParseHivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return (null, "");

            // Strip PowerShell provider prefix if present: "Registry::" or "Microsoft.PowerShell.Core\Registry::"
            if (path.Contains("::"))
            {
                path = path.Substring(path.IndexOf("::") + 2);
            }

            path = path.Replace("/", "\\").Trim('\\');

            // Find the first separator: '\' or ':'
            int sepIdx = path.IndexOfAny(new[] { '\\', ':' });
            string hivePart = (sepIdx > 0 ? path.Substring(0, sepIdx) : path).Trim().ToUpperInvariant();
            string subKey = sepIdx > 0 ? path.Substring(sepIdx).TrimStart(':', '\\').Trim() : "";

            RegistryHive? hive = hivePart switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
                "HKU" or "HKEY_USERS" => RegistryHive.Users,
                "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
                _ => null
            };

            return (hive, subKey);
        }

        private static byte[] ParseHexBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();
            var cleaned = hex.Replace("0x", "").Replace(",", " ").Replace("-", " ");
            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var list = new List<byte>();
            foreach (var p in parts)
            {
                if (byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    list.Add(b);
            }
            return list.ToArray();
        }

        private static async Task SetServiceStartupAsync(string serviceName, string startupType)
        {
            string psCmd = startupType.ToLowerInvariant() switch
            {
                "disabled" => $"Set-Service -Name '{serviceName}' -StartupType Disabled -ErrorAction SilentlyContinue; Stop-Service -Name '{serviceName}' -Force -ErrorAction SilentlyContinue",
                "manual" => $"Set-Service -Name '{serviceName}' -StartupType Manual -ErrorAction SilentlyContinue",
                "automatic" => $"Set-Service -Name '{serviceName}' -StartupType Automatic -ErrorAction SilentlyContinue",
                "automaticdelayedstart" => $"Set-Service -Name '{serviceName}' -StartupType AutomaticDelayedStart -ErrorAction SilentlyContinue",
                _ => $"Set-Service -Name '{serviceName}' -StartupType Manual -ErrorAction SilentlyContinue"
            };

            await RunPowerShellAsync(psCmd);
        }

        private static Task RunPowerShellAsync(string script)
        {
            return Task.Run(() =>
            {
                try
                {
                    byte[] bytes = System.Text.Encoding.Unicode.GetBytes(script);
                    string encoded = Convert.ToBase64String(bytes);

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(30_000);
                }
                catch
                {
                    // Fallback to ShellExecute runas if needed
                    try
                    {
                        var psiFallback = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -Command \"{EscapeForPs(script)}\"",
                            UseShellExecute = true,
                            Verb = "runas",
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                            CreateNoWindow = true,
                        };
                        using var procFallback = System.Diagnostics.Process.Start(psiFallback);
                        procFallback?.WaitForExit(30_000);
                    }
                    catch { }
                }
            });
        }

        private static string EscapeForPs(string script)
            => script.Replace("\"", "\\\"").Replace("\r\n", "; ").Replace("\n", "; ");
    }
}
