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

        public async Task ApplyAsync(TweakItem tweak)
        {
            foreach (var reg in tweak.Registry)
            {
                ApplyRegistryEntry(reg, undo: false);
            }

            foreach (var svc in tweak.Service)
            {
                await SetServiceStartupAsync(svc.Name, svc.StartupType);
            }

            foreach (var script in tweak.InvokeScript)
            {
                if (!string.IsNullOrWhiteSpace(script))
                    await RunPowerShellAsync(script);
            }
        }

        public async Task UndoAsync(TweakItem tweak)
        {
            foreach (var reg in tweak.Registry)
            {
                ApplyRegistryEntry(reg, undo: true);
            }

            foreach (var svc in tweak.Service)
            {
                if (!string.IsNullOrWhiteSpace(svc.OriginalType))
                    await SetServiceStartupAsync(svc.Name, svc.OriginalType);
            }

            foreach (var script in tweak.UndoScript)
            {
                if (!string.IsNullOrWhiteSpace(script))
                    await RunPowerShellAsync(script);
            }
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

                // Value deletion handling
                if ((undo && (value == "<RemoveEntry>" || string.IsNullOrEmpty(value))) ||
                    (!undo && value == "<RemoveEntry>"))
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
