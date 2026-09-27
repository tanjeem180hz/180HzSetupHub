using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class AppMetadataHelper
    {
        public class RegistryAppInfo
        {
            public string DisplayName { get; set; } = "";
            public string DisplayVersion { get; set; } = "";
            public string Size { get; set; } = "";
            public string? InstallLocation { get; set; }
            public string? DisplayIcon { get; set; }
            public string? UninstallString { get; set; }
            public string? WebUrl { get; set; }
        }

        private static Dictionary<string, RegistryAppInfo>? _registryCache;

        public static void EnrichAppItem(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            // 1. Smart match against catalog
            if (catalog != null)
            {
                var match = FindCatalogMatch(app, catalog);
                if (match != null)
                {
                    if (string.IsNullOrWhiteSpace(app.IconUrl)) app.IconUrl = match.IconUrl;
                    if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General") app.Category = match.Category;
                    if (string.IsNullOrWhiteSpace(app.WebUrl)) app.WebUrl = match.WebUrl;
                    if (string.IsNullOrWhiteSpace(app.Size)) app.Size = match.Size;
                    if (string.IsNullOrWhiteSpace(app.Description)) app.Description = match.Description;
                    app.AccentColor = match.AccentColor;
                }
            }

            // 2. Query Windows Registry for exact real installed version, size, and real icon path
            var regInfo = GetRegistryInfo(app.Name, app.Id);
            if (regInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(regInfo.DisplayVersion) && (string.IsNullOrWhiteSpace(app.Version) || app.Version == "Latest"))
                {
                    app.Version = regInfo.DisplayVersion;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.Size))
                {
                    app.Size = regInfo.Size;
                }
                else if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    var dirSize = CalculateDirectorySize(regInfo.InstallLocation);
                    if (!string.IsNullOrWhiteSpace(dirSize))
                    {
                        app.Size = dirSize;
                    }
                }

                if (!string.IsNullOrWhiteSpace(regInfo.DisplayIcon))
                {
                    var cleanIcon = CleanIconPath(regInfo.DisplayIcon);
                    if (File.Exists(cleanIcon))
                    {
                        app.LocalIconPath = cleanIcon;
                    }
                }

                // If no DisplayIcon found, search InstallLocation for primary executable
                if (string.IsNullOrWhiteSpace(app.LocalIconPath) && !string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    try
                    {
                        var exes = Directory.GetFiles(regInfo.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        var matchExe = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(app.Name, StringComparison.OrdinalIgnoreCase))
                                       ?? exes.FirstOrDefault(e => !Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
                        if (matchExe != null && File.Exists(matchExe))
                        {
                            app.LocalIconPath = matchExe;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(regInfo.UninstallString))
                {
                    app.UninstallString = regInfo.UninstallString;
                }

                if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(regInfo.WebUrl))
                {
                    app.WebUrl = regInfo.WebUrl;
                }
            }

            // 3. If local icon still missing, try Windows App Paths registry
            if (string.IsNullOrWhiteSpace(app.LocalIconPath))
            {
                var appPathExe = ResolveFromAppPaths(app.Name) ?? ResolveFromAppPaths(CleanPackageId(app.Id));
                if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
                {
                    app.LocalIconPath = appPathExe;
                }
            }

            // 4. Fallback website for Microsoft Store / Windows built-ins
            if (string.IsNullOrWhiteSpace(app.WebUrl))
            {
                if (app.Name.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase) ||
                    app.Name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                {
                    app.WebUrl = "https://www.microsoft.com";
                }
            }

            // 5. Fallback size if truly unmeasured: "-"
            if (string.IsNullOrWhiteSpace(app.Size))
            {
                app.Size = "-";
            }

            // 6. Derive authentic IconUrl from WebUrl if IconUrl is missing
            if (string.IsNullOrWhiteSpace(app.IconUrl) && !string.IsNullOrWhiteSpace(app.WebUrl))
            {
                var favicon = IconCacheService.DeriveFaviconUrl(app.WebUrl);
                if (!string.IsNullOrWhiteSpace(favicon))
                {
                    app.IconUrl = favicon;
                }
            }

            // 7. Try load local icon immediately if available
            if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && File.Exists(app.LocalIconPath))
            {
                var localImg = IconCacheService.GetLocalFileIcon(app.LocalIconPath);
                if (localImg != null)
                {
                    app.IconImageSource = localImg;
                }
            }
        }

        public static AppItem? FindCatalogMatch(AppItem app, IEnumerable<AppItem> catalog)
        {
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                var match = catalog.FirstOrDefault(c => string.Equals(c.Id, app.Id, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                var cleanId = CleanPackageId(app.Id);
                if (!string.IsNullOrWhiteSpace(cleanId))
                {
                    match = catalog.FirstOrDefault(c =>
                        string.Equals(c.Id, cleanId, StringComparison.OrdinalIgnoreCase) ||
                        cleanId.StartsWith(c.Id, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.EndsWith(cleanId, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }

            return FindCatalogMatchForName(app.Name, catalog);
        }

        public static AppItem? FindCatalogMatchForName(string name, IEnumerable<AppItem> catalog)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // 1. Exact Name match
            var match = catalog.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. DetectionNames match
            match = catalog.FirstOrDefault(c => c.DetectionNames != null && c.DetectionNames.Any(d =>
                string.Equals(d, name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(d, StringComparison.OrdinalIgnoreCase)));
            if (match != null) return match;

            // 3. Normalized Name match (strips (x64), versions, bitness, etc.)
            var normApp = NormalizeAppName(name);
            if (!string.IsNullOrWhiteSpace(normApp) && normApp.Length >= 3)
            {
                match = catalog.FirstOrDefault(c =>
                {
                    var normCat = NormalizeAppName(c.Name);
                    return string.Equals(normCat, normApp, StringComparison.OrdinalIgnoreCase) ||
                           normApp.StartsWith(normCat, StringComparison.OrdinalIgnoreCase) ||
                           normCat.StartsWith(normApp, StringComparison.OrdinalIgnoreCase) ||
                           normApp.Contains(normCat, StringComparison.OrdinalIgnoreCase);
                });
                if (match != null) return match;
            }

            return null;
        }

        public static string NormalizeAppName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();
            s = Regex.Replace(s, @"\s*\([^)]*\)", "");
            s = Regex.Replace(s, @"\s+(v|version\s+)?\d+(\.\d+)*.*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*-\s*[a-z]{2}-[a-z]{2}$", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        public static string CleanPackageId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            var s = id.Trim();
            if (s.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(5);
                var underscore = s.IndexOf('_');
                if (underscore > 0) s = s.Substring(0, underscore);
            }
            else if (s.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
            {
                var lastSlash = s.LastIndexOf('\\');
                if (lastSlash >= 0 && lastSlash + 1 < s.Length)
                {
                    s = s.Substring(lastSlash + 1);
                }
            }
            return s.Trim();
        }

        public static string? FindLocalIconPath(string appName)
        {
            var regInfo = GetRegistryInfo(appName, "");
            if (regInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(regInfo.DisplayIcon))
                {
                    var cleaned = CleanIconPath(regInfo.DisplayIcon);
                    if (File.Exists(cleaned)) return cleaned;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    try
                    {
                        var exes = Directory.GetFiles(regInfo.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        var matchExe = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(appName, StringComparison.OrdinalIgnoreCase))
                                       ?? exes.FirstOrDefault(e => !Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
                        if (matchExe != null && File.Exists(matchExe)) return matchExe;
                    }
                    catch { }
                }
            }

            var appPathExe = ResolveFromAppPaths(appName);
            if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
            {
                return appPathExe;
            }

            return null;
        }

        public static string? ResolveFromAppPaths(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            var clean = query.Trim().Trim('\"', ' ');
            var candidates = new List<string>();

            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(clean + ".exe");
            }
            candidates.Add(clean);

            var firstWord = clean.Split(' ', '-', '_').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstWord))
            {
                if (!firstWord.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(firstWord + ".exe");
                }
                candidates.Add(firstWord);
            }

            string[] appPathRoots = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
            };

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var basePath in appPathRoots)
                {
                    foreach (var cand in candidates)
                    {
                        try
                        {
                            using var key = root.OpenSubKey($@"{basePath}\{cand}");
                            if (key != null)
                            {
                                var path = (key.GetValue("") as string) ?? (key.GetValue("Path") as string);
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    var cleaned = CleanIconPath(path);
                                    if (File.Exists(cleaned)) return cleaned;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        public static string CleanIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var clean = raw.Trim();
            var commaIdx = clean.IndexOf(',');
            if (commaIdx > 0)
            {
                clean = clean.Substring(0, commaIdx).Trim();
            }
            return clean.Trim('\"', ' ');
        }

        private static string CalculateDirectorySize(string folderPath)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                long totalBytes = 0;
                int fileCount = 0;
                foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += fi.Length;
                    if (++fileCount > 1500) break; // Speed-capped at 1500 files
                }
                if (totalBytes > 0)
                {
                    double mb = totalBytes / (1024.0 * 1024.0);
                    return mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb:0.0} MB";
                }
            }
            catch { }
            return "";
        }

        public static RegistryAppInfo? GetRegistryInfo(string name, string id = "")
        {
            EnsureRegistryCache();
            if (_registryCache == null) return null;

            if (!string.IsNullOrWhiteSpace(id) && _registryCache.TryGetValue(id.ToLowerInvariant(), out var info))
                return info;

            var cleanId = CleanPackageId(id).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(cleanId) && _registryCache.TryGetValue(cleanId, out var infoCleanId))
                return infoCleanId;

            if (!string.IsNullOrWhiteSpace(name) && _registryCache.TryGetValue(name.ToLowerInvariant(), out var infoName))
                return infoName;

            var normName = NormalizeAppName(name).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normName) && _registryCache.TryGetValue(normName, out var infoNorm))
                return infoNorm;

            foreach (var kvp in _registryCache)
            {
                var k = kvp.Key;
                if (k.Length < 3) continue;

                if ((!string.IsNullOrWhiteSpace(name) && k.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(normName) && k.Equals(normName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(cleanId) && k.Equals(cleanId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(normName) && (k.StartsWith(normName) || normName.StartsWith(k))))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void EnsureRegistryCache()
        {
            if (_registryCache != null) return;
            _registryCache = new Dictionary<string, RegistryAppInfo>(StringComparer.OrdinalIgnoreCase);

            string[] subKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var path in subKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(path);
                        if (key == null) continue;

                        foreach (var subName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subName);
                                if (appKey == null) continue;

                                var dispName = appKey.GetValue("DisplayName") as string;
                                var dispVer = appKey.GetValue("DisplayVersion") as string;
                                var sizeObj = appKey.GetValue("EstimatedSize");
                                var installLoc = appKey.GetValue("InstallLocation") as string;
                                var dispIcon = appKey.GetValue("DisplayIcon") as string;
                                var uninstStr = appKey.GetValue("UninstallString") as string;
                                var webUrl = (appKey.GetValue("URLInfoAbout") as string)
                                             ?? (appKey.GetValue("HelpLink") as string)
                                             ?? (appKey.GetValue("URLUpdateInfo") as string);

                                string sizeStr = "";
                                if (sizeObj is int sizeKb && sizeKb > 0)
                                {
                                    double mb = sizeKb / 1024.0;
                                    sizeStr = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
                                }

                                var entry = new RegistryAppInfo
                                {
                                    DisplayName = dispName ?? subName,
                                    DisplayVersion = dispVer ?? "",
                                    Size = sizeStr,
                                    InstallLocation = installLoc,
                                    DisplayIcon = dispIcon,
                                    UninstallString = uninstStr,
                                    WebUrl = webUrl
                                };

                                if (!string.IsNullOrWhiteSpace(dispName))
                                {
                                    _registryCache[dispName.Trim().ToLowerInvariant()] = entry;
                                    var norm = NormalizeAppName(dispName).ToLowerInvariant();
                                    if (!string.IsNullOrWhiteSpace(norm))
                                    {
                                        _registryCache[norm] = entry;
                                    }
                                }
                                _registryCache[subName.Trim().ToLowerInvariant()] = entry;
                                var cleanSub = CleanPackageId(subName).ToLowerInvariant();
                                if (!string.IsNullOrWhiteSpace(cleanSub))
                                {
                                    _registryCache[cleanSub] = entry;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
