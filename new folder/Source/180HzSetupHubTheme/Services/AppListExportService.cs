using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class AppListExportService
    {
        private readonly WingetService _winget = new();
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public class ExportEntry
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
        }

        public async Task ExportAsync(string filePath, IEnumerable<AppItem>? apps = null)
        {
            var list = apps?.ToList() ?? await _winget.GetInstalledAppsAsync();
            var entries = list.Select(a => new ExportEntry
            {
                Id = a.Id,
                Name = a.Name,
                Version = a.Version
            }).ToList();

            var json = JsonSerializer.Serialize(entries, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<List<AppItem>> ImportAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            var entries = JsonSerializer.Deserialize<List<ExportEntry>>(json);
            if (entries == null) return new List<AppItem>();

            return entries.Select(e => new AppItem
            {
                Id = e.Id,
                Name = string.IsNullOrWhiteSpace(e.Name) ? e.Id : e.Name,
                Version = e.Version
            }).ToList();
        }
    }
}
