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

        public Task CheckInstalledStatusAsync(WingetService winget, IEnumerable<AppItem> items)
        {
            try
            {
                // Invalidate registry cache before full recheck to ensure fresh, authentic state
                AppMetadataHelper.InvalidateCache();

                var itemList = items.ToList();

                // 1. Instant check via AppMetadataHelper
                foreach (var item in itemList)
                {
                    bool isInst = AppMetadataHelper.IsAppInstalled(item);
                    item.IsInstalled = isInst;
                    item.Status = isInst ? "Installed" : "Install";
                }

                // 2. Correlate with detected updates from UpdateMonitorService
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

            return Task.CompletedTask;
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
