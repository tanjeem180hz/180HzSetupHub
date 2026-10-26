using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class StorageService
    {
        private static readonly object _syncLock = new();
        private static string? _customAppDataFolder;

        public static event EventHandler<string>? AppDataFolderChanged;

        public static string DefaultAppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "180HzSetupHub");

        public static string AppDataFolder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_customAppDataFolder))
                    return _customAppDataFolder;

                lock (_syncLock)
                {
                    if (!string.IsNullOrWhiteSpace(_customAppDataFolder))
                        return _customAppDataFolder;

                    _customAppDataFolder = ResolveInitialAppDataFolder();
                    return _customAppDataFolder;
                }
            }
        }

        public static bool IsCustomLocation =>
            !string.Equals(AppDataFolder, DefaultAppDataFolder, StringComparison.OrdinalIgnoreCase);

        public static string LogsFolder => Path.Combine(AppDataFolder, "logs");
        public static string DownloadsFolder => Path.Combine(AppDataFolder, "downloads");
        public static string ConfigFolder => Path.Combine(AppDataFolder, "config");

        public StorageService()
        {
            EnsureStructure(AppDataFolder);
        }

        public static void EnsureStructure(string rootPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rootPath)) return;
                Directory.CreateDirectory(rootPath);
                Directory.CreateDirectory(Path.Combine(rootPath, "logs"));
                Directory.CreateDirectory(Path.Combine(rootPath, "downloads"));
                Directory.CreateDirectory(Path.Combine(rootPath, "config"));
            }
            catch { }
        }

        private static string ResolveInitialAppDataFolder()
        {
            try
            {
                // 1. Check Windows Registry HKCU\Software\180HzSetupHub -> CustomDataDirectory
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\180HzSetupHub");
                if (key?.GetValue("CustomDataDirectory") is string regPath && !string.IsNullOrWhiteSpace(regPath))
                {
                    regPath = regPath.Trim();
                    if (IsValidDirectoryPath(regPath))
                    {
                        EnsureStructure(regPath);
                        return regPath;
                    }
                }
            }
            catch { }

            try
            {
                // 2. Check storage_path.txt pointer file in DefaultAppDataFolder
                var defaultPath = DefaultAppDataFolder;
                var pointerFile = Path.Combine(defaultPath, "storage_path.txt");
                if (File.Exists(pointerFile))
                {
                    var fileContent = File.ReadAllText(pointerFile).Trim();
                    if (!string.IsNullOrWhiteSpace(fileContent) && IsValidDirectoryPath(fileContent))
                    {
                        EnsureStructure(fileContent);
                        return fileContent;
                    }
                }
            }
            catch { }

            // 3. Fallback to standard default
            var def = DefaultAppDataFolder;
            EnsureStructure(def);
            return def;
        }

        public static bool IsValidDirectoryPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                if (!Path.IsPathRooted(path)) return false;

                var root = Path.GetPathRoot(path);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return false;

                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var sysDir = Environment.SystemDirectory;
                if (!string.IsNullOrWhiteSpace(winDir) && path.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.IsNullOrWhiteSpace(sysDir) && path.StartsWith(sysDir, StringComparison.OrdinalIgnoreCase)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (bool success, string message) SetCustomAppDataFolder(string newPath, bool migrateExistingData = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newPath))
                    return (false, "Please select a valid folder path.");

                newPath = Path.GetFullPath(newPath.Trim());

                if (!IsValidDirectoryPath(newPath))
                    return (false, "The selected path is invalid, restricted, or on an unreachable drive.");

                if (string.Equals(newPath, AppDataFolder, StringComparison.OrdinalIgnoreCase))
                    return (true, "Application data is already configured at this location.");

                // Validate write permissions
                Directory.CreateDirectory(newPath);
                string testFile = Path.Combine(newPath, $".access_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "180Hz Setup Hub Permission Test");
                if (File.Exists(testFile)) File.Delete(testFile);

                string oldPath = AppDataFolder;

                if (migrateExistingData && Directory.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    MigrateDirectorySafely(oldPath, newPath);
                }

                EnsureStructure(newPath);

                // Persist to registry
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\180HzSetupHub", true);
                    key?.SetValue("CustomDataDirectory", newPath, RegistryValueKind.String);
                }
                catch { }

                // Persist pointer files
                try
                {
                    Directory.CreateDirectory(DefaultAppDataFolder);
                    File.WriteAllText(Path.Combine(DefaultAppDataFolder, "storage_path.txt"), newPath);
                    File.WriteAllText(Path.Combine(newPath, "storage_path.txt"), newPath);
                }
                catch { }

                lock (_syncLock)
                {
                    _customAppDataFolder = newPath;
                }

                AppDataFolderChanged?.Invoke(null, newPath);

                ActivityLogger.Instance.Log($"Application data directory customized to: {newPath}", ActivityType.Success);
                return (true, $"Application data location successfully updated to:\n{newPath}");
            }
            catch (Exception ex)
            {
                return (false, $"Could not change data directory: {ex.Message}");
            }
        }

        public static (bool success, string message) ResetToDefaultDirectory(bool migrateExistingData = true)
        {
            try
            {
                string defaultPath = DefaultAppDataFolder;
                string oldPath = AppDataFolder;

                if (string.Equals(oldPath, defaultPath, StringComparison.OrdinalIgnoreCase))
                    return (true, "Already using the default directory location.");

                if (migrateExistingData && Directory.Exists(oldPath))
                {
                    MigrateDirectorySafely(oldPath, defaultPath);
                }

                EnsureStructure(defaultPath);

                // Clean registry
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\180HzSetupHub", true);
                    key?.DeleteValue("CustomDataDirectory", false);
                }
                catch { }

                // Clean pointer
                try
                {
                    var ptr = Path.Combine(defaultPath, "storage_path.txt");
                    if (File.Exists(ptr)) File.Delete(ptr);
                }
                catch { }

                lock (_syncLock)
                {
                    _customAppDataFolder = defaultPath;
                }

                AppDataFolderChanged?.Invoke(null, defaultPath);

                ActivityLogger.Instance.Log("Application data directory restored to default location.", ActivityType.Info);
                return (true, "Application data location restored to default (%LocalAppData%\\180HzSetupHub).");
            }
            catch (Exception ex)
            {
                return (false, $"Could not reset directory: {ex.Message}");
            }
        }

        public static void MigrateDirectorySafely(string sourceDir, string destinationDir)
        {
            try
            {
                if (!Directory.Exists(sourceDir)) return;
                Directory.CreateDirectory(destinationDir);

                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var destFile = Path.Combine(destinationDir, fileName);
                        File.Copy(file, destFile, overwrite: true);
                    }
                    catch { }
                }

                foreach (var subDir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var dirName = Path.GetFileName(subDir);
                        if (string.Equals(Path.GetFullPath(subDir), Path.GetFullPath(destinationDir), StringComparison.OrdinalIgnoreCase))
                            continue;

                        var destSubDir = Path.Combine(destinationDir, dirName);
                        MigrateDirectorySafely(subDir, destSubDir);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void OpenInExplorer()
        {
            try
            {
                EnsureStructure(AppDataFolder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppDataFolder,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public long GetFolderSizeBytes()
        {
            if (!Directory.Exists(AppDataFolder)) return 0;
            long size = 0;
            foreach (var file in SafeEnumerateFiles(AppDataFolder))
            {
                try { size += new FileInfo(file).Length; }
                catch { }
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