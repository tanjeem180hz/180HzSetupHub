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
        #region Protected Sets (Revo-Grade Blacklists & Safe Guards)

        // Paths that must NEVER be deleted under any circumstance
        private static readonly HashSet<string> ProtectedExactPaths = new(StringComparer.OrdinalIgnoreCase);

        // Directories directly under Program Files, AppData, or ProgramData that are vendor/shared roots
        private static readonly HashSet<string> ProtectedVendorNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "Windows Defender", "WindowsApps", "WindowsPowerShell",
            "Common Files", "Internet Explorer", "dotnet", "Packages", "Temp", "Programs",
            "LocalLow", "VirtualStore", "Google", "Adobe", "Intel", "AMD", "NVIDIA",
            "NVIDIA Corporation", "Apple", "Apple Computer", "Mozilla", "Oracle", "Java",
            "Steam", "Valve", "Epic Games", "Ubisoft", "Origin", "Electronic Arts",
            "Dropbox", "Spotify", "Discord", "GitHub", "Git", "JetBrains", "CanonicalGroupLimited"
        };

        // Top-level registry keys that must NEVER be deleted
        private static readonly HashSet<string> ProtectedRegistryExactKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            @"HKEY_CURRENT_USER\Software",
            @"HKEY_CURRENT_USER\Software\Microsoft",
            @"HKEY_CURRENT_USER\Software\Classes",
            @"HKEY_CURRENT_USER\Software\Policies",
            @"HKEY_CURRENT_USER\Software\Clients",
            @"HKEY_CURRENT_USER\Software\RegisteredApplications",
            @"HKEY_LOCAL_MACHINE\Software",
            @"HKEY_LOCAL_MACHINE\Software\Microsoft",
            @"HKEY_LOCAL_MACHINE\Software\Classes",
            @"HKEY_LOCAL_MACHINE\Software\Policies",
            @"HKEY_LOCAL_MACHINE\Software\Clients",
            @"HKEY_LOCAL_MACHINE\Software\RegisteredApplications",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Classes",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Policies",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Clients",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\RegisteredApplications",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        // Registry vendor keys directly under Software or WOW6432Node that hold multiple applications
        private static readonly HashSet<string> ProtectedTopLevelRegistryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Classes", "Policies", "Clients", "RegisteredApplications",
            "Windows", "Windows NT", "DirectX", ".NETFramework", "Google", "Adobe",
            "Intel", "AMD", "NVIDIA", "NVIDIA Corporation", "Apple Inc.", "Apple Computer",
            "Mozilla", "Oracle", "JavaSoft", "Valve", "Epic Games", "Electronic Arts"
        };

        // Words that must NEVER be isolated as standalone search tokens
        private static readonly HashSet<string> BlacklistedTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft", "windows", "system", "intel", "amd", "nvidia", "google", "apple",
            "adobe", "mozilla", "oracle", "the", "for", "and", "app", "inc", "corp", "ltd",
            "llc", "setup", "installer", "install", "update", "updater", "service", "services",
            "support", "driver", "drivers", "assistant", "manager", "center", "package",
            "packages", "platform", "runtime", "framework", "engine", "sdk", "redistributable",
            "extension", "extensions", "security", "defender", "explorer", "host",
            "client", "server", "config", "data", "user", "program", "programs", "software",
            "digital", "media", "player", "viewer", "editor", "studio", "audio", "video",
            "graphics", "display", "network", "wireless", "bluetooth", "device", "devices",
            "help", "default", "settings", "control", "universal", "component", "components",
            "library", "libraries", "community", "code", "professional", "enterprise", "home",
            "edition", "build", "release", "version", "desktop", "tools", "tool", "common",
            "shared", "files", "file", "core", "x86", "x64", "arm", "arm64", "32-bit", "64-bit",
            "node", "suite", "web", "cloud", "online", "live", "workstation", "personal",
            "free", "open", "source", "redist", "microsoft visual c++", "visual c++", "visual"
        };

        private static readonly List<string> AppDataRoots = new();
        private static readonly List<string> ProgramFilesRoots = new();
        private static readonly List<string> UserSpecialFolders = new();

        static DeepUninstallService()
        {
            try
            {
                // Populate base folders
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(winDir))
                {
                    ProtectedExactPaths.Add(NormalizePath(winDir));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(winDir, "System32")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(winDir, "SysWOW64")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(winDir, "WinSxS")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(winDir, "DriverStore")));
                }

                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(progFiles))
                {
                    ProtectedExactPaths.Add(NormalizePath(progFiles));
                    ProgramFilesRoots.Add(NormalizePath(progFiles));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Common Files")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Windows Defender")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "WindowsApps")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "WindowsPowerShell")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "dotnet")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Microsoft.NET")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Internet Explorer")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Windows Media Player")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Windows Mail")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Windows NT")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFiles, "Windows Photo Viewer")));
                }

                var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(progFilesX86))
                {
                    ProtectedExactPaths.Add(NormalizePath(progFilesX86));
                    ProgramFilesRoots.Add(NormalizePath(progFilesX86));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "Common Files")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "Windows Defender")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "WindowsApps")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "WindowsPowerShell")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "dotnet")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "Microsoft.NET")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "Internet Explorer")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progFilesX86, "Windows Media Player")));
                }

                var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrEmpty(progData))
                {
                    ProtectedExactPaths.Add(NormalizePath(progData));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progData, "Microsoft")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(progData, "Package Cache")));
                }

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    ProtectedExactPaths.Add(NormalizePath(localAppData));
                    AppDataRoots.Add(NormalizePath(localAppData));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(localAppData, "Microsoft")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(localAppData, "Packages")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(localAppData, "Temp")));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(localAppData, "VirtualStore")));
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    ProtectedExactPaths.Add(NormalizePath(appData));
                    AppDataRoots.Add(NormalizePath(appData));
                    ProtectedExactPaths.Add(NormalizePath(Path.Combine(appData, "Microsoft")));
                }

                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    ProtectedExactPaths.Add(NormalizePath(userProfile));
                    var localLow = Path.Combine(userProfile, "AppData", "LocalLow");
                    ProtectedExactPaths.Add(NormalizePath(localLow));
                    AppDataRoots.Add(NormalizePath(localLow));
                }

                // User special folders (Desktop, Documents, Downloads, etc.)
                var specialEnums = new[]
                {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolder.CommonDesktopDirectory,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.CommonDocuments,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.CommonMusic,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.CommonPictures,
                    Environment.SpecialFolder.MyVideos,
                    Environment.SpecialFolder.CommonVideos,
                    Environment.SpecialFolder.StartMenu,
                    Environment.SpecialFolder.CommonStartMenu
                };

                foreach (var s in specialEnums)
                {
                    var p = Environment.GetFolderPath(s);
                    if (!string.IsNullOrEmpty(p))
                    {
                        var norm = NormalizePath(p);
                        ProtectedExactPaths.Add(norm);
                        UserSpecialFolders.Add(norm);
                    }
                }

                // Add user Downloads folder
                if (!string.IsNullOrEmpty(userProfile))
                {
                    var downloads = Path.Combine(userProfile, "Downloads");
                    ProtectedExactPaths.Add(NormalizePath(downloads));
                    UserSpecialFolders.Add(NormalizePath(downloads));
                }
            }
            catch { }
        }

        private static string NormalizePath(string path) =>
            Path.GetFullPath(path).TrimEnd('\\', '/');

        #endregion

        #region Safety Validation (Bulletproof Defense-in-Depth)

        public static bool IsSafePathToDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var full = NormalizePath(path);

                // Minimum path length check (e.g. C:\A\B is at least 6 characters)
                if (full.Length < 7) return false;

                // Drive root protection (e.g. C:\ or C:)
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return false;
                if (string.Equals(full, NormalizePath(root), StringComparison.OrdinalIgnoreCase)) return false;

                // Check exact protected paths
                if (ProtectedExactPaths.Contains(full)) return false;

                // Must NOT be inside Windows directory (C:\Windows, System32, WinSxS, etc.)
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(winDir) && full.StartsWith(NormalizePath(winDir), StringComparison.OrdinalIgnoreCase))
                    return false;

                // Must NOT be directly any user library or profile root
                foreach (var special in UserSpecialFolders)
                {
                    if (string.Equals(full, special, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Must NOT be directly an AppData root
                foreach (var appDataRoot in AppDataRoots)
                {
                    if (string.Equals(full, appDataRoot, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Must NOT be directly a Program Files root
                foreach (var progRoot in ProgramFilesRoots)
                {
                    if (string.Equals(full, progRoot, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Must NOT be a protected vendor root folder directly under Program Files, AppData, or ProgramData
                var dirName = Path.GetFileName(full);
                var parentDir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var normParent = NormalizePath(parentDir);
                    bool parentIsRoot = AppDataRoots.Any(r => string.Equals(r, normParent, StringComparison.OrdinalIgnoreCase)) ||
                                        ProgramFilesRoots.Any(r => string.Equals(r, normParent, StringComparison.OrdinalIgnoreCase)) ||
                                        string.Equals(normParent, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase);

                    if (parentIsRoot && ProtectedVendorNames.Contains(dirName))
                    {
                        return false; // Blocks AppData\Local\Microsoft, Program Files\Google, etc.
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSafeRegistryKeyToDelete(string? fullKeyPath)
        {
            if (string.IsNullOrWhiteSpace(fullKeyPath)) return false;

            try
            {
                var trimmed = fullKeyPath.Trim().TrimEnd('\\', '/');

                // Block hive roots
                if (trimmed.Equals(@"HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_USERS", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_CURRENT_CONFIG", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Exact protected keys
                if (ProtectedRegistryExactKeys.Contains(trimmed)) return false;

                // Must NOT be Software root
                if (trimmed.Equals(@"HKEY_CURRENT_USER\Software", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE\Software", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE\Software\WOW6432Node", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check protected vendor roots directly under Software or WOW6432Node
                var parts = trimmed.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 3 && parts.Length >= 2)
                {
                    var leaf = parts.Last();
                    if (ProtectedTopLevelRegistryNames.Contains(leaf))
                        return false;
                }
                else if (parts.Length == 4 && string.Equals(parts[2], "WOW6432Node", StringComparison.OrdinalIgnoreCase))
                {
                    var leaf = parts.Last();
                    if (ProtectedTopLevelRegistryNames.Contains(leaf))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Token and Signature Extraction (Revo-Grade Precision)

        public record AppSearchSignature(
            string FullName,
            string CleanName,
            string? DistinctProductName,
            string? PublisherPart,
            string? IdProductPart);

        public AppSearchSignature BuildSearchSignature(AppItem app)
        {
            var rawName = app.Name?.Trim() ?? string.Empty;
            var cleanName = CleanAppName(rawName);

            string? distinctProductName = null;
            string? publisherPart = null;
            string? idProductPart = null;

            // Extract vendor prefix if present
            var vendorPrefixes = new[] { "Google ", "Mozilla ", "Microsoft ", "Adobe ", "Intel ", "NVIDIA ", "AMD ", "VideoLAN " };
            foreach (var prefix in vendorPrefixes)
            {
                if (cleanName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var remainder = cleanName.Substring(prefix.Length).Trim();
                    if (remainder.Length >= 3 && !BlacklistedTokens.Contains(remainder))
                    {
                        distinctProductName = remainder;
                        publisherPart = prefix.Trim();
                        break;
                    }
                }
            }

            // Extract from app.Id (e.g. Google.Chrome -> Publisher: Google, Product: Chrome)
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                var idParts = app.Id.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (idParts.Length >= 2)
                {
                    var last = idParts.Last();
                    if (last.Length >= 3 && !BlacklistedTokens.Contains(last))
                    {
                        idProductPart = last;
                        if (publisherPart == null) publisherPart = idParts[0];
                    }
                }
            }

            return new AppSearchSignature(rawName, cleanName, distinctProductName, publisherPart, idProductPart);
        }

        private static string CleanAppName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;

            var cleaned = rawName;
            // Remove architecture tags like (x64), (x86), 64-bit, 32-bit
            cleaned = Regex.Replace(cleaned, @"\s*[\(\[](x64|x86|arm64|32-bit|64-bit)[\)\]]", "", RegexOptions.IgnoreCase);
            // Remove version tags at end like " 23.01", " v1.2"
            cleaned = Regex.Replace(cleaned, @"\s+v?\d+(\.\d+)*\b", "", RegexOptions.IgnoreCase);
            // Remove trailing "Redistributable" or "Setup" if preceded by a name
            cleaned = Regex.Replace(cleaned, @"\s+(Redistributable|Installer|Setup)$", "", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }

        #endregion

        #region Scanning Logic

        public async Task<List<LeftoverItem>> ScanAsync(AppItem app)
        {
            var sig = BuildSearchSignature(app);
            if (string.IsNullOrWhiteSpace(sig.CleanName) && string.IsNullOrWhiteSpace(sig.FullName))
                return new List<LeftoverItem>();

            var registryTask = Task.Run(() => ScanRegistry(app, sig));
            var filesystemTask = Task.Run(() => ScanFilesystem(app, sig));

            await Task.WhenAll(registryTask, filesystemTask);

            var combined = new List<LeftoverItem>();
            combined.AddRange(registryTask.Result);
            combined.AddRange(filesystemTask.Result);

            // Deduplicate by path
            var unique = combined
                .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(i => i.IsHighConfidence)
                .ThenByDescending(i => i.SizeBytes ?? 0)
                .ToList();

            return unique;
        }

        private List<LeftoverItem> ScanFilesystem(AppItem app, AppSearchSignature sig)
        {
            var results = new List<LeftoverItem>();

            var candidateRoots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                candidateRoots.Add(Path.Combine(userProfile, "AppData", "LocalLow"));
            }

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

                        // Case 1: Vendor folder (e.g. Google, Mozilla, Adobe, Microsoft)
                        if (ProtectedVendorNames.Contains(dirName))
                        {
                            // DO NOT match the vendor root folder!
                            // Only inspect immediate children inside the vendor directory:
                            try
                            {
                                var vendorChildren = Directory.GetDirectories(dir);
                                foreach (var child in vendorChildren)
                                {
                                    var childName = Path.GetFileName(child);
                                    if (IsMatch(childName, sig))
                                    {
                                        if (IsSafePathToDelete(child))
                                        {
                                            long size = CalculateDirectorySizeSafe(child);
                                            results.Add(new LeftoverItem(LeftoverType.Folder, child, size, childName, IsHighConfidence: true));
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        // Case 2: Standalone application folder
                        else
                        {
                            if (IsMatch(dirName, sig))
                            {
                                if (IsSafePathToDelete(dir))
                                {
                                    long size = CalculateDirectorySizeSafe(dir);
                                    results.Add(new LeftoverItem(LeftoverType.Folder, dir, size, dirName, IsHighConfidence: true));
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Case 3: App's explicit InstallLocation if known
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                if (IsSafePathToDelete(app.InstallLocation))
                {
                    long size = CalculateDirectorySizeSafe(app.InstallLocation);
                    results.Add(new LeftoverItem(LeftoverType.Folder, app.InstallLocation, size, "Install Location", IsHighConfidence: true));
                }
            }

            // Case 4: Leftover shortcuts on Desktop & Start Menu
            ScanShortcuts(sig, results);

            return results;
        }

        private static void ScanShortcuts(AppSearchSignature sig, List<LeftoverItem> results)
        {
            var shortcutDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
            };

            foreach (var dir in shortcutDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var lnks = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                    foreach (var lnk in lnks)
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        if (IsMatch(name, sig))
                        {
                            if (IsSafePathToDelete(lnk))
                            {
                                results.Add(new LeftoverItem(LeftoverType.File, lnk, new FileInfo(lnk).Length, name, IsHighConfidence: true));
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static bool IsMatch(string targetName, AppSearchSignature sig)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return false;

            // Block generic stop words
            if (BlacklistedTokens.Contains(targetName)) return false;

            // Exact match against clean name or full name
            if (string.Equals(targetName, sig.CleanName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetName, sig.FullName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Exact match against distinct product name (e.g. "Chrome", "Firefox", "VLC")
            if (!string.IsNullOrWhiteSpace(sig.DistinctProductName) &&
                string.Equals(targetName, sig.DistinctProductName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Exact match against ID product part
            if (!string.IsNullOrWhiteSpace(sig.IdProductPart) &&
                string.Equals(targetName, sig.IdProductPart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Starts with product name followed by a space (e.g. "Discord Canary" or "Notepad++ (64-bit)")
            if (sig.CleanName.Length >= 4 && targetName.StartsWith(sig.CleanName + " ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(sig.DistinctProductName) &&
                sig.DistinctProductName.Length >= 4 &&
                targetName.StartsWith(sig.DistinctProductName + " ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private List<LeftoverItem> ScanRegistry(AppItem app, AppSearchSignature sig)
        {
            var results = new List<LeftoverItem>();

            var targets = new (RegistryHive Hive, RegistryView View, string SubKeyPath, string DisplayPrefix)[]
            {
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software", @"HKEY_CURRENT_USER\Software"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software", @"HKEY_LOCAL_MACHINE\Software"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software", @"HKEY_LOCAL_MACHINE\Software\WOW6432Node")
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
                        // Check vendor root keys (e.g. Software\Google, Software\Microsoft)
                        if (ProtectedTopLevelRegistryNames.Contains(name))
                        {
                            // Never delete vendor root! Check its child keys:
                            try
                            {
                                using var vendorKey = subKey.OpenSubKey(name, false);
                                if (vendorKey != null)
                                {
                                    foreach (var childName in vendorKey.GetSubKeyNames())
                                    {
                                        if (IsMatch(childName, sig))
                                        {
                                            var fullPath = $@"{target.DisplayPrefix}\{name}\{childName}";
                                            if (IsSafeRegistryKeyToDelete(fullPath))
                                            {
                                                results.Add(new LeftoverItem(LeftoverType.RegistryKey, fullPath, null, childName, IsHighConfidence: true));
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            if (IsMatch(name, sig))
                            {
                                var fullPath = $@"{target.DisplayPrefix}\{name}";
                                if (IsSafeRegistryKeyToDelete(fullPath))
                                {
                                    results.Add(new LeftoverItem(LeftoverType.RegistryKey, fullPath, null, name, IsHighConfidence: true));
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Scan Windows Uninstall Keys specifically for this app
            ScanUninstallRegistryKeys(app, sig, results);

            return results;
        }

        private static void ScanUninstallRegistryKeys(AppItem app, AppSearchSignature sig, List<LeftoverItem> results)
        {
            var uninstallTargets = new (RegistryHive Hive, RegistryView View, string SubKeyPath, string DisplayPrefix)[]
            {
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var target in uninstallTargets)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
                    using var uninstKey = baseKey.OpenSubKey(target.SubKeyPath, false);
                    if (uninstKey == null) continue;

                    foreach (var subName in uninstKey.GetSubKeyNames())
                    {
                        bool isMatch = false;

                        // Check exact App ID match
                        if (!string.IsNullOrWhiteSpace(app.Id) && string.Equals(subName, app.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else
                        {
                            // Check DisplayName
                            try
                            {
                                using var sub = uninstKey.OpenSubKey(subName, false);
                                var disp = sub?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(disp) &&
                                    (string.Equals(disp.Trim(), app.Name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(disp.Trim(), sig.CleanName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    isMatch = true;
                                }
                            }
                            catch { }
                        }

                        if (isMatch)
                        {
                            var fullPath = $@"{target.DisplayPrefix}\{subName}";
                            if (IsSafeRegistryKeyToDelete(fullPath))
                            {
                                results.Add(new LeftoverItem(LeftoverType.RegistryKey, fullPath, null, "Uninstall Key", IsHighConfidence: true));
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static long CalculateDirectorySizeSafe(string dirPath)
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

        #endregion

        #region Deletion with Two-Tier Verification

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
                            // Double-check safety before deletion
                            if (!IsSafeRegistryKeyToDelete(item.Path))
                            {
                                ActivityLogger.Instance.Log($"SAFETY SHIELD: Blocked deletion of protected registry key: {item.Path}", ActivityType.Warning);
                                failed++;
                            }
                            else
                            {
                                DeleteRegistryKey(item.Path);
                                deleted++;
                                ok = true;
                            }
                        }
                        else if (item.Type == LeftoverType.Folder)
                        {
                            // Double-check safety before deletion
                            if (!IsSafePathToDelete(item.Path))
                            {
                                ActivityLogger.Instance.Log($"SAFETY SHIELD: Blocked deletion of protected folder: {item.Path}", ActivityType.Warning);
                                failed++;
                            }
                            else
                            {
                                if (Directory.Exists(item.Path))
                                {
                                    Directory.Delete(item.Path, recursive: true);
                                }
                                deleted++;
                                ok = true;
                            }
                        }
                        else if (item.Type == LeftoverType.File)
                        {
                            if (!IsSafePathToDelete(item.Path))
                            {
                                ActivityLogger.Instance.Log($"SAFETY SHIELD: Blocked deletion of protected file: {item.Path}", ActivityType.Warning);
                                failed++;
                            }
                            else
                            {
                                if (File.Exists(item.Path))
                                {
                                    File.Delete(item.Path);
                                }
                                deleted++;
                                ok = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ActivityLogger.Instance.Log($"Error cleaning leftover {item.Path}: {ex.Message}", ActivityType.Warning);
                        failed++;
                        ok = false;
                    }

                    progress?.Report(new LeftoverDeleteProgress(i + 1, total, item, ok));

                    // Pacing for UI smoothness
                    System.Threading.Thread.Sleep(30);
                }

                ActivityLogger.Instance.Log($"Deep clean finished: removed {deleted}, skipped {failed}.", ActivityType.Info);
                return (deleted, failed);
            });
        }

        private static void DeleteRegistryKey(string fullPath)
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

        #endregion
    }
}
