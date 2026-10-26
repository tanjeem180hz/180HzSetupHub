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

        private string LogFilePath => Path.Combine(StorageService.LogsFolder, "activity.json");
        private const int MaxEntries = 500;

        private ActivityLogger()
        {
            Directory.CreateDirectory(StorageService.LogsFolder);
            Load();

            StorageService.AppDataFolderChanged += (_, _) =>
            {
                Directory.CreateDirectory(StorageService.LogsFolder);
                Load();
            };
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

        public void Load()
        {
            try
            {
                var path = LogFilePath;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var items = JsonSerializer.Deserialize<ActivityLogEntry[]>(json);
                if (items == null) return;

                void Populate()
                {
                    Entries.Clear();
                    foreach (var i in items) Entries.Add(i);
                }

                if (Application.Current?.Dispatcher.CheckAccess() == false)
                    Application.Current.Dispatcher.Invoke(Populate);
                else
                    Populate();
            }
            catch
            {
                // Corrupt or missing log file — start with an empty feed rather than crash.
            }
        }

        public void Clear()
        {
            void DoClear()
            {
                Entries.Clear();
                try
                {
                    var path = LogFilePath;
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                }
            }

            if (Application.Current?.Dispatcher.CheckAccess() == false)
                Application.Current.Dispatcher.Invoke(DoClear);
            else
                DoClear();
        }

        private void Save()
        {
            try
            {
                var path = LogFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Best-effort persistence — a failed write shouldn't interrupt the user's action.
            }
        }
    }
}