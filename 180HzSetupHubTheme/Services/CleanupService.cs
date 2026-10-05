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
        private static readonly Lazy<CleanupService> _instance = new(() => new CleanupService());
        public static CleanupService Instance => _instance.Value;

        private readonly object _cacheLock = new();
        private List<CleanupTarget>? _cachedTargets;
        private Task<List<CleanupTarget>>? _activeScanTask;

        public event Action<List<CleanupTarget>>? ScanCompleted;

        public bool IsScanning
        {
            get
            {
                lock (_cacheLock)
                {
                    return _activeScanTask != null && !_activeScanTask.IsCompleted;
                }
            }
        }

        public List<CleanupTarget>? CachedTargets
        {
            get
            {
                lock (_cacheLock)
                {
                    return _cachedTargets != null ? CloneTargets(_cachedTargets) : null;
                }
            }
        }

        private static List<CleanupTarget> CloneTargets(List<CleanupTarget> source)
        {
            var list = new List<CleanupTarget>(source.Count);
            foreach (var t in source)
            {
                list.Add(new CleanupTarget
                {
                    Name = t.Name,
                    Path = t.Path,
                    AdditionalPaths = t.AdditionalPaths != null ? new List<string>(t.AdditionalPaths) : null,
                    SizeBytes = t.SizeBytes,
                    IsRamTarget = t.IsRamTarget
                });
            }
            return list;
        }

        public void StartBackgroundScan()
        {
            _ = ScanAsync(force: false);
        }

        public Task<List<CleanupTarget>> ScanAsync(bool force = false)
        {
            lock (_cacheLock)
            {
                if (!force && _cachedTargets != null)
                {
                    return Task.FromResult(CloneTargets(_cachedTargets));
                }

                if (_activeScanTask != null && !_activeScanTask.IsCompleted)
                {
                    return _activeScanTask;
                }

                _activeScanTask = Task.Run(() => PerformScan());
                return _activeScanTask;
            }
        }

        private List<CleanupTarget> PerformScan()
        {
            long totalMem = MemoryCleaner.GetTotalMemoryBytes();
            long availMem = MemoryCleaner.GetAvailableMemoryBytes();
            long usedMem = Math.Max(0, totalMem - availMem);
            long estimatedReclaimableRam = usedMem > 0 ? (long)(usedMem * 0.25) : 512L * 1024L * 1024L;

            var targets = new List<CleanupTarget>
            {
                // System RAM Working Set & Standby Cache target
                new()
                {
                    Name = "RAM Cache & Working Sets",
                    Path = "Process Working Sets, Standby Memory & CLR Heap",
                    IsRamTarget = true,
                    SizeBytes = estimatedReclaimableRam
                },
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
                }
            };

            // Scan directory sizes in parallel using all available cores
            Parallel.ForEach(targets, t =>
            {
                if (t.IsRamTarget) return;

                long total = GetDirectorySize(t.Path);
                if (t.AdditionalPaths != null)
                {
                    foreach (var ap in t.AdditionalPaths)
                    {
                        total += GetDirectorySize(ap);
                    }
                }
                t.SizeBytes = total;
            });

            lock (_cacheLock)
            {
                _cachedTargets = targets;
            }

            try
            {
                ScanCompleted?.Invoke(CloneTargets(targets));
            }
            catch { }

            return targets;
        }

        public async Task<long> CleanAsync(CleanupTarget target, Action<string>? onFile = null, bool rescan = true)
        {
            long freed = await Task.Run(() =>
            {
                if (target.IsRamTarget)
                {
                    long ramFreed = MemoryCleaner.CleanRam();
                    onFile?.Invoke("Purged process working sets and RAM cache");
                    return ramFreed;
                }

                long totalFreed = CleanDirectory(target.Path, onFile);

                if (target.AdditionalPaths != null)
                {
                    foreach (var ap in target.AdditionalPaths)
                        totalFreed += CleanDirectory(ap, onFile);
                }

                return totalFreed;
            });

            // Rescan in background to update cache if requested
            if (rescan)
            {
                _ = ScanAsync(force: true);
            }

            return freed;
        }

        public async Task<long> CleanAllAsync(IEnumerable<CleanupTarget> targets, Action<string>? onFile = null)
        {
            long total = 0;
            foreach (var target in targets)
                total += await CleanAsync(target, onFile, rescan: false);

            _ = ScanAsync(force: true);
            return total;
        }

        private static long GetDirectorySize(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
            try
            {
                var di = new DirectoryInfo(path);
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                long size = 0;
                foreach (var fi in di.EnumerateFiles("*", options))
                {
                    try { size += fi.Length; }
                    catch { }
                }
                return size;
            }
            catch
            {
                return 0;
            }
        }

        private static long CleanDirectory(string path, Action<string>? onFile = null)
        {
            long freed = 0;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return freed;

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            try
            {
                var di = new DirectoryInfo(path);

                foreach (var fi in di.EnumerateFiles("*", options))
                {
                    try
                    {
                        long size = fi.Length;
                        fi.Delete();
                        freed += size;
                        onFile?.Invoke(fi.FullName);
                    }
                    catch { }
                }

                foreach (var subDir in di.EnumerateDirectories("*", options).OrderByDescending(d => d.FullName.Length))
                {
                    try
                    {
                        if (!subDir.EnumerateFileSystemInfos().Any())
                            subDir.Delete();
                    }
                    catch { }
                }
            }
            catch { }

            return freed;
        }
    }
}
