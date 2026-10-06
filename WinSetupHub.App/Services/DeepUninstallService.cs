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
    public enum UninstallScanMode
    {
        Safe,
        Moderate,
        Advanced
    }

    public record LeftoverDeleteProgress(int Current, int Total, LeftoverItem Item, bool Success);

    public class DeepUninstallService
    {
        #region Protected Sets (Revo-Grade Blacklists & Safe Guards)

        // Paths that must NEVER be deleted under any circumstance
        private static readonly HashSet<string> ProtectedExactPaths = new(StringComparer.OrdinalIgnoreCase);

        // Directories directly under Program Files, AppData, or ProgramData that are vendor/shared roots
        private static readonly HashSet<string> ProtectedVendorNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows / Microsoft Core
            "Microsoft", "Windows", "Windows Defender", "WindowsApps", "WindowsPowerShell",
            "Common Files", "Internet Explorer", "dotnet", "Packages", "Temp", "Programs",
            "LocalLow", "VirtualStore", "Microsoft.NET", "Windows NT", "Windows Mail",
            "Windows Media Player", "Windows Photo Viewer", "Windows Security",

            // Major Ecosystem Platforms & Vendors
            "Google", "Adobe", "Intel", "AMD", "NVIDIA", "NVIDIA Corporation",
            "Apple", "Apple Computer", "Apple Inc.", "Mozilla", "Oracle", "Java", "JavaSoft",
            "Steam", "Valve", "Epic Games", "Ubisoft", "Origin", "Electronic Arts", "EA Games",
            "Dropbox", "Spotify", "Discord", "GitHub", "Git", "JetBrains", "CanonicalGroupLimited",

            // Peripherals & Hardware Vendors
            "Logitech", "Razer", "Corsair", "SteelSeries", "ASUS", "MSI", "Gigabyte",
            "Sony", "Samsung", "Dell", "HP", "Lenovo", "Acer", "Huawei", "Realtek",

            // Gaming & Graphics Platforms
            "Blizzard", "Battle.net", "Riot Games", "GOG.com", "GOG Galaxy", "Unity", "Autodesk",

            // Development & Virtualization
            "Docker", "VMware", "VirtualBox", "Python", "Python3", "Nodejs", "PostgreSQL", "MySQL",

            // Media & Web Browsers
            "OBS Studio", "Blackmagic Design", "Wondershare", "CyberLink", "Corel", "TechSmith",
            "BraveSoftware", "Opera Software", "Vivaldi Technologies"
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
            "Mozilla", "Oracle", "JavaSoft", "Valve", "Epic Games", "Electronic Arts",
            "Logitech", "Razer", "Corsair", "SteelSeries", "ASUS", "MSI", "Gigabyte",
            "Blizzard", "Riot Games", "Docker", "VMware", "Python", "Realtek"
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

        public static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "svchost", "csrss", "services", "lsass", "winlogon", "system", "registry",
            "smss", "wininit", "fontdrvhost", "dwm", "taskmgr", "powershell", "cmd", "conhost",
            "runtimebroker", "sihost", "ctfmon", "startmenuexperiencehost", "shellexperiencehost",
            "searchhost", "searchindexer", "applicationframehost", "audiodg", "spoolsv", "wlanext",
            "smartscreen", "securityhealthservice", "antigravity", "code", "180hzsetuphub", "devenv"
        };

        private static readonly List<string> AppDataRoots = new();
        private static readonly List<string> ProgramFilesRoots = new();
        private static readonly List<string> UserSpecialFolders = new();

        static DeepUninstallService()
        {
            try
            {
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

                // 1. Minimum path length check (e.g. C:\Abc is only 6 chars)
                if (full.Length < 8) return false;

                // 2. Drive root check (e.g. C:\, D:\, E:\)
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return false;
                if (string.Equals(full, NormalizePath(root), StringComparison.OrdinalIgnoreCase)) return false;

                // 3. Exact protected paths check
                if (ProtectedExactPaths.Contains(full)) return false;

                // 4. Windows directory & all subdirectories check
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(winDir) && full.StartsWith(NormalizePath(winDir), StringComparison.OrdinalIgnoreCase))
                    return false;

                // 5. System drive top-level folders check (e.g. C:\Users, C:\Windows, C:\ProgramData)
                var sysDrive = Path.GetPathRoot(Environment.SystemDirectory);
                if (!string.IsNullOrEmpty(sysDrive))
                {
                    var normSysDrive = NormalizePath(sysDrive);
                    var parentOfFull = NormalizePath(Path.GetDirectoryName(full) ?? "");
                    if (string.Equals(parentOfFull, normSysDrive, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                // 6. User profile root check (e.g. C:\Users\Username)
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    var normUserProfile = NormalizePath(userProfile);
                    if (string.Equals(full, normUserProfile, StringComparison.OrdinalIgnoreCase))
                        return false;

                    var appDataParent = NormalizePath(Path.Combine(userProfile, "AppData"));
                    if (string.Equals(full, appDataParent, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // 7. User special folders check (Documents, Desktop, Downloads, Pictures, etc.)
                foreach (var special in UserSpecialFolders)
                {
                    if (string.Equals(full, special, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // 8. AppData roots check (Local, Roaming, LocalLow)
                foreach (var appDataRoot in AppDataRoots)
                {
                    if (string.Equals(full, appDataRoot, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // 9. Program Files roots check (Program Files, Program Files (x86), ProgramData)
                foreach (var progRoot in ProgramFilesRoots)
                {
                    if (string.Equals(full, progRoot, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // 10. Multi-app Vendor parent folder check:
                // If the folder is directly inside Program Files, Program Files (x86), ProgramData,
                // AppData\Local, AppData\Roaming, or AppData\Local\Programs, and its name is a vendor folder,
                // NEVER delete it!
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
                        return false;
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

                // 1. Root hives check
                if (trimmed.Equals(@"HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_USERS", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_CURRENT_CONFIG", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 2. Exact protected keys check
                if (ProtectedRegistryExactKeys.Contains(trimmed)) return false;

                // 3. Protected Software roots check
                if (trimmed.Equals(@"HKEY_CURRENT_USER\Software", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE\Software", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE\Software\WOW6432Node", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 4. Uninstall root keys check
                if (trimmed.Equals(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(@"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 5. Protected vendor & top-level keys check
                var parts = trimmed.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

                // Needs to have at least hive + Software + SubKey (e.g. HKCU\Software\App = 3 parts)
                if (parts.Length < 3) return false;

                // If it's directly under Software (e.g. HKCU\Software\Vendor), check if it's a protected vendor
                if (parts.Length == 3 && string.Equals(parts[1], "Software", StringComparison.OrdinalIgnoreCase))
                {
                    var leaf = parts[2];
                    if (ProtectedTopLevelRegistryNames.Contains(leaf) || ProtectedVendorNames.Contains(leaf))
                        return false;
                }
                // If it's under WOW6432Node (e.g. HKLM\Software\WOW6432Node\Vendor)
                else if (parts.Length == 4 && string.Equals(parts[1], "Software", StringComparison.OrdinalIgnoreCase) && string.Equals(parts[2], "WOW6432Node", StringComparison.OrdinalIgnoreCase))
                {
                    var leaf = parts[3];
                    if (ProtectedTopLevelRegistryNames.Contains(leaf) || ProtectedVendorNames.Contains(leaf))
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
            cleaned = Regex.Replace(cleaned, @"\s*[\(\[](x64|x86|arm64|32-bit|64-bit)[\)\]]", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+v?\d+(\.\d+)*\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+(Redistributable|Installer|Setup)$", "", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }

        #endregion

        #region Scanning Logic (Revo Modes: Safe, Moderate, Advanced)

        public async Task<List<LeftoverItem>> ScanAsync(AppItem app, UninstallScanMode mode = UninstallScanMode.Moderate)
        {
            var sig = BuildSearchSignature(app);
            if (string.IsNullOrWhiteSpace(sig.CleanName) && string.IsNullOrWhiteSpace(sig.FullName))
                return new List<LeftoverItem>();

            var registryTask = Task.Run(() => ScanRegistry(app, sig, mode));
            var filesystemTask = Task.Run(() => ScanFilesystem(app, sig, mode));

            await Task.WhenAll(registryTask, filesystemTask);

            var combined = new List<LeftoverItem>();
            combined.AddRange(registryTask.Result);
            combined.AddRange(filesystemTask.Result);

            var unique = combined
                .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(i => i.IsHighConfidence)
                .ThenByDescending(i => i.SizeBytes ?? 0)
                .ToList();

            return unique;
        }

        private List<LeftoverItem> ScanFilesystem(AppItem app, AppSearchSignature sig, UninstallScanMode mode)
        {
            var results = new List<LeftoverItem>();

            var candidateRoots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            if (mode >= UninstallScanMode.Moderate)
            {
                candidateRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                candidateRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            }

            if (mode == UninstallScanMode.Advanced)
            {
                candidateRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    candidateRoots.Add(Path.Combine(userProfile, "AppData", "LocalLow"));
                }
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

                        if (ProtectedVendorNames.Contains(dirName))
                        {
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

            if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                if (IsSafePathToDelete(app.InstallLocation))
                {
                    long size = CalculateDirectorySizeSafe(app.InstallLocation);
                    results.Add(new LeftoverItem(LeftoverType.Folder, app.InstallLocation, size, "Install Location", IsHighConfidence: true));
                }
            }

            if (mode >= UninstallScanMode.Moderate)
            {
                ScanShortcuts(sig, results);
            }

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

            if (targetName.Length < 3) return false;

            if (BlacklistedTokens.Contains(targetName)) return false;
            if (ProtectedVendorNames.Contains(targetName)) return false;

            // 1. Exact match on clean name or full name
            if (string.Equals(targetName, sig.CleanName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetName, sig.FullName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Exact match on distinct product name (e.g. "Chrome" for "Google Chrome")
            if (!string.IsNullOrWhiteSpace(sig.DistinctProductName) &&
                string.Equals(targetName, sig.DistinctProductName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Exact match on Winget ID product part (e.g. "VLC" for "VideoLAN.VLC")
            if (!string.IsNullOrWhiteSpace(sig.IdProductPart) &&
                string.Equals(targetName, sig.IdProductPart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 4. Specific prefix matching with delimiter check (' ', '-', '_')
            if (sig.CleanName.Length >= 5)
            {
                if (targetName.StartsWith(sig.CleanName + " ", StringComparison.OrdinalIgnoreCase) ||
                    targetName.StartsWith(sig.CleanName + "-", StringComparison.OrdinalIgnoreCase) ||
                    targetName.StartsWith(sig.CleanName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(sig.DistinctProductName) && sig.DistinctProductName.Length >= 5)
            {
                if (targetName.StartsWith(sig.DistinctProductName + " ", StringComparison.OrdinalIgnoreCase) ||
                    targetName.StartsWith(sig.DistinctProductName + "-", StringComparison.OrdinalIgnoreCase) ||
                    targetName.StartsWith(sig.DistinctProductName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private List<LeftoverItem> ScanRegistry(AppItem app, AppSearchSignature sig, UninstallScanMode mode)
        {
            var results = new List<LeftoverItem>();

            ScanUninstallRegistryKeys(app, sig, results);

            var targets = new List<(RegistryHive Hive, RegistryView View, string SubKeyPath, string DisplayPrefix)>
            {
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software", @"HKEY_CURRENT_USER\Software"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software", @"HKEY_LOCAL_MACHINE\Software")
            };

            if (mode >= UninstallScanMode.Moderate)
            {
                targets.Add((RegistryHive.LocalMachine, RegistryView.Registry32, @"Software", @"HKEY_LOCAL_MACHINE\Software\WOW6432Node"));
            }

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
                        if (ProtectedTopLevelRegistryNames.Contains(name))
                        {
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

            var cleanId = AppMetadataHelper.CleanPackageId(app.Id);
            var cleanName = app.Name?.Trim() ?? string.Empty;

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

                        if (!string.IsNullOrWhiteSpace(app.Id) && string.Equals(subName, app.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(cleanId) && string.Equals(subName, cleanId, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(cleanName) && string.Equals(subName, cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else
                        {
                            try
                            {
                                using var sub = uninstKey.OpenSubKey(subName, false);
                                var disp = sub?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(disp) &&
                                    (string.Equals(disp.Trim(), cleanName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(disp.Trim(), sig.CleanName, StringComparison.OrdinalIgnoreCase) ||
                                     (!string.IsNullOrWhiteSpace(sig.DistinctProductName) && string.Equals(disp.Trim(), sig.DistinctProductName, StringComparison.OrdinalIgnoreCase))))
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
                            if (!IsSafeRegistryKeyToDelete(item.Path))
                            {
                                ActivityLogger.Instance.Log($"SAFETY SHIELD: Blocked deletion of protected registry key: {item.Path}", ActivityType.Warning);
                                failed++;
                            }
                            else
                            {
                                bool okReg = DeleteRegistryKey(item.Path);
                                if (okReg) { deleted++; ok = true; }
                                else { failed++; }
                            }
                        }
                        else if (item.Type == LeftoverType.Folder)
                        {
                            if (!IsSafePathToDelete(item.Path))
                            {
                                ActivityLogger.Instance.Log($"SAFETY SHIELD: Blocked deletion of protected folder: {item.Path}", ActivityType.Warning);
                                failed++;
                            }
                            else
                            {
                                bool okDir = DeleteFolderSafe(item.Path);
                                if (okDir) { deleted++; ok = true; }
                                else { failed++; }
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
                                bool okFile = DeleteFileSafe(item.Path);
                                if (okFile) { deleted++; ok = true; }
                                else { failed++; }
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
                    System.Threading.Thread.Sleep(25);
                }

                ActivityLogger.Instance.Log($"Deep clean finished: removed {deleted}, skipped {failed}.", ActivityType.Info);
                return (deleted, failed);
            });
        }

        public static bool DeleteRegistryKey(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;

            try
            {
                if (fullPath.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
                {
                    var subPath = fullPath.Substring(@"HKEY_CURRENT_USER\".Length);
                    Registry.CurrentUser.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
                    return true;
                }
                else if (fullPath.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
                {
                    var subPath = fullPath.Substring(@"HKEY_LOCAL_MACHINE\".Length);
                    bool deleted = false;

                    try
                    {
                        if (subPath.StartsWith(@"Software\WOW6432Node\", StringComparison.OrdinalIgnoreCase))
                        {
                            var wowPath = subPath.Substring(@"Software\WOW6432Node\".Length);
                            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                            using var swKey = baseKey.OpenSubKey("Software", true);
                            swKey?.DeleteSubKeyTree(wowPath, throwOnMissingSubKey: false);
                            deleted = true;
                        }
                        else
                        {
                            using var base64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                            base64.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);

                            using var base32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                            base32.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
                            deleted = true;
                        }
                    }
                    catch
                    {
                        // In-process failed due to UAC permissions: fallback to elevated reg.exe delete!
                        var regPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = $"delete \"{fullPath}\" /f",
                            UseShellExecute = true,
                            Verb = "runas",
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };
                        using var p = System.Diagnostics.Process.Start(regPsi);
                        p?.WaitForExit(3000);
                        deleted = p != null && p.ExitCode == 0;
                    }

                    return deleted;
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Registry delete error for {fullPath}: {ex.Message}", ActivityType.Warning);
                return false;
            }
            return false;
        }

        public static bool DeleteFolderSafe(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return true;
            if (!IsSafePathToDelete(folderPath)) return false;

            try
            {
                // Normalize attributes on files to remove ReadOnly flags
                try
                {
                    var di = new DirectoryInfo(folderPath);
                    foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try { fi.Attributes = FileAttributes.Normal; } catch { }
                    }
                }
                catch { }

                try
                {
                    Directory.Delete(folderPath, recursive: true);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    // Fallback to elevated rd /s /q
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c rd /s /q \"{folderPath}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(5000);
                    return !Directory.Exists(folderPath);
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Folder delete error for {folderPath}: {ex.Message}", ActivityType.Warning);
                return false;
            }
        }

        public static bool DeleteFileSafe(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return true;
            if (!IsSafePathToDelete(filePath)) return false;

            try
            {
                try { File.SetAttributes(filePath, FileAttributes.Normal); } catch { }
                try
                {
                    File.Delete(filePath);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c del /f /q \"{filePath}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(3000);
                    return !File.Exists(filePath);
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"File delete error for {filePath}: {ex.Message}", ActivityType.Warning);
                return false;
            }
        }

        #endregion

        #region Revo-Style Operational Helpers

        public static async Task<bool> CreateRestorePointAsync(string appName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var cleanDesc = Regex.Replace(appName ?? "App", @"['""`$]", "");
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '180Hz Setup Hub: Uninstall {cleanDesc}' -RestorePointType 'APPLICATION_UNINSTALL' -ErrorAction SilentlyContinue\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p != null)
                    {
                        bool exited = p.WaitForExit(12000);
                        return exited && p.ExitCode == 0;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static async Task<string?> CreateRegistryBackupAsync(AppItem app)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var backupDir = Path.Combine(Path.GetTempPath(), "180HzSetupHub_Backups");
                    Directory.CreateDirectory(backupDir);
                    var safeName = Regex.Replace(app.Name ?? "App", @"[^\w\-]", "_");
                    var backupFile = Path.Combine(backupDir, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.reg");

                    if (!string.IsNullOrWhiteSpace(app.Id))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = $"export \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{app.Id}\" \"{backupFile}\" /y",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit(3000);
                    }

                    return File.Exists(backupFile) ? backupFile : null;
                }
                catch
                {
                    return null;
                }
            });
        }

        public static void KillProcessesForApp(AppItem app)
        {
            try
            {
                var sig = new DeepUninstallService().BuildSearchSignature(app);
                var procs = System.Diagnostics.Process.GetProcesses();

                foreach (var p in procs)
                {
                    try
                    {
                        var procName = p.ProcessName;
                        if (ProtectedProcessNames.Contains(procName)) continue;

                        // Never terminate uninstallation processes or helpers!
                        if (procName.Contains("unins", StringComparison.OrdinalIgnoreCase) ||
                            procName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                            procName.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        bool shouldKill = false;

                        if (!string.IsNullOrWhiteSpace(app.InstallLocation) &&
                            IsSafePathToDelete(app.InstallLocation))
                        {
                            try
                            {
                                var procPath = p.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(procPath) &&
                                    procPath.StartsWith(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldKill = true;
                                }
                            }
                            catch { }
                        }

                        if (!shouldKill)
                        {
                            if (string.Equals(procName, sig.CleanName, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrWhiteSpace(sig.DistinctProductName) &&
                                 string.Equals(procName, sig.DistinctProductName, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(sig.IdProductPart) &&
                                 string.Equals(procName, sig.IdProductPart, StringComparison.OrdinalIgnoreCase)))
                            {
                                shouldKill = true;
                            }
                        }

                        if (shouldKill)
                        {
                            p.Kill();
                            p.WaitForExit(1500);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static string? FindRegistryUninstallString(AppItem app)
        {
            var targets = new (RegistryHive Hive, RegistryView View, string SubKey)[]
            {
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            var cleanId = AppMetadataHelper.CleanPackageId(app.Id);
            var cleanName = app.Name?.Trim() ?? string.Empty;
            var normName = AppMetadataHelper.NormalizeAppName(app.Name);

            foreach (var target in targets)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
                    using var uninst = baseKey.OpenSubKey(target.SubKey, false);
                    if (uninst == null) continue;

                    foreach (var subName in uninst.GetSubKeyNames())
                    {
                        bool isKeyMatch = false;

                        if (!string.IsNullOrWhiteSpace(app.Id) && subName.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            isKeyMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(cleanId) && subName.Equals(cleanId, StringComparison.OrdinalIgnoreCase))
                        {
                            isKeyMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(cleanName) && subName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            isKeyMatch = true;
                        }

                        using var sub = uninst.OpenSubKey(subName);
                        if (sub == null) continue;

                        if (!isKeyMatch)
                        {
                            var disp = sub.GetValue("DisplayName") as string;
                            if (!string.IsNullOrWhiteSpace(disp))
                            {
                                var cleanDisp = disp.Trim();
                                if (string.Equals(cleanDisp, cleanName, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrWhiteSpace(normName) && string.Equals(cleanDisp, normName, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrWhiteSpace(cleanName) && cleanName.Length >= 4 && cleanDisp.Contains(cleanName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    isKeyMatch = true;
                                }
                            }
                        }

                        if (isKeyMatch)
                        {
                            var uStr = sub.GetValue("UninstallString") as string;
                            var qStr = sub.GetValue("QuietUninstallString") as string;
                            if (!string.IsNullOrWhiteSpace(uStr)) return uStr;
                            if (!string.IsNullOrWhiteSpace(qStr)) return qStr;
                        }
                    }
                }
                catch { }
            }

            // Fallback: check cached registry info
            var regInfo = AppMetadataHelper.GetRegistryInfo(app.Name ?? string.Empty, app.Id ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(regInfo?.UninstallString))
            {
                return regInfo.UninstallString;
            }

            return null;
        }

        public static (string fileName, string arguments) ParseUninstallString(string uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString)) return ("", "");
            string raw = uninstallString.Trim();

            // 1. Quoted executable
            if (raw.StartsWith("\""))
            {
                int endQuote = raw.IndexOf('\"', 1);
                if (endQuote > 0)
                {
                    string file = raw.Substring(1, endQuote - 1).Trim();
                    string args = raw.Substring(endQuote + 1).Trim();
                    return NormalizeParsedCommand(file, args);
                }
            }

            // 2. msiexec /X or /I
            if (raw.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                int space = raw.IndexOf(' ');
                if (space > 0)
                {
                    return NormalizeParsedCommand("msiexec.exe", raw.Substring(space + 1).Trim());
                }
                return ("msiexec.exe", "");
            }

            // 3. Unquoted executable with .exe extension
            int exeIdx = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                string file = raw.Substring(0, exeIdx + 4).Trim('\"', ' ');
                string args = raw.Substring(exeIdx + 4).Trim();
                return NormalizeParsedCommand(file, args);
            }

            // 4. Unquoted script (.bat / .cmd)
            int batIdx = raw.IndexOf(".bat", StringComparison.OrdinalIgnoreCase);
            if (batIdx < 0) batIdx = raw.IndexOf(".cmd", StringComparison.OrdinalIgnoreCase);
            if (batIdx > 0)
            {
                string file = raw.Substring(0, batIdx + 4).Trim('\"', ' ');
                string args = raw.Substring(batIdx + 4).Trim();
                return NormalizeParsedCommand(file, args);
            }

            // 5. Existing file check
            if (File.Exists(raw))
            {
                return NormalizeParsedCommand(raw, "");
            }

            // 6. First space fallback
            int firstSpace = raw.IndexOf(' ');
            if (firstSpace > 0)
            {
                return NormalizeParsedCommand(raw.Substring(0, firstSpace).Trim('\"'), raw.Substring(firstSpace + 1).Trim());
            }

            return NormalizeParsedCommand(raw.Trim('\"'), "");
        }

        private static (string fileName, string arguments) NormalizeParsedCommand(string fileName, string arguments)
        {
            var cleanFile = fileName.Trim('\"', ' ');
            var cleanArgs = arguments.Trim();

            if (cleanFile.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                cleanFile = "msiexec.exe";
                cleanArgs = cleanArgs.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
                if (!cleanArgs.Contains("/X", StringComparison.OrdinalIgnoreCase) && !cleanArgs.Contains("/package", StringComparison.OrdinalIgnoreCase))
                {
                    cleanArgs = $"/X {cleanArgs}".Trim();
                }
            }

            return (cleanFile, cleanArgs);
        }

        public static System.Diagnostics.Process? LaunchNativeUninstallProcess(string uninstallString, string? workingDir = null)
        {
            try
            {
                var (fileName, arguments) = ParseUninstallString(uninstallString);
                if (string.IsNullOrWhiteSpace(fileName)) return null;

                // Resolve relative path if possible
                if (!Path.IsPathRooted(fileName) && !fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(workingDir))
                    {
                        var candidate = Path.Combine(workingDir, fileName);
                        if (File.Exists(candidate)) fileName = candidate;
                    }
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
                {
                    psi.WorkingDirectory = workingDir;
                }

                return System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Native uninstaller launch error: {ex.Message}", ActivityType.Warning);
                return null;
            }
        }

        public static async Task<bool> RunNativeUninstallStringAsync(string uninstallString, string? workingDir = null)
        {
            try
            {
                using var proc = LaunchNativeUninstallProcess(uninstallString, workingDir);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    return proc.ExitCode == 0 || proc.ExitCode == 3010 || proc.ExitCode == 1641 || proc.ExitCode == 1605;
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Native uninstaller execution error: {ex.Message}", ActivityType.Warning);
            }
            return false;
        }

        public static void RemoveRegistryUninstallKeys(AppItem app)
        {
            var targets = new (RegistryHive Hive, RegistryView View, string SubKey, string Prefix)[]
            {
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKEY_CURRENT_USER"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKEY_LOCAL_MACHINE"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKEY_LOCAL_MACHINE")
            };

            var cleanId = AppMetadataHelper.CleanPackageId(app.Id);
            var cleanName = app.Name?.Trim() ?? string.Empty;

            foreach (var target in targets)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View);
                    using var uninst = baseKey.OpenSubKey(target.SubKey, false);
                    if (uninst == null) continue;

                    foreach (var subName in uninst.GetSubKeyNames())
                    {
                        bool isMatch = false;
                        if (!string.IsNullOrWhiteSpace(app.Id) && subName.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(cleanId) && subName.Equals(cleanId, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(cleanName) && subName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else
                        {
                            try
                            {
                                using var sub = uninst.OpenSubKey(subName);
                                var disp = sub?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(disp) &&
                                    (string.Equals(disp.Trim(), cleanName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(disp.Trim(), app.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
                                {
                                    isMatch = true;
                                }
                            }
                            catch { }
                        }

                        if (isMatch)
                        {
                            var fullKey = $@"{target.Prefix}\{target.SubKey}\{subName}";
                            if (IsSafeRegistryKeyToDelete(fullKey))
                            {
                                DeleteRegistryKey(fullKey);
                            }
                        }
                    }
                }
                catch { }
            }

            CleanStartupLeftovers(app);
        }

        public static void CleanStartupLeftovers(AppItem app)
        {
            try
            {
                var runKeys = new[]
                {
                    (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
                    (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
                    (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"),
                    (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
                    (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run")
                };

                var sig = new DeepUninstallService().BuildSearchSignature(app);
                var cleanName = sig.CleanName;
                var installLoc = app.InstallLocation;

                foreach (var (hive, path) in runKeys)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(path, true);
                        if (key == null) continue;

                        foreach (var valName in key.GetValueNames())
                        {
                            bool shouldDelete = false;
                            if (string.Equals(valName, cleanName, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrWhiteSpace(sig.DistinctProductName) && string.Equals(valName, sig.DistinctProductName, StringComparison.OrdinalIgnoreCase)))
                            {
                                shouldDelete = true;
                            }
                            else if (!string.IsNullOrWhiteSpace(installLoc))
                            {
                                var valData = key.GetValue(valName) as string;
                                if (!string.IsNullOrWhiteSpace(valData) && valData.Contains(installLoc, StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldDelete = true;
                                }
                            }

                            if (shouldDelete)
                            {
                                try { key.DeleteValue(valName, false); } catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void RemoveShortcutsForApp(AppItem app)
        {
            var candidateDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
            };

            var cleanName = app.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cleanName)) return;

            foreach (var dir in candidateDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var lnks = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                    foreach (var lnk in lnks)
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        if (string.Equals(name, cleanName, StringComparison.OrdinalIgnoreCase) ||
                            (cleanName.Length >= 4 && name.StartsWith(cleanName + " ", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (IsSafePathToDelete(lnk))
                            {
                                try { File.Delete(lnk); } catch { }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        #endregion
    }
}
