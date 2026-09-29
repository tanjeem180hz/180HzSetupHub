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

        public async Task CheckInstalledStatusAsync(WingetService winget, IEnumerable<AppItem> items)
        {
            try
            {
                // 1. Instant check via Windows Registry (0-5 milliseconds, zero UI freeze)
                foreach (var item in items)
                {
                    var reg = AppMetadataHelper.GetRegistryInfo(item.Name, item.Id);
                    if (reg != null)
                    {
                        item.IsInstalled = true;
                    }
                }

                // 2. Background check via winget for store packages and packages not in standard registry
                var installed = await winget.GetInstalledAppsAsync();
                var installedMap = new HashSet<string>(installed.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
                var installedNames = new HashSet<string>(installed.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    if (!item.IsInstalled && (installedMap.Contains(item.Id) || installedNames.Contains(item.Name)))
                    {
                        item.IsInstalled = true;
                    }
                }
            }
            catch
            {
                // Best effort check
            }
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
