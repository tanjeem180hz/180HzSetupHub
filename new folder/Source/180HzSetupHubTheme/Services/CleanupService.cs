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
                long totalMem = MemoryCleaner.GetTotalMemoryBytes();
                long availMem = MemoryCleaner.GetAvailableMemoryBytes();
                long usedMem = Math.Max(0, totalMem - availMem);
                long estimatedReclaimableRam = usedMem > 0 ? (long)(usedMem * 0.25) : 512L * 1024L * 1024L;

                var targets = new List<CleanupTarget>
                {
                    new() { Name = "User Temp Files", Path = Path.GetTempPath() },
                    new() { Name = "Windows Temp", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Temp") },
                    new() { Name = "Windows Update Cache", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\SoftwareDistribution\Download") },
                    new() { Name = "Prefetch Cache", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Prefetch") },
                    new() { Name = "Recent Items", Path = Environment.GetFolderPath(Environment.SpecialFolder.Recent) },

                    // C: Drive System Cleanup target
                    new()
                    {
                        Name = "C: Drive Cleanup",
                        Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Logs"),
                        AdditionalPaths = new List<string>
                        {
                            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\CrashDumps"),
                            Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows\WER"),
                            Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\LogFiles"),
                            Environment.ExpandEnvironmentVariables(@"%WINDIR%\Downloaded Program Files"),
                            Environment.ExpandEnvironmentVariables(@"%WINDIR%\Minidump"),
                            Environment.ExpandEnvironmentVariables(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Temp"),
                            Environment.ExpandEnvironmentVariables(@"%WINDIR%\ServiceProfiles\LocalService\AppData\Local\Temp"),
                        }
                    },

                    // System RAM Working Set Cache target
                    new()
                    {
                        Name = "System Memory (RAM Cache)",
                        Path = "Process Working Sets & RAM Cache",
                        IsRamTarget = true,
                        SizeBytes = estimatedReclaimableRam
                    }
                };

                foreach (var t in targets)
                {
                    if (t.IsRamTarget) continue;

                    long total = GetDirectorySize(t.Path);
                    if (t.AdditionalPaths != null)
                    {
                        foreach (var ap in t.AdditionalPaths)
                            total += GetDirectorySize(ap);
                    }
                    t.SizeBytes = total;
                }

                return targets;
            });
        }

        public Task<long> CleanAsync(CleanupTarget target, Action<string>? onFile = null)
        {
            return Task.Run(() =>
            {
                if (target.IsRamTarget)
                {
                    long ramFreed = MemoryCleaner.CleanRam();
                    onFile?.Invoke("Purged process working sets and RAM cache");
                    return ramFreed;
                }

                long freed = CleanDirectory(target.Path, onFile);

                if (target.AdditionalPaths != null)
                {
                    foreach (var ap in target.AdditionalPaths)
                        freed += CleanDirectory(ap, onFile);
                }

                return freed;
            });
        }

        private static long CleanDirectory(string path, Action<string>? onFile = null)
        {
            long freed = 0;
            if (!Directory.Exists(path)) return freed;

            foreach (var file in SafeEnumerateFiles(path))
            {
                try
                {
                    var info = new FileInfo(file);
                    long size = info.Length;
                    info.Delete();
                    freed += size;
                    onFile?.Invoke(file);
                }
                catch { }
            }

            foreach (var dir in SafeEnumerateDirectories(path).OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { }
            }

            return freed;
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
                catch { }
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
