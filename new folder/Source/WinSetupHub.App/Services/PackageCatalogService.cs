using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class PackageCatalogService
    {
        public static event Action<string, bool>? AppInstallationStatusChanged;

        public static event Action? AppUpdatesRefreshed;

        public static void NotifyUpdatesRefreshed()
        {
            AppUpdatesRefreshed?.Invoke();
        }

        public static void NotifyStatusChanged(string idOrName, bool isInstalled)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return;
            AppInstallationStatusChanged?.Invoke(idOrName, isInstalled);
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private List<AppItem>? _cachedPackages;

        public async Task<List<AppItem>> GetAllAsync()
        {
            if (_cachedPackages != null) return _cachedPackages;

            var list = await LoadFromJsonAsync();
            _cachedPackages = list;
            return _cachedPackages;
        }

        public async Task<List<string>> GetCategoriesAsync()
        {
            var packages = await GetAllAsync();
            var categories = packages
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            categories.Insert(0, "All");
            return categories;
        }

        public async Task<List<AppItem>> FilterAsync(string? category, string? search)
        {
            var all = await GetAllAsync();
            var query = all.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                query = query.Where(p =>
                    p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }

        private static readonly Dictionary<string, string[]> KnownPackageAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Spotify.Spotify"] = new[] { "9NCBCSZSJRSB", "Spotify", "Spotify Music", "Spotify - Music and Podcasts", "SpotifyAB.SpotifyMusic" },
            ["9NCBCSZSJRSB"] = new[] { "Spotify.Spotify", "Spotify", "Spotify Music", "SpotifyAB.SpotifyMusic" },
            ["VideoLAN.VLC"] = new[] { "XPDM1ZW6815MQM", "9NBLGGH4VVNH", "VLC media player", "VLC", "VideoLAN VLC" },
            ["9NBLGGH4VVNH"] = new[] { "VideoLAN.VLC", "XPDM1ZW6815MQM", "VLC media player", "VLC" },
            ["XPDM1ZW6815MQM"] = new[] { "VideoLAN.VLC", "9NBLGGH4VVNH", "VLC media player", "VLC" },
            ["Microsoft.VisualStudioCode"] = new[] { "XP9KHM4BK9FZ7Q", "Microsoft.VisualStudioCode", "Visual Studio Code", "VS Code", "Code" },
            ["XP9KHM4BK9FZ7Q"] = new[] { "Microsoft.VisualStudioCode", "Visual Studio Code", "VS Code", "Code" },
            ["WhatsApp.WhatsApp"] = new[] { "9NKSQGP7F2NH", "5319275A.WhatsAppDesktop", "WhatsApp", "WhatsApp Desktop" },
            ["9NKSQGP7F2NH"] = new[] { "WhatsApp.WhatsApp", "5319275A.WhatsAppDesktop", "WhatsApp", "WhatsApp Desktop" },
            ["Telegram.TelegramDesktop"] = new[] { "9NZTWSQNTD0S", "Telegram", "Telegram Desktop" },
            ["9NZTWSQNTD0S"] = new[] { "Telegram.TelegramDesktop", "Telegram", "Telegram Desktop" },
            ["Discord.Discord"] = new[] { "XPDC2RH70K22MN", "Discord" },
            ["XPDC2RH70K22MN"] = new[] { "Discord.Discord", "Discord" },
            ["OpenAI.ChatGPT"] = new[] { "9PLM9XGG6VKS", "ChatGPT", "OpenAI.Codex", "OpenAI ChatGPT" },
            ["9PLM9XGG6VKS"] = new[] { "OpenAI.ChatGPT", "ChatGPT", "OpenAI.Codex" },
            ["Anthropic.Claude"] = new[] { "Claude", "Claude Desktop" },
            ["OpenJS.NodeJS"] = new[] { "OpenJS.NodeJS.LTS", "Node.js", "NodeJS", "Node.js LTS", "NodeJS LTS" },
            ["OpenJS.NodeJS.LTS"] = new[] { "OpenJS.NodeJS", "Node.js", "NodeJS", "Node.js LTS", "NodeJS LTS" },
            ["Python.Python.3.13"] = new[] { "Python 3.13", "Python", "Python3", "Python 3.13.15 Standard Library" },
            ["Python.Python.3.14"] = new[] { "Python 3.14", "Python", "Python3" },
            ["Python.Python.3.12"] = new[] { "Python 3.12", "Python", "Python3" },
            ["Guru3D.Afterburner"] = new[] { "MSI.Afterburner", "MSI Afterburner", "Afterburner" },
            ["MSI.Afterburner"] = new[] { "Guru3D.Afterburner", "MSI Afterburner", "Afterburner" },
            ["Guru3D.RTSS"] = new[] { "RivaTuner Statistics Server", "RTSS", "RivaTuner" },
            ["Valve.Steam"] = new[] { "Valve.SteamCMD", "Steam", "Steam Client" },
            ["Valve.SteamCMD"] = new[] { "Valve.Steam", "Steam" },
            ["Microsoft.WindowsTerminal"] = new[] { "9N0DX20HK701", "Windows Terminal" },
            ["9N0DX20HK701"] = new[] { "Microsoft.WindowsTerminal", "Windows Terminal" },
            ["Microsoft.PowerToys"] = new[] { "XP89DCGQ3K6VLD", "PowerToys", "Microsoft PowerToys" },
            ["XP89DCGQ3K6VLD"] = new[] { "Microsoft.PowerToys", "PowerToys" },
            ["Google.Chrome"] = new[] { "Google Chrome", "Chrome" },
            ["Mozilla.Firefox"] = new[] { "Mozilla Firefox", "Firefox" },
            ["Brave.Brave"] = new[] { "Brave Browser", "Brave", "BraveSoftware.BraveBrowser" },
            ["Opera.Opera"] = new[] { "Opera Stable", "Opera Browser", "Opera" },
            ["Opera.OperaGX"] = new[] { "Opera GX Stable", "Opera GX", "OperaGX" },
            ["Zoom.Zoom"] = new[] { "Zoom Workplace", "Zoom Meetings", "Zoom" },
            ["7zip.7zip"] = new[] { "7-Zip", "7zip", "7-Zip 26.03 (x64)" },
            ["RARLab.WinRAR"] = new[] { "WinRAR", "WinRAR archiver" },
            ["voidtools.Everything"] = new[] { "Everything", "voidtools Everything" },
            ["Notepad++.Notepad++"] = new[] { "Notepad++" },
            ["Git.Git"] = new[] { "Git", "Git for Windows" },
            ["RevoUninstaller.RevoUninstaller"] = new[] { "Revo Uninstaller", "Revo Uninstaller Pro" },
            ["Razer.Synapse"] = new[] { "Razer Synapse", "Razer Synapse 3" },
            ["JetBrains.IntelliJIDEA.Community"] = new[] { "IntelliJ IDEA", "IntelliJ IDEA Community Edition" }
        };

        public static bool AreAliases(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

            if (KnownPackageAliases.TryGetValue(a, out var aliasesA))
            {
                if (aliasesA.Any(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase))) return true;
            }
            if (KnownPackageAliases.TryGetValue(b, out var aliasesB))
            {
                if (aliasesB.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        public async Task CheckInstalledStatusAsync(WingetService winget, IEnumerable<AppItem> items)
        {
            try
            {
                var itemList = items.ToList();

                // 1. Instant check via AppMetadataHelper
                foreach (var item in itemList)
                {
                    bool isInst = AppMetadataHelper.IsAppInstalled(item);
                    if (isInst)
                    {
                        item.IsInstalled = true;
                        item.Status = "Installed";
                    }
                }

                // 2. Comprehensive check via winget installed apps list (MS Store, MSIX, ARP, Winget)
                List<AppItem>? installedApps = null;
                try
                {
                    installedApps = await winget.GetInstalledAppsAsync();
                }
                catch { }

                if (installedApps != null && installedApps.Count > 0)
                {
                    var instIdSet = new HashSet<string>(installedApps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
                    var instCleanIdSet = new HashSet<string>(installedApps.Select(a => AppMetadataHelper.CleanPackageId(a.Id)), StringComparer.OrdinalIgnoreCase);
                    var instNameSet = new HashSet<string>(installedApps.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
                    var instNormNameSet = new HashSet<string>(installedApps.Select(a => AppMetadataHelper.NormalizeAppName(a.Name)).Where(s => s.Length >= 3), StringComparer.OrdinalIgnoreCase);
                    var instStrippedSet = new HashSet<string>(installedApps.Select(a => AppMetadataHelper.StripPublisherPrefix(AppMetadataHelper.NormalizeAppName(a.Name))).Where(s => s.Length >= 3), StringComparer.OrdinalIgnoreCase);

                    foreach (var item in itemList)
                    {
                        if (item.IsInstalled) continue;

                        bool matched = false;

                        // A. Exact or clean ID match
                        if (!string.IsNullOrWhiteSpace(item.Id) && (instIdSet.Contains(item.Id) || instCleanIdSet.Contains(item.Id)))
                        {
                            matched = true;
                        }

                        // B. Known alias match
                        if (!matched && !string.IsNullOrWhiteSpace(item.Id) && KnownPackageAliases.TryGetValue(item.Id, out var aliases))
                        {
                            if (aliases.Any(a => instIdSet.Contains(a) || instCleanIdSet.Contains(a) || instNameSet.Contains(a) || instNormNameSet.Contains(a)))
                            {
                                matched = true;
                            }
                        }

                        // C. Exact Name match
                        if (!matched && !string.IsNullOrWhiteSpace(item.Name) && instNameSet.Contains(item.Name))
                        {
                            matched = true;
                        }

                        // D. Normalized Name match
                        if (!matched && !string.IsNullOrWhiteSpace(item.Name))
                        {
                            var norm = AppMetadataHelper.NormalizeAppName(item.Name);
                            if (norm.Length >= 3 && instNormNameSet.Contains(norm))
                            {
                                matched = true;
                            }
                        }

                        // E. Stripped Publisher Name match
                        if (!matched && !string.IsNullOrWhiteSpace(item.Name))
                        {
                            var stripped = AppMetadataHelper.StripPublisherPrefix(AppMetadataHelper.NormalizeAppName(item.Name));
                            if (stripped.Length >= 3 && instStrippedSet.Contains(stripped))
                            {
                                matched = true;
                            }
                        }

                        // F. Word-bounded or alias matching across all installed apps
                        if (!matched)
                        {
                            var itemCleanId = AppMetadataHelper.CleanPackageId(item.Id);
                            var itemNorm = AppMetadataHelper.NormalizeAppName(item.Name);
                            var itemStripped = AppMetadataHelper.StripPublisherPrefix(itemNorm);

                            matched = installedApps.Any(a =>
                            {
                                if (!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(item.Id))
                                {
                                    if (a.Id.Contains(item.Id, StringComparison.OrdinalIgnoreCase) ||
                                        item.Id.Contains(a.Id, StringComparison.OrdinalIgnoreCase))
                                        return true;

                                    var cleanA = AppMetadataHelper.CleanPackageId(a.Id);
                                    if (cleanA.Equals(itemCleanId, StringComparison.OrdinalIgnoreCase) ||
                                        cleanA.Contains(itemCleanId, StringComparison.OrdinalIgnoreCase) ||
                                        itemCleanId.Contains(cleanA, StringComparison.OrdinalIgnoreCase))
                                        return true;
                                }

                                if (AreAliases(item.Id, a.Id) || AreAliases(item.Name, a.Name))
                                    return true;

                                if (!string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(itemNorm))
                                {
                                    var normA = AppMetadataHelper.NormalizeAppName(a.Name);
                                    var strippedA = AppMetadataHelper.StripPublisherPrefix(normA);

                                    if (normA.Equals(itemNorm, StringComparison.OrdinalIgnoreCase) ||
                                        strippedA.Equals(itemStripped, StringComparison.OrdinalIgnoreCase))
                                        return true;

                                    if (normA.Length >= 4 && (normA.StartsWith(itemNorm + " ") || normA.EndsWith(" " + itemNorm) || normA.Contains(" " + itemNorm + " ")))
                                        return true;
                                    if (itemNorm.Length >= 4 && (itemNorm.StartsWith(normA + " ") || itemNorm.EndsWith(" " + normA) || itemNorm.Contains(" " + normA + " ")))
                                        return true;

                                    if (strippedA.Length >= 4 && (strippedA.StartsWith(itemStripped + " ") || strippedA.EndsWith(" " + itemStripped) || strippedA.Contains(" " + itemStripped + " ")))
                                        return true;
                                }

                                return false;
                            });
                        }

                        if (matched)
                        {
                            item.IsInstalled = true;
                            item.Status = "Installed";
                        }
                    }
                }

                // 3. Correlate with detected updates from UpdateMonitorService
                var upgradable = UpdateMonitorService.Instance.UpgradableApps.ToList();
                if (upgradable.Count > 0)
                {
                    var upMap = upgradable.ToDictionary(u => u.Id, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in itemList)
                    {
                        if (item.IsInstalled)
                        {
                            AppItem? match = null;
                            if (upMap.TryGetValue(item.Id, out var up))
                            {
                                match = up;
                            }
                            else
                            {
                                match = upgradable.FirstOrDefault(u => string.Equals(u.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                            }

                            if (match != null && !string.IsNullOrWhiteSpace(match.AvailableVersion))
                            {
                                item.AvailableVersion = match.AvailableVersion;
                                item.IsUpgrade = true;
                                item.Status = "Update";
                            }
                        }
                    }
                }
            }
            catch
            {
                // Best effort check
            }
        }

        private static bool CheckLocalExeExists(AppItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.LocalIconPath) &&
                item.LocalIconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(item.LocalIconPath))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(item.InstallLocation) && Directory.Exists(item.InstallLocation))
            {
                try
                {
                    if (Directory.EnumerateFiles(item.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly).Any())
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static async Task<List<AppItem>> LoadFromJsonAsync()
        {
            var pathsToTry = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "packages.default.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages.default.json"),
                Path.Combine(Environment.CurrentDirectory, "Configuration", "packages.default.json"),
                Path.Combine(Environment.CurrentDirectory, "WinSetupHub.App", "Configuration", "packages.default.json")
            };

            foreach (var p in pathsToTry)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(p);
                        var items = JsonSerializer.Deserialize<List<AppItem>>(json, JsonOptions);
                        if (items != null && items.Count > 0) return items;
                    }
                    catch
                    {
                        // Try next path
                    }
                }
            }

            // Embedded resource fallback
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("packages.default.json", StringComparison.OrdinalIgnoreCase));

                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = await reader.ReadToEndAsync();
                        var items = JsonSerializer.Deserialize<List<AppItem>>(json, JsonOptions);
                        if (items != null && items.Count > 0) return items;
                    }
                }
            }
            catch
            {
                // Fallback to empty list
            }

            return new List<AppItem>();
        }
    }
}
