using System;
using System.Diagnostics;
using System.IO;

namespace SetupHub180Hz.Services
{
    public class StorageService
    {
        public static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "180HzSetupHub");

        public static string LogsFolder => Path.Combine(AppDataFolder, "logs");
        public static string DownloadsFolder => Path.Combine(AppDataFolder, "downloads");
        public static string ConfigFolder => Path.Combine(AppDataFolder, "config");

        public StorageService()
        {
            Directory.CreateDirectory(AppDataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(DownloadsFolder);
            Directory.CreateDirectory(ConfigFolder);
        }

        public void OpenInExplorer()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppDataFolder,
                UseShellExecute = true
            });
        }

        public long GetFolderSizeBytes()
        {
            if (!Directory.Exists(AppDataFolder)) return 0;
            long size = 0;
            foreach (var file in SafeEnumerateFiles(AppDataFolder))
            {
                try { size += new FileInfo(file).Length; }
                catch { /* file may be locked between enumeration and read — skip it */ }
            }
            return size;
        }

        private static string[] SafeEnumerateFiles(string path)
        {
            try { return Directory.GetFiles(path, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }
    }
}
