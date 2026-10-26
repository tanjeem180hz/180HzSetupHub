using System;
using System.IO;
using System.Text.Json;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private string SettingsFilePath => Path.Combine(StorageService.ConfigFolder, "settings.json");
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public AppSettings Current { get; private set; }

        private SettingsService()
        {
            Current = Load();
            StorageService.AppDataFolderChanged += (_, _) =>
            {
                Current = Load();
            };
        }

        public void Reload()
        {
            Current = Load();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(StorageService.ConfigFolder);
                var json = JsonSerializer.Serialize(Current, _jsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to save settings: {ex.Message}", ActivityType.Warning);
            }
        }

        private AppSettings Load()
        {
            try
            {
                var path = SettingsFilePath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to load settings (using defaults): {ex.Message}", ActivityType.Warning);
            }

            return new AppSettings();
        }
    }
}