using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class AppMetadataHelper
    {
        private class RegistryAppInfo
        {
            public string DisplayName { get; set; } = "";
            public string DisplayVersion { get; set; } = "";
            public string Size { get; set; } = "";
            public string? InstallLocation { get; set; }
            public string? DisplayIcon { get; set; }
            public string? UninstallString { get; set; }
        }

        private static Dictionary<string, RegistryAppInfo>? _registryCache;

        public static void EnrichAppItem(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            // 1. If in catalog, copy verified official metadata
            if (catalog != null)
            {
                var match = catalog.FirstOrDefault(c =>
                    string.Equals(c.Id, app.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Name, app.Name, StringComparison.OrdinalIgnoreCase));

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
                    app.LocalIconPath = CleanIconPath(regInfo.DisplayIcon);
                }

                if (!string.IsNullOrWhiteSpace(regInfo.UninstallString))
                {
                    app.UninstallString = regInfo.UninstallString;
                }
            }

            // 3. Fallback size if truly unmeasured: "-" (strictly no fake predictions)
            if (string.IsNullOrWhiteSpace(app.Size))
            {
                app.Size = "-";
            }

            // 4. Try load local icon immediately if available
            if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && File.Exists(app.LocalIconPath))
            {
                var localImg = IconCacheService.GetLocalFileIcon(app.LocalIconPath);
                if (localImg != null)
                {
                    app.IconImageSource = localImg;
                }
            }
        }

        private static string CleanIconPath(string raw)
        {
            var clean = raw.Trim().Trim('"');
            var commaIdx = clean.IndexOf(',');
            if (commaIdx > 0)
            {
                clean = clean.Substring(0, commaIdx).Trim();
            }
            return clean;
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

        private static RegistryAppInfo? GetRegistryInfo(string name, string id)
        {
            EnsureRegistryCache();
            if (_registryCache == null) return null;

            if (_registryCache.TryGetValue(id.ToLowerInvariant(), out var info))
                return info;

            if (_registryCache.TryGetValue(name.ToLowerInvariant(), out var infoName))
                return infoName;

            foreach (var kvp in _registryCache)
            {
                if (kvp.Key.Contains(name.ToLowerInvariant()) || name.ToLowerInvariant().Contains(kvp.Key))
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
                                    UninstallString = uninstStr
                                };

                                if (!string.IsNullOrWhiteSpace(dispName))
                                {
                                    _registryCache[dispName.Trim().ToLowerInvariant()] = entry;
                                }
                                _registryCache[subName.Trim().ToLowerInvariant()] = entry;
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
