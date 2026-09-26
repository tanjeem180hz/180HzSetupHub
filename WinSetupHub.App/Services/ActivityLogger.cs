using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    /// <summary>
    /// App-wide activity feed. Any page can call ActivityLogger.Instance.Log(...)
    /// and the Activity page will update live via the bound ObservableCollection.
    /// </summary>
    public class ActivityLogger
    {
        private static readonly Lazy<ActivityLogger> _instance = new(() => new ActivityLogger());
        public static ActivityLogger Instance => _instance.Value;

        public ObservableCollection<ActivityLogEntry> Entries { get; } = new();

        private readonly string _logFile;
        private const int MaxEntries = 500;

        private ActivityLogger()
        {
            Directory.CreateDirectory(StorageService.LogsFolder);
            _logFile = Path.Combine(StorageService.LogsFolder, "activity.json");
            Load();
        }

        public void Log(string message, ActivityType type = ActivityType.Info)
        {
            var entry = new ActivityLogEntry { Message = message, Type = type, Timestamp = DateTime.Now };

            void Add()
            {
                Entries.Insert(0, entry);
                while (Entries.Count > MaxEntries)
                    Entries.RemoveAt(Entries.Count - 1);
                Save();
            }

            if (Application.Current?.Dispatcher.CheckAccess() == false)
                Application.Current.Dispatcher.Invoke(Add);
            else
                Add();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_logFile)) return;
                var json = File.ReadAllText(_logFile);
                var items = JsonSerializer.Deserialize<ActivityLogEntry[]>(json);
                if (items == null) return;
                foreach (var i in items) Entries.Add(i);
            }
            catch
            {
                // Corrupt or missing log file — start with an empty feed rather than crash.
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_logFile, json);
            }
            catch
            {
                // Best-effort persistence — a failed write shouldn't interrupt the user's action.
            }
        }
    }
}
