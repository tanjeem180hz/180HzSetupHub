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
            "WPFTweaksDisplay"
        };

        private List<TweakItem>? _cache;

        public async Task<List<TweakItem>> GetAllAsync()
        {
            if (_cache != null) return _cache;
            _cache = await LoadAsync();
            return _cache;
        }

        private static async Task<List<TweakItem>> LoadAsync()
        {
            var pathsToTry = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "tweaks.default.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tweaks.default.json"),
                Path.Combine(Environment.CurrentDirectory, "Configuration", "tweaks.default.json"),
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
                        TagRecommended(items);
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
                    .FirstOrDefault(n => n.EndsWith("tweaks.default.json", StringComparison.OrdinalIgnoreCase));

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
                            TagRecommended(items);
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
                string type = reg.Type;

                (RegistryKey? hive, string subKey) = ParseHivePath(path);
                if (hive == null) return;

                if (undo && value == "<RemoveEntry>")
                {
                    using var key = hive.OpenSubKey(subKey, writable: true);
                    key?.DeleteValue(name, throwOnMissingValue: false);
                    return;
                }

                if (value == "<RemoveEntry>") return;

                using var regKey = hive.CreateSubKey(subKey, writable: true);
                if (regKey == null) return;

                switch (type.ToLowerInvariant())
                {
                    case "dword":
                        if (int.TryParse(value, out int dword))
                            regKey.SetValue(name, dword, RegistryValueKind.DWord);
                        break;
                    case "qword":
                        if (long.TryParse(value, out long qword))
                            regKey.SetValue(name, qword, RegistryValueKind.QWord);
                        break;
                    case "string":
                    case "sz":
                        regKey.SetValue(name, value, RegistryValueKind.String);
                        break;
                    case "expandstring":
                    case "expandsz":
                        regKey.SetValue(name, value, RegistryValueKind.ExpandString);
                        break;
                    default:
                        if (int.TryParse(value, out int def))
                            regKey.SetValue(name, def, RegistryValueKind.DWord);
                        else
                            regKey.SetValue(name, value, RegistryValueKind.String);
                        break;
                }
            }
            catch { }
        }

        private static (RegistryKey? hive, string subKey) ParseHivePath(string path)
        {
            path = path.Replace("/", "\\");
            string[] parts = path.Split(new[] { ':', '\\' }, 3);
            if (parts.Length < 2) return (null, "");

            string hivePart = parts[0].ToUpperInvariant();
            string subKey = path.Contains(':') ? path.Substring(path.IndexOf(':') + 2) : path;

            RegistryKey? hive = hivePart switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKCR" => Registry.ClassesRoot,
                "HKU" => Registry.Users,
                "HKCC" => Registry.CurrentConfig,
                _ => null
            };

            return (hive, subKey);
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
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -Command \"{EscapeForPs(script)}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(30_000);
                }
                catch { }
            });
        }

        private static string EscapeForPs(string script)
            => script.Replace("\"", "\\\"").Replace("\r\n", " ").Replace("\n", " ");
    }
}
