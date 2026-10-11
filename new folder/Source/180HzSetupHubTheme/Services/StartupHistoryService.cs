using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class StartupHistoryService
    {
        private static readonly Lazy<StartupHistoryService> _instance = new(() => new StartupHistoryService());
        public static StartupHistoryService Instance => _instance.Value;

        private readonly string _historyFilePath;
        private readonly string _statesFilePath;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly object _lock = new();

        private List<StartupHistoryEntry> _historyCache = new();
        private Dictionary<string, bool> _statesCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _isLoaded;

        public event EventHandler? HistoryChanged;

        private StartupHistoryService()
        {
            var baseDir = StorageService.ConfigFolder;
            try { Directory.CreateDirectory(baseDir); } catch { }

            _historyFilePath = Path.Combine(baseDir, "startup_history.json");
            _statesFilePath = Path.Combine(baseDir, "startup_states.json");

            LoadFromDisk();
        }

        private void LoadFromDisk()
        {
            lock (_lock)
            {
                if (_isLoaded) return;

                // 1. Load History
                try
                {
                    if (File.Exists(_historyFilePath))
                    {
                        var json = File.ReadAllText(_historyFilePath);
                        var list = JsonSerializer.Deserialize<List<StartupHistoryEntry>>(json, _jsonOptions);
                        if (list != null)
                        {
                            _historyCache = list.OrderByDescending(h => h.Timestamp).ToList();
                        }
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Failed to read startup history: {ex.Message}", ActivityType.Warning);
                }

                // 2. Load States
                try
                {
                    if (File.Exists(_statesFilePath))
                    {
                        var json = File.ReadAllText(_statesFilePath);
                        var states = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, _jsonOptions);
                        if (states != null)
                        {
                            _statesCache = new Dictionary<string, bool>(states, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Failed to read startup states: {ex.Message}", ActivityType.Warning);
                }

                _isLoaded = true;
            }
        }

        public List<StartupHistoryEntry> GetHistory()
        {
            lock (_lock)
            {
                LoadFromDisk();
                return _historyCache.ToList();
            }
        }

        public StartupHistoryEntry? GetLastHistory(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;

            lock (_lock)
            {
                LoadFromDisk();
                return _historyCache.FirstOrDefault(h =>
                    string.Equals(h.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void RecordAction(string itemName, bool isEnabled, string source, string command, string notes = "")
        {
            if (string.IsNullOrWhiteSpace(itemName)) return;

            lock (_lock)
            {
                LoadFromDisk();

                var entry = new StartupHistoryEntry
                {
                    ItemName = itemName,
                    Action = isEnabled ? "Enabled" : "Disabled",
                    Timestamp = DateTime.Now,
                    Source = source,
                    Command = command,
                    Notes = notes
                };

                _historyCache.Insert(0, entry);

                // Keep max 500 records
                if (_historyCache.Count > 500)
                {
                    _historyCache = _historyCache.Take(500).ToList();
                }

                _statesCache[itemName] = isEnabled;

                SaveHistoryToDisk();
                SaveStatesToDisk();
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SaveKnownStates(IEnumerable<StartupItem> items)
        {
            lock (_lock)
            {
                LoadFromDisk();
                bool changed = false;

                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name)) continue;

                    if (!_statesCache.TryGetValue(item.Name, out var existing) || existing != item.IsEnabled)
                    {
                        _statesCache[item.Name] = item.IsEnabled;
                        changed = true;
                    }
                }

                if (changed)
                {
                    SaveStatesToDisk();
                }
            }
        }

        public Dictionary<string, bool> GetKnownStates()
        {
            lock (_lock)
            {
                LoadFromDisk();
                return new Dictionary<string, bool>(_statesCache, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _historyCache.Clear();
                SaveHistoryToDisk();
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SaveHistoryToDisk()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_historyFilePath)!);
                var json = JsonSerializer.Serialize(_historyCache, _jsonOptions);
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to save startup history: {ex.Message}", ActivityType.Warning);
            }
        }

        private void SaveStatesToDisk()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statesFilePath)!);
                var json = JsonSerializer.Serialize(_statesCache, _jsonOptions);
                File.WriteAllText(_statesFilePath, json);
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to save startup states: {ex.Message}", ActivityType.Warning);
            }
        }
    }
}
