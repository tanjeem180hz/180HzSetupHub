using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public record LeftoverDeleteProgress(int Current, int Total, LeftoverItem Item, bool Success);

    public class DeepUninstallService
    {
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "for", "and", "app", "inc", "corp", "ltd", "llc", "setup", "installer"
        };

        public List<string> BuildSearchTokens(AppItem app)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var cleanName = app.Name?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(cleanName))
            {
                // Full original name is the highest-confidence token
                tokens.Add(cleanName);

                // Split on whitespace and punctuation
                var parts = Regex.Split(cleanName, @"[\s\-_,.()]+");
                foreach (var part in parts)
                {
                    var token = part.Trim();
                    if (token.Length < 3) continue;
                    if (StopWords.Contains(token)) continue;
                    if (Regex.IsMatch(token, @"^\d+(\.\d+)*$")) continue; // Drop pure version numbers

                    tokens.Add(token);
                }
            }

            return tokens.ToList();
        }

        public async Task<List<LeftoverItem>> ScanAsync(AppItem app)
        {
            var tokens = BuildSearchTokens(app);
            if (tokens.Count == 0) return new List<LeftoverItem>();

            var registryTask = Task.Run(() => ScanRegistry(app, tokens));
            var filesystemTask = Task.Run(() => ScanFilesystem(app, tokens));

            await Task.WhenAll(registryTask, filesystemTask);

            var combined = new List<LeftoverItem>();
            combined.AddRange(registryTask.Result);
            combined.AddRange(filesystemTask.Result);

            // Deduplicate by path
            var unique = combined
                .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(i => string.Equals(i.MatchedOn, app.Name, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(i => i.SizeBytes ?? 0)
                .ToList();

            return unique;
        }

        private List<LeftoverItem> ScanRegistry(AppItem app, List<string> tokens)
        {
            var results = new List<LeftoverItem>();

            var targets = new (RegistryHive Hive, RegistryView View, string SubKeyPath, string DisplayPrefix)[]
            {
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software", @"HKEY_CURRENT_USER\Software"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software", @"HKEY_LOCAL_MACHINE\Software"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software", @"HKEY_LOCAL_MACHINE\Software\WOW6432Node"),
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var target in targets)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
                    using var subKey = baseKey.OpenSubKey(target.SubKeyPath, false);
                    if (subKey == null) continue;

                    var subKeyNames = subKey.GetSubKeyNames();
                    foreach (var name in subKeyNames)
                    {
                        var matchedToken = tokens.FirstOrDefault(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
                        if (matchedToken != null)
                        {
                            var fullPath = $@"{target.DisplayPrefix}\{name}";
                            results.Add(new LeftoverItem(LeftoverType.RegistryKey, fullPath, null, matchedToken));
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        private List<LeftoverItem> ScanFilesystem(AppItem app, List<string> tokens)
        {
            var results = new List<LeftoverItem>();

            var candidateRoots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };

            var validRoots = candidateRoots
                .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var root in validRoots)
            {
                try
                {
                    var subDirs = Directory.GetDirectories(root);
                    foreach (var dir in subDirs)
                    {
                        var dirName = Path.GetFileName(dir);
                        if (string.IsNullOrWhiteSpace(dirName)) continue;

                        var matchedToken = tokens.FirstOrDefault(t => dirName.Contains(t, StringComparison.OrdinalIgnoreCase));
                        if (matchedToken != null)
                        {
                            long size = CalculateDirectorySizeSafe(dir);
                            results.Add(new LeftoverItem(LeftoverType.Folder, dir, size, matchedToken));
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        private long CalculateDirectorySizeSafe(string dirPath)
        {
            long total = 0;
            try
            {
                var files = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        total += info.Length;
                    }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        public async Task<(int deleted, int failed)> DeleteAsync(
            IEnumerable<LeftoverItem> items,
            IProgress<LeftoverDeleteProgress>? progress = null)
        {
            return await Task.Run(() =>
            {
                var itemList = items.ToList();
                int total = itemList.Count;
                int deleted = 0;
                int failed = 0;

                for (int i = 0; i < total; i++)
                {
                    var item = itemList[i];
                    bool ok = false;
                    try
                    {
                        if (item.Type == LeftoverType.RegistryKey)
                        {
                            DeleteRegistryKey(item.Path);
                            deleted++;
                            ok = true;
                        }
                        else if (item.Type == LeftoverType.Folder)
                        {
                            if (Directory.Exists(item.Path))
                            {
                                Directory.Delete(item.Path, recursive: true);
                            }
                            deleted++;
                            ok = true;
                        }
                        else if (item.Type == LeftoverType.File)
                        {
                            if (File.Exists(item.Path))
                            {
                                File.Delete(item.Path);
                            }
                            deleted++;
                            ok = true;
                        }
                    }
                    catch
                    {
                        failed++;
                        ok = false;
                    }

                    progress?.Report(new LeftoverDeleteProgress(i + 1, total, item, ok));

                    // Smooth visual feedback pacing (35ms per item)
                    System.Threading.Thread.Sleep(35);
                }

                ActivityLogger.Instance.Log($"Deep clean: removed {deleted}, skipped {failed}.", ActivityType.Info);
                return (deleted, failed);
            });
        }

        private void DeleteRegistryKey(string fullPath)
        {
            if (fullPath.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            {
                var subPath = fullPath.Substring(@"HKEY_CURRENT_USER\".Length);
                Registry.CurrentUser.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            }
            else if (fullPath.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            {
                var subPath = fullPath.Substring(@"HKEY_LOCAL_MACHINE\".Length);

                if (subPath.StartsWith(@"Software\WOW6432Node\", StringComparison.OrdinalIgnoreCase))
                {
                    var wowPath = subPath.Substring(@"Software\WOW6432Node\".Length);
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                    using var swKey = baseKey.OpenSubKey("Software", true);
                    swKey?.DeleteSubKeyTree(wowPath, throwOnMissingSubKey: false);
                }
                else
                {
                    using var base64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    base64.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);

                    using var base32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                    base32.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
                }
            }
        }
    }
}
