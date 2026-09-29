using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class CleanupService
    {
        public Task<List<CleanupTarget>> ScanAsync()
        {
            return Task.Run(() =>
            {
                var targets = new List<CleanupTarget>
                {
                    new() { Name = "User Temp Files", Path = Path.GetTempPath() },
                    new() { Name = "Windows Temp", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Temp") },
                    new() { Name = "Windows Update Cache", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\SoftwareDistribution\Download") },
                    new() { Name = "Prefetch Cache", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Prefetch") },
                    new() { Name = "Recent Items", Path = Environment.GetFolderPath(Environment.SpecialFolder.Recent) },
                };

                foreach (var t in targets)
                    t.SizeBytes = GetDirectorySize(t.Path);

                return targets;
            });
        }

        public Task<long> CleanAsync(CleanupTarget target, Action<string>? onFile = null)
        {
            return Task.Run(() =>
            {
                long freed = 0;
                if (!Directory.Exists(target.Path)) return freed;

                foreach (var file in SafeEnumerateFiles(target.Path))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        long size = info.Length;
                        info.Delete();
                        freed += size;
                        onFile?.Invoke(file);
                    }
                    catch
                    {
                        // File is in use or access is denied — skip it and keep sweeping
                        // the rest, rather than aborting the whole cleanup.
                    }
                }

                // Remove now-empty subfolders, deepest first, best-effort.
                foreach (var dir in SafeEnumerateDirectories(target.Path).OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch { /* not empty or locked — leave it */ }
                }

                return freed;
            });
        }

        public async Task<long> CleanAllAsync(IEnumerable<CleanupTarget> targets, Action<string>? onFile = null)
        {
            long total = 0;
            foreach (var target in targets)
                total += await CleanAsync(target, onFile);
            return total;
        }

        private static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long size = 0;
            foreach (var file in SafeEnumerateFiles(path))
            {
                try { size += new FileInfo(file).Length; }
                catch { /* skip locked/inaccessible files during size scan */ }
            }
            return size;
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path)
        {
            try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories); }
            catch { return Enumerable.Empty<string>(); }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories); }
            catch { return Enumerable.Empty<string>(); }
        }
    }
}
