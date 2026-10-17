using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        public static long GetRecycleBinSizeBytes()
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                int res = SHQueryRecycleBin(null, ref info);
                return res == 0 ? Math.Max(0, info.i64Size) : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static long EmptyRecycleBin()
        {
            long sizeBefore = GetRecycleBinSizeBytes();
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            }
            catch { }
            return sizeBefore;
        }

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
                    IsRamTarget = t.IsRamTarget,
                    IsRecycleBinTarget = t.IsRecycleBinTarget,
                    IsSelected = t.IsSelected
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
                new()
                {
                    Name = "Delivery Optimization & BITS Cache",
                    Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\SoftwareDistribution\DeliveryOptimization"),
                    AdditionalPaths = new List<string>
                    {
                        Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Network\Downloader")
                    }
                },
                new()
                {
                    Name = "DirectX & GPU Shader Cache",
                    Path = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\D3DSCache"),
                    AdditionalPaths = new List<string>
                    {
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\NVIDIA\DXCache"),
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\NVIDIA\GLCache"),
                        Environment.ExpandEnvironmentVariables(@"%APPDATA%\NVIDIA\ComputeCache"),
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\AMD\DxCache"),
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\AMD\GLCache"),
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\AMD\OglCache"),
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Intel\ShaderCache")
                    }
                },
                new()
                {
                    Name = "Crash Dumps & Error Reports",
                    Path = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\CrashDumps"),
                    AdditionalPaths = new List<string>
                    {
                        Environment.ExpandEnvironmentVariables(@"%WINDIR%\Minidump"),
                        Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"),
                        Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"),
                        Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows\WER\Temp"),
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\WER")
                    }
                },
                new()
                {
                    Name = "Thumbnail & Icon Cache",
                    Path = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer")
                },
                new()
                {
                    Name = "Recycle Bin (Drive C:)",
                    Path = "Windows Recycle Bin (C:)",
                    IsRecycleBinTarget = true
                },
                new() { Name = "Prefetch Cache", Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Prefetch") },
                new() { Name = "Recent Items", Path = Environment.GetFolderPath(Environment.SpecialFolder.Recent) },

                // C: Drive System Cleanup target
                new()
                {
                    Name = "Windows System Logs & Traces",
                    Path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Logs"),
                    AdditionalPaths = new List<string>
                    {
                        Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\LogFiles"),
                        Environment.ExpandEnvironmentVariables(@"%WINDIR%\Downloaded Program Files"),
                        Environment.ExpandEnvironmentVariables(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Temp"),
                        Environment.ExpandEnvironmentVariables(@"%WINDIR%\ServiceProfiles\LocalService\AppData\Local\Temp"),
                    }
                }
            };

            // Scan directory sizes in parallel using all available cores
            Parallel.ForEach(targets, t =>
            {
                if (t.IsRamTarget) return;

                if (t.IsRecycleBinTarget)
                {
                    t.SizeBytes = GetRecycleBinSizeBytes();
                    return;
                }

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

                if (target.IsRecycleBinTarget)
                {
                    long rbFreed = EmptyRecycleBin();
                    onFile?.Invoke("Emptied Windows Recycle Bin on Drive C:");
                    return rbFreed;
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

        public static async Task<bool> RunDismComponentCleanupAsync(Action<string>? onProgress = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    onProgress?.Invoke("Running Windows Component Store (WinSxS) Cleanup...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/online /Cleanup-Image /StartComponentCleanup /ResetBase",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) return false;

                    proc.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            onProgress?.Invoke(e.Data.Trim());
                    };
                    proc.BeginOutputReadLine();

                    bool finished = proc.WaitForExit(90000); // 90-sec safeguard timeout
                    if (!finished)
                    {
                        try { proc.Kill(); } catch { }
                        return false;
                    }
                    return proc.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    onProgress?.Invoke($"DISM Notice: {ex.Message}");
                    return false;
                }
            });
        }

        public static async Task RunCleanMgrAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cleanmgr.exe",
                        Arguments = "/d C: /VERYLOWDISK",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    proc?.WaitForExit(30000);
                }
                catch { }
            });
        }

        public static long CleanBrowserCaches(Action<string>? onProgress = null)
        {
            long freed = 0;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var browserCachePaths = new[]
            {
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
                Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Code Cache"),
                Path.Combine(appData, @"discord\Cache"),
                Path.Combine(appData, @"discord\Code Cache"),
            };

            foreach (var p in browserCachePaths)
            {
                if (Directory.Exists(p))
                {
                    try
                    {
                        freed += CleanDirectory(p, onProgress);
                    }
                    catch { }
                }
            }
            return freed;
        }

        public async Task<long> DeepCleanCDriveAsync(Action<string>? onProgress = null, bool includeDism = true)
        {
            long freedDisk = 0;
            onProgress?.Invoke("Scanning Drive C: system & user temp files...");

            var targets = await ScanAsync(force: true);
            var diskTargets = targets.Where(t => !t.IsRamTarget).ToList();

            foreach (var t in diskTargets)
            {
                onProgress?.Invoke($"Purging {t.Name}...");
                freedDisk += await CleanAsync(t, onProgress, rescan: false);
            }

            // Safe browser caches on C:
            onProgress?.Invoke("Cleaning application and browser temporary caches...");
            freedDisk += CleanBrowserCaches(onProgress);

            // Empty Recycle Bin
            freedDisk += EmptyRecycleBin();

            // Run CleanMgr
            onProgress?.Invoke("Running Windows LowDisk cleanmgr purge...");
            await RunCleanMgrAsync();

            // Run DISM Component Cleanup if requested
            if (includeDism)
            {
                onProgress?.Invoke("Running Windows Component Store (WinSxS) Deep Cleanup...");
                await RunDismComponentCleanupAsync(onProgress);
            }

            _ = ScanAsync(force: true);
            return freedDisk;
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
