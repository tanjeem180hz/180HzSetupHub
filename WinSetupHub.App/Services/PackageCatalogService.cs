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
            _cachedPackages = DeduplicatePackages(list);
            return _cachedPackages;
        }

        public static List<AppItem> DeduplicatePackages(IEnumerable<AppItem> items)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<AppItem>();

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;
                string idKey = item.Id.Trim();
                string normName = NormalizeAppKey(item.Name);

                if (seenIds.Contains(idKey) || (!string.IsNullOrWhiteSpace(normName) && seenNames.Contains(normName)))
                {
                    continue;
                }

                if (KnownPackageAliases.TryGetValue(idKey, out var aliases))
                {
                    if (aliases.Any(a => seenIds.Contains(a)))
                    {
                        continue;
                    }
                }

                seenIds.Add(idKey);
                if (aliases != null)
                {
                    foreach (var a in aliases)
                    {
                        seenIds.Add(a);
                    }
                }

                if (!string.IsNullOrWhiteSpace(normName))
                {
                    seenNames.Add(normName);
                }
                result.Add(item);
            }

            return result;
        }

        public static string NormalizeAppKey(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var clean = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", "").Trim();
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+(desktop|client|for windows|windows|app|official|edition|community|professional)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            return clean;
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

            return DeduplicatePackages(query);
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
            ["OpenAI.ChatGPT"] = new[] { "9PLM9XGG6VKS", "ChatGPT", "OpenAI.Codex", "9NT1R1C2HH7J", "ChatGPT Desktop", "OpenAI ChatGPT" },
            ["9PLM9XGG6VKS"] = new[] { "OpenAI.ChatGPT", "ChatGPT", "OpenAI.Codex", "9NT1R1C2HH7J", "ChatGPT Desktop" },
            ["9NT1R1C2HH7J"] = new[] { "9PLM9XGG6VKS", "OpenAI.ChatGPT", "ChatGPT", "OpenAI.Codex", "ChatGPT Desktop" },
            ["OpenAI.Codex"] = new[] { "9PLM9XGG6VKS", "OpenAI.ChatGPT", "ChatGPT", "9NT1R1C2HH7J" },
            ["ChatGPT"] = new[] { "9PLM9XGG6VKS", "OpenAI.ChatGPT", "OpenAI.Codex", "9NT1R1C2HH7J", "ChatGPT Desktop" },
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
                if (itemList.Count == 0) return;

                // 1. Instant check via AppMetadataHelper (Registry, Canonical Paths, AppPaths, Start Menu, AppX)
                foreach (var item in itemList)
                {
                    bool isInst = AppMetadataHelper.IsAppInstalled(item);
                    if (isInst)
                    {
                        item.IsInstalled = true;
                        if (UpdateMonitorService.Instance.IsRecentlyUpdated(item.Id, item.Name))
                        {
                            item.IsRecentlyUpdated = true;
                            item.AvailableVersion = "";
                            item.IsUpgrade = false;
                            item.Status = "✓ Up to Date";
                        }
                        else if (!item.HasUpdate)
                        {
                            item.Status = "Installed";
                        }
                    }
                    else
                    {
                        item.IsInstalled = false;
                        item.AvailableVersion = "";
                        item.IsUpgrade = false;
                        item.IsRecentlyUpdated = false;
                        item.Status = "Install";
                    }
                }

                // 2. Comprehensive check via winget installed apps list (MS Store, MSIX, ARP, Winget)
                // Always inspect even if Step 1 marked IsInstalled = true, so we capture exact Winget Version & AvailableVersion!
                List<AppItem>? installedApps = null;
                try
                {
                    installedApps = await winget.GetInstalledAppsAsync();
                }
                catch { }

                if (installedApps != null && installedApps.Count > 0)
                {
                    var byId = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var byCleanId = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var byName = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var byNormName = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var byStrippedName = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);

                    static void PutPreferred(Dictionary<string, AppItem> map, string key, AppItem candidate)
                    {
                        if (string.IsNullOrWhiteSpace(key)) return;
                        if (!map.TryGetValue(key, out var existing) ||
                            (string.IsNullOrWhiteSpace(existing.AvailableVersion) && !string.IsNullOrWhiteSpace(candidate.AvailableVersion)))
                        {
                            map[key] = candidate;
                        }
                    }

                    foreach (var a in installedApps)
                    {
                        PutPreferred(byId, a.Id, a);
                        PutPreferred(byCleanId, AppMetadataHelper.CleanPackageId(a.Id), a);
                        PutPreferred(byName, a.Name, a);
                        var norm = AppMetadataHelper.NormalizeAppName(a.Name);
                        if (norm.Length >= 3)
                        {
                            PutPreferred(byNormName, norm, a);
                            var stripped = AppMetadataHelper.StripPublisherPrefix(norm);
                            if (stripped.Length >= 3)
                            {
                                PutPreferred(byStrippedName, stripped, a);
                            }
                        }
                    }

                    foreach (var item in itemList)
                    {
                        var matchedApp = FindMatchingWingetApp(item, installedApps, byId, byCleanId, byName, byNormName, byStrippedName);
                        if (matchedApp != null)
                        {
                            item.IsInstalled = true;

                            if (!string.IsNullOrWhiteSpace(matchedApp.Version) &&
                                !matchedApp.Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                            {
                                item.Version = matchedApp.Version;
                            }

                            bool recentlyUpdated = UpdateMonitorService.Instance.IsRecentlyUpdated(item.Id, item.Name);
                            if (recentlyUpdated)
                            {
                                item.AvailableVersion = "";
                                item.IsUpgrade = false;
                                item.IsRecentlyUpdated = true;
                                item.Status = "✓ Up to Date";
                            }
                            else if (!string.IsNullOrWhiteSpace(matchedApp.AvailableVersion) &&
                                     !string.Equals(matchedApp.AvailableVersion.Trim(), item.Version?.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                item.AvailableVersion = matchedApp.AvailableVersion;
                                item.IsUpgrade = true;
                                item.IsRecentlyUpdated = false;
                                item.Status = "Update";
                            }
                            else if (!item.HasUpdate)
                            {
                                item.Status = item.IsRecentlyUpdated ? "✓ Up to Date" : "Installed";
                            }
                        }
                    }
                }

                // 3. Correlate with detected updates from UpdateMonitorService (safe indexing without ToDictionary crash)
                var upgradable = UpdateMonitorService.Instance.UpgradableApps.ToList();
                if (upgradable.Count > 0)
                {
                    var upById = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var upByCleanId = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var upByName = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var upByNormName = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
                    var upByStripped = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);

                    foreach (var u in upgradable)
                    {
                        if (!string.IsNullOrWhiteSpace(u.Id)) upById[u.Id] = u;
                        var clean = AppMetadataHelper.CleanPackageId(u.Id);
                        if (!string.IsNullOrWhiteSpace(clean)) upByCleanId[clean] = u;
                        if (!string.IsNullOrWhiteSpace(u.Name)) upByName[u.Name] = u;
                        var norm = AppMetadataHelper.NormalizeAppName(u.Name);
                        if (norm.Length >= 3)
                        {
                            upByNormName[norm] = u;
                            var stripped = AppMetadataHelper.StripPublisherPrefix(norm);
                            if (stripped.Length >= 3) upByStripped[stripped] = u;
                        }
                    }

                    foreach (var item in itemList)
                    {
                        if (UpdateMonitorService.Instance.IsRecentlyUpdated(item.Id, item.Name))
                        {
                            item.IsInstalled = true;
                            item.AvailableVersion = "";
                            item.IsUpgrade = false;
                            item.IsRecentlyUpdated = true;
                            item.Status = "✓ Up to Date";
                            continue;
                        }

                        var match = FindMatchingWingetApp(item, upgradable, upById, upByCleanId, upByName, upByNormName, upByStripped);
                        if (match != null && !string.IsNullOrWhiteSpace(match.AvailableVersion))
                        {
                            item.IsInstalled = true;
                            if (!string.IsNullOrWhiteSpace(match.Version) &&
                                !match.Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                            {
                                item.Version = match.Version;
                            }
                            item.AvailableVersion = match.AvailableVersion;
                            item.IsUpgrade = true;
                            item.IsRecentlyUpdated = false;
                            item.Status = "Update";
                        }
                    }
                }
            }
            catch
            {
                // Best effort check
            }
        }

        private static AppItem? FindMatchingWingetApp(
            AppItem item,
            List<AppItem> pool,
            Dictionary<string, AppItem> byId,
            Dictionary<string, AppItem> byCleanId,
            Dictionary<string, AppItem> byName,
            Dictionary<string, AppItem> byNormName,
            Dictionary<string, AppItem> byStrippedName)
        {
            // A. Exact or clean ID match
            if (!string.IsNullOrWhiteSpace(item.Id))
            {
                if (byId.TryGetValue(item.Id, out var m)) return m;
                if (byCleanId.TryGetValue(item.Id, out m)) return m;
                var cleanItemId = AppMetadataHelper.CleanPackageId(item.Id);
                if (!string.IsNullOrWhiteSpace(cleanItemId) && (byId.TryGetValue(cleanItemId, out m) || byCleanId.TryGetValue(cleanItemId, out m)))
                    return m;
            }

            // B. Known alias match
            if (!string.IsNullOrWhiteSpace(item.Id) && KnownPackageAliases.TryGetValue(item.Id, out var aliases))
            {
                foreach (var a in aliases)
                {
                    if (byId.TryGetValue(a, out var m) ||
                        byCleanId.TryGetValue(a, out m) ||
                        byName.TryGetValue(a, out m) ||
                        byNormName.TryGetValue(a, out m))
                    {
                        return m;
                    }
                }
            }

            // C. Exact Name match
            if (!string.IsNullOrWhiteSpace(item.Name) && byName.TryGetValue(item.Name, out var exactNameMatch))
            {
                return exactNameMatch;
            }

            // D. Normalized Name & Stripped Publisher Name match
            var itemNorm = AppMetadataHelper.NormalizeAppName(item.Name);
            var itemStripped = AppMetadataHelper.StripPublisherPrefix(itemNorm);
            if (itemNorm.Length >= 3 && byNormName.TryGetValue(itemNorm, out var normMatch))
            {
                return normMatch;
            }
            if (itemStripped.Length >= 3 && byStrippedName.TryGetValue(itemStripped, out var strippedMatch))
            {
                return strippedMatch;
            }

            // E. Word-bounded or alias matching across pool
            var itemCleanId = AppMetadataHelper.CleanPackageId(item.Id);
            return pool.FirstOrDefault(a =>
            {
                if (!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(item.Id))
                {
                    if (a.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var cleanA = AppMetadataHelper.CleanPackageId(a.Id);
                    if (!string.IsNullOrWhiteSpace(cleanA) && !string.IsNullOrWhiteSpace(itemCleanId) &&
                        cleanA.Equals(itemCleanId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                if (AreAliases(item.Id, a.Id) || AreAliases(item.Name, a.Name))
                    return true;

                if (!string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(itemNorm))
                {
                    var normA = AppMetadataHelper.NormalizeAppName(a.Name);
                    var strippedA = AppMetadataHelper.StripPublisherPrefix(normA);

                    if (normA.Equals(itemNorm, StringComparison.OrdinalIgnoreCase) ||
                        (itemStripped.Length >= 3 && strippedA.Equals(itemStripped, StringComparison.OrdinalIgnoreCase)))
                        return true;

                    if (normA.Length >= 4 && itemNorm.Length >= 4 &&
                        (normA.StartsWith(itemNorm + " ", StringComparison.OrdinalIgnoreCase) ||
                         normA.EndsWith(" " + itemNorm, StringComparison.OrdinalIgnoreCase) ||
                         normA.Contains(" " + itemNorm + " ", StringComparison.OrdinalIgnoreCase)))
                        return true;

                    if (itemNorm.Length >= 4 && normA.Length >= 4 &&
                        (itemNorm.StartsWith(normA + " ", StringComparison.OrdinalIgnoreCase) ||
                         itemNorm.EndsWith(" " + normA, StringComparison.OrdinalIgnoreCase) ||
                         itemNorm.Contains(" " + normA + " ", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }

                return false;
            });
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
            var localAppDataConfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "180Hz Setup Hub", "Configuration", "packages.default.json");

            var pathsToTry = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "packages.default.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages.default.json"),
                localAppDataConfig,
                Path.Combine(Environment.CurrentDirectory, "Configuration", "packages.default.json"),
                Path.Combine(Environment.CurrentDirectory, "WinSetupHub.App", "Configuration", "packages.default.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Configuration", "packages.default.json")
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
                var candidateAssemblies = new[]
                {
                    typeof(PackageCatalogService).Assembly,
                    Assembly.GetEntryAssembly(),
                    Assembly.GetExecutingAssembly()
                }.Where(a => a != null).Distinct();

                foreach (var assembly in candidateAssemblies)
                {
                    var names = assembly!.GetManifestResourceNames();
                    var resourceName = names.FirstOrDefault(n =>
                        n.EndsWith(".packages.default.json", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("packages.default.json", StringComparison.OrdinalIgnoreCase));

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
            }
            catch
            {
                // Fallback to empty list
            }

            return new List<AppItem>();
        }
    }
}
