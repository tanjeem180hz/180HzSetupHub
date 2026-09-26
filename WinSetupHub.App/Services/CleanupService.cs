using WinSetupHub.App.Models;

namespace WinSetupHub.App.Services;

public sealed class CleanupService
{
    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".rtf",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mkv", ".mov", ".avi",
        ".mp3", ".wav", ".flac", ".psd", ".ai", ".prproj", ".aep", ".blend",
        ".zip", ".rar", ".7z", ".tar", ".gz", ".cs", ".js", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".cpp", ".c", ".h", ".sql", ".db", ".sqlite", ".json", ".xml"
    };

    public IReadOnlyList<CleanupTarget> BuildTargets()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return
        [
            new CleanupTarget
            {
                Id = "user-temp",
                Name = "User Temp",
                Description = "Temporary files created by apps in your user profile.",
                RootPath = Path.GetTempPath(),
                MinimumAge = TimeSpan.FromHours(24)
            },
            new CleanupTarget
            {
                Id = "windows-temp",
                Name = "Windows Temp",
                Description = "System temporary files Windows allows this app to remove.",
                RootPath = Path.Combine(windows, "Temp"),
                MinimumAge = TimeSpan.FromHours(24)
            },
            new CleanupTarget
            {
                Id = "crash-dumps",
                Name = "Crash Dumps",
                Description = "Local crash dump files left after app failures.",
                RootPath = Path.Combine(localAppData, "CrashDumps"),
                FilePatterns = ["*.dmp", "*.mdmp", "*.hdmp"],
                MinimumAge = TimeSpan.FromDays(7),
                IsSelectedByDefault = false
            },
            new CleanupTarget
            {
                Id = "directx-cache",
                Name = "DirectX Shader Cache",
                Description = "Graphics shader cache that Windows and games can rebuild.",
                RootPath = Path.Combine(localAppData, "D3DSCache"),
                MinimumAge = TimeSpan.FromHours(12)
            },
            new CleanupTarget
            {
                Id = "nvidia-dx-cache",
                Name = "NVIDIA DX Cache",
                Description = "NVIDIA shader cache rebuilt automatically by the driver.",
                RootPath = Path.Combine(localAppData, "NVIDIA", "DXCache"),
                MinimumAge = TimeSpan.FromHours(12)
            },
            new CleanupTarget
            {
                Id = "nvidia-gl-cache",
                Name = "NVIDIA GL Cache",
                Description = "NVIDIA OpenGL/Vulkan cache rebuilt automatically by the driver.",
                RootPath = Path.Combine(localAppData, "NVIDIA", "GLCache"),
                MinimumAge = TimeSpan.FromHours(12)
            },
            new CleanupTarget
            {
                Id = "amd-dx-cache",
                Name = "AMD Shader Cache",
                Description = "AMD graphics cache rebuilt automatically by the driver.",
                RootPath = Path.Combine(localAppData, "AMD", "DxCache"),
                MinimumAge = TimeSpan.FromHours(12)
            },
            new CleanupTarget
            {
                Id = "inet-cache",
                Name = "Windows Web Cache",
                Description = "Windows internet cache files, not browser passwords or profiles.",
                RootPath = Path.Combine(localAppData, "Microsoft", "Windows", "INetCache"),
                MinimumAge = TimeSpan.FromHours(24)
            },
            new CleanupTarget
            {
                Id = "thumbnail-cache",
                Name = "Thumbnail Cache",
                Description = "Explorer thumbnail and icon caches that can be rebuilt.",
                RootPath = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                FilePatterns = ["thumbcache_*.db", "iconcache_*.db"],
                MinimumAge = TimeSpan.FromHours(12)
            },
            new CleanupTarget
            {
                Id = "delivery-cache",
                Name = "Delivery Optimization",
                Description = "Windows Update delivery cache; locked files are skipped.",
                RootPath = Path.Combine(programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
                MinimumAge = TimeSpan.FromHours(24),
                IsSelectedByDefault = false
            }
        ];
    }

    public async Task<CleanupScanResult> ScanAsync(CleanupTarget target, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            long size = 0;
            var files = 0;
            var skipped = 0;

            var targetFiles = EnumerateTargetFiles(target, out var enumerationSkipped, cancellationToken);
            skipped += enumerationSkipped;

            foreach (var file in targetFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (!IsOldEnough(info, target.MinimumAge))
                    {
                        continue;
                    }

                    size += info.Length;
                    files++;
                }
                catch
                {
                    skipped++;
                }
            }

            return new CleanupScanResult(target.Id, size, files, skipped);
        }, cancellationToken);
    }

    public async Task<CleanupRunResult> CleanAsync(
        CleanupTarget target,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            long freed = 0;
            var deleted = 0;
            var skipped = 0;

            var targetFiles = EnumerateTargetFiles(target, out var enumerationSkipped, cancellationToken);
            skipped += enumerationSkipped;

            foreach (var file in targetFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(file);
                    if (!IsOldEnough(info, target.MinimumAge))
                    {
                        continue;
                    }

                    var size = info.Length;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    freed += size;
                    deleted++;
                }
                catch
                {
                    skipped++;
                }
            }

            RemoveEmptyDirectories(target.RootPath, ref skipped, cancellationToken);
            onOutput?.Invoke($"{target.Name}: deleted {deleted} file(s), freed {SizeFormatter.Format(freed)}, skipped {skipped}.");
            return new CleanupRunResult(target.Id, freed, deleted, skipped);
        }, cancellationToken);
    }

    private static IReadOnlyList<string> EnumerateTargetFiles(
        CleanupTarget target,
        out int skipped,
        CancellationToken cancellationToken)
    {
        skipped = 0;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!IsKnownCleanupTarget(target) || !Directory.Exists(target.RootPath) || !IsSafeCleanupRoot(target.RootPath))
        {
            return result;
        }

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(target.RootPath));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (!IsSafeCleanupDirectory(target.RootPath, directory))
            {
                skipped++;
                continue;
            }

            foreach (var pattern in target.FilePatterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    skipped++;
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!seen.Add(file))
                    {
                        continue;
                    }

                    if (IsSafeCleanupFile(target, file))
                    {
                        result.Add(file);
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                skipped++;
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSafeCleanupDirectory(target.RootPath, childDirectory))
                {
                    pending.Push(childDirectory);
                }
                else
                {
                    skipped++;
                }
            }
        }

        return result;
    }

    private static void RemoveEmptyDirectories(string root, ref int skipped, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || !IsSafeCleanupRoot(root))
        {
            return;
        }

        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                skipped++;
                continue;
            }

            foreach (var child in children)
            {
                if (!IsSafeCleanupDirectory(root, child))
                {
                    skipped++;
                    continue;
                }

                directories.Add(child);
                pending.Push(child);
            }
        }

        directories = directories.OrderByDescending(path => path.Length).ToList();

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (IsSafeCleanupDirectory(root, directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static bool IsOldEnough(FileInfo info, TimeSpan minimumAge)
    {
        if (minimumAge <= TimeSpan.Zero)
        {
            return true;
        }

        return DateTime.Now - info.LastWriteTime > minimumAge;
    }

    private static bool IsSafeCleanupRoot(string root)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return !string.IsNullOrWhiteSpace(fullRoot)
                && !fullRoot.Equals(windows, StringComparison.OrdinalIgnoreCase)
                && !fullRoot.Equals(profile, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetPathRoot(fullRoot), fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownCleanupTarget(CleanupTarget target)
    {
        var knownTarget = new CleanupService()
            .BuildTargets()
            .FirstOrDefault(candidate => candidate.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase));

        if (knownTarget is null)
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(knownTarget.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(target.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeCleanupDirectory(string root, string directory)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullDirectory.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                && !fullDirectory.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var attributes = File.GetAttributes(fullDirectory);
            return (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeCleanupFile(CleanupTarget target, string file)
    {
        try
        {
            var fullRoot = Path.GetFullPath(target.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullFile = Path.GetFullPath(file);
            if (!fullFile.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var attributes = File.GetAttributes(fullFile);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.System) != 0)
            {
                return false;
            }

            var extension = Path.GetExtension(fullFile);
            return !ProtectedExtensions.Contains(extension) || IsExplicitCleanupExtensionAllowed(target, extension);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExplicitCleanupExtensionAllowed(CleanupTarget target, string extension)
    {
        return target.Id.Equals("thumbnail-cache", StringComparison.OrdinalIgnoreCase)
                && extension.Equals(".db", StringComparison.OrdinalIgnoreCase)
            || target.Id.Equals("crash-dumps", StringComparison.OrdinalIgnoreCase)
                && (extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".mdmp", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".hdmp", StringComparison.OrdinalIgnoreCase));
    }
}
