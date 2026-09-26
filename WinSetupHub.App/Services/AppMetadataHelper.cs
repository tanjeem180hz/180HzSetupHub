using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class AppMetadataHelper
    {
        private static Dictionary<string, (string Size, string? Path)>? _registrySizeCache;

        public static void EnrichAppItem(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            // 1. If in catalog, copy official metadata
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
                    if (string.IsNullOrWhiteSpace(app.Size) || app.Size == "~50 MB") app.Size = match.Size;
                    if (string.IsNullOrWhiteSpace(app.Description)) app.Description = match.Description;
                    app.AccentColor = match.AccentColor;
                }
            }

            // 2. Query Windows Registry for real installed size if not set or generic
            if (string.IsNullOrWhiteSpace(app.Size) || app.Size.StartsWith("~"))
            {
                var regSize = GetInstalledSizeFromRegistry(app.Name, app.Id);
                if (!string.IsNullOrWhiteSpace(regSize))
                {
                    app.Size = regSize;
                }
                else
                {
                    app.Size = EstimateFallbackSize(app.Name, app.Category);
                }
            }

            // 3. Fallback icon URL if missing
            if (string.IsNullOrWhiteSpace(app.IconUrl))
            {
                var domainGuess = app.Id.Contains('.')
                    ? app.Id.Split('.')[0].ToLowerInvariant() + ".com"
                    : app.Name.Replace(" ", "").ToLowerInvariant() + ".com";

                app.IconUrl = $"https://www.google.com/s2/favicons?domain={domainGuess}&sz=128";
            }

            // 4. Fallback version if missing
            if (string.IsNullOrWhiteSpace(app.Version))
            {
                app.Version = "Latest";
            }
        }

        private static string GetInstalledSizeFromRegistry(string name, string id)
        {
            EnsureRegistryCache();
            if (_registrySizeCache == null) return "";

            if (_registrySizeCache.TryGetValue(id.ToLowerInvariant(), out var info) && !string.IsNullOrWhiteSpace(info.Size))
                return info.Size;

            if (_registrySizeCache.TryGetValue(name.ToLowerInvariant(), out var infoName) && !string.IsNullOrWhiteSpace(infoName.Size))
                return infoName.Size;

            foreach (var kvp in _registrySizeCache)
            {
                if (kvp.Key.Contains(name.ToLowerInvariant()) || name.ToLowerInvariant().Contains(kvp.Key))
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Value.Size)) return kvp.Value.Size;
                }
            }

            return "";
        }

        private static void EnsureRegistryCache()
        {
            if (_registrySizeCache != null) return;
            _registrySizeCache = new Dictionary<string, (string Size, string? Path)>(StringComparer.OrdinalIgnoreCase);

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
                                var sizeObj = appKey.GetValue("EstimatedSize");
                                var installLoc = appKey.GetValue("InstallLocation") as string;

                                string sizeStr = "";
                                if (sizeObj is int sizeKb && sizeKb > 0)
                                {
                                    double mb = sizeKb / 1024.0;
                                    sizeStr = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
                                }

                                if (!string.IsNullOrWhiteSpace(dispName))
                                {
                                    _registrySizeCache[dispName.Trim().ToLowerInvariant()] = (sizeStr, installLoc);
                                }
                                _registrySizeCache[subName.Trim().ToLowerInvariant()] = (sizeStr, installLoc);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        private static string EstimateFallbackSize(string name, string category)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("visual studio") || lower.Contains("game") || lower.Contains("engine") || lower.Contains("cuda")) return "1.6 GB";
            if (lower.Contains("chrome") || lower.Contains("browser") || lower.Contains("edge") || lower.Contains("firefox")) return "120 MB";
            if (lower.Contains("driver") || lower.Contains("nvidia") || lower.Contains("amd") || lower.Contains("intel")) return "520 MB";
            if (lower.Contains("discord") || lower.Contains("slack") || lower.Contains("teams") || lower.Contains("zoom")) return "95 MB";
            if (lower.Contains("office") || lower.Contains("adobe") || lower.Contains("libreoffice")) return "310 MB";
            if (lower.Contains("7-zip") || lower.Contains("rufus") || lower.Contains("everything") || lower.Contains("curl")) return "3.5 MB";
            return "48 MB";
        }
    }
}
