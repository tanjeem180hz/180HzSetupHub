using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class AppMetadataHelper
    {
        public class RegistryAppInfo
        {
            public string DisplayName { get; set; } = "";
            public string DisplayVersion { get; set; } = "";
            public string Size { get; set; } = "";
            public string? InstallLocation { get; set; }
            public string? DisplayIcon { get; set; }
            public string? UninstallString { get; set; }
            public string? Publisher { get; set; }
            public string? WebUrl { get; set; }
            public DateTime? InstallDate { get; set; }
        }

        private static Dictionary<string, RegistryAppInfo>? _registryCache;
        private static HashSet<string>? _appxPackageCache;
        private static DateTime _appxCacheTime = DateTime.MinValue;
        private static HashSet<string>? _startMenuLinksCache;
        private static HashSet<string>? _installedProgramFoldersCache;
        private static DateTime _diskScanTime = DateTime.MinValue;

        public static void InvalidateCache()
        {
            _registryCache = null;
            _appxPackageCache = null;
            _startMenuLinksCache = null;
            _installedProgramFoldersCache = null;
        }

        private static void EnsureDiskFolderCaches()
        {
            if (_startMenuLinksCache != null && _installedProgramFoldersCache != null && (DateTime.UtcNow - _diskScanTime).TotalSeconds < 60)
                return;

            var linkSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] startDirs = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
            };
            foreach (var dir in startDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                        {
                            var name = Path.GetFileNameWithoutExtension(lnk);
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                linkSet.Add(name);
                                var norm = NormalizeAppName(name);
                                if (!string.IsNullOrWhiteSpace(norm))
                                {
                                    linkSet.Add(norm);
                                    var stripped = StripPublisherPrefix(norm);
                                    if (!string.IsNullOrWhiteSpace(stripped)) linkSet.Add(stripped);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            _startMenuLinksCache = linkSet;

            var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] baseDirs = {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            foreach (var b in baseDirs)
            {
                if (Directory.Exists(b))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(b))
                        {
                            var subName = Path.GetFileName(sub);
                            if (!string.IsNullOrWhiteSpace(subName))
                            {
                                folderSet.Add(subName);
                                var norm = NormalizeAppName(subName);
                                if (!string.IsNullOrWhiteSpace(norm))
                                {
                                    folderSet.Add(norm);
                                    var stripped = StripPublisherPrefix(norm);
                                    if (!string.IsNullOrWhiteSpace(stripped)) folderSet.Add(stripped);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            _installedProgramFoldersCache = folderSet;
            _diskScanTime = DateTime.UtcNow;
        }

        public static void EnrichAppItem(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            // 1. Smart match against catalog
            if (catalog != null)
            {
                var match = FindCatalogMatch(app, catalog);
                if (match != null)
                {
                    if (string.IsNullOrWhiteSpace(app.IconUrl)) app.IconUrl = match.IconUrl;
                    if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General") app.Category = match.Category;
                    if (string.IsNullOrWhiteSpace(app.WebUrl)) app.WebUrl = match.WebUrl;
                    if (string.IsNullOrWhiteSpace(app.Size)) app.Size = match.Size;
                    if (string.IsNullOrWhiteSpace(app.Description)) app.Description = match.Description;
                    app.AccentColor = match.AccentColor;
                }
            }

            // 2. Query Windows Registry for exact real installed version, size, and real icon path
            var regInfo = GetRegistryInfo(app.Name, app.Id);
            if (regInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(regInfo.DisplayVersion) && (string.IsNullOrWhiteSpace(app.Version) || app.Version == "Latest"))
                {
                    app.Version = regInfo.DisplayVersion;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation))
                {
                    app.InstallLocation = regInfo.InstallLocation;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.Size))
                {
                    app.Size = regInfo.Size;
                }
                else if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    var dirSize = CalculateDirectorySize(regInfo.InstallLocation);
                    if (!string.IsNullOrWhiteSpace(dirSize))
                    {
                        app.Size = dirSize;
                    }
                }

                if (!string.IsNullOrWhiteSpace(regInfo.DisplayIcon))
                {
                    var cleanIcon = CleanIconPath(regInfo.DisplayIcon);
                    if (File.Exists(cleanIcon))
                    {
                        app.LocalIconPath = cleanIcon;
                    }
                }

                // If no DisplayIcon found, search InstallLocation for primary executable
                if (string.IsNullOrWhiteSpace(app.LocalIconPath) && !string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    try
                    {
                        var exes = Directory.GetFiles(regInfo.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        var matchExe = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(app.Name, StringComparison.OrdinalIgnoreCase))
                                       ?? exes.FirstOrDefault(e => !Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
                        if (matchExe != null && File.Exists(matchExe))
                        {
                            app.LocalIconPath = matchExe;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(regInfo.UninstallString))
                {
                    app.UninstallString = regInfo.UninstallString;
                }

                if (string.IsNullOrWhiteSpace(app.Publisher) && !string.IsNullOrWhiteSpace(regInfo.Publisher))
                {
                    app.Publisher = regInfo.Publisher;
                }

                if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(regInfo.WebUrl))
                {
                    app.WebUrl = regInfo.WebUrl;
                }

                if (regInfo.InstallDate.HasValue && !app.InstallDate.HasValue)
                {
                    app.InstallDate = regInfo.InstallDate;
                }
            }

            if (!app.InstallDate.HasValue && !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                try
                {
                    app.InstallDate = Directory.GetCreationTime(app.InstallLocation);
                }
                catch { }
            }

            // 3. If local icon still missing, try Windows App Paths registry
            if (string.IsNullOrWhiteSpace(app.LocalIconPath))
            {
                var appPathExe = ResolveFromAppPaths(app.Name) ?? ResolveFromAppPaths(CleanPackageId(app.Id));
                if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
                {
                    app.LocalIconPath = appPathExe;
                }
            }

            // 4. Resolve authentic official website via catalog, registry, or comprehensive domain mapping
            if (string.IsNullOrWhiteSpace(app.WebUrl))
            {
                var resolved = ResolveOfficialUrl(app, catalog);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    app.WebUrl = resolved;
                }
                else if (app.Name.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase) ||
                         app.Name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                {
                    app.WebUrl = "https://www.microsoft.com";
                }
            }

            // 5. Fallback size if truly unmeasured: "-"
            if (string.IsNullOrWhiteSpace(app.Size))
            {
                app.Size = "-";
            }

            // 6. Derive authentic IconUrl from WebUrl if IconUrl is missing
            if (string.IsNullOrWhiteSpace(app.IconUrl) && !string.IsNullOrWhiteSpace(app.WebUrl))
            {
                var favicon = IconCacheService.DeriveFaviconUrl(app.WebUrl);
                if (!string.IsNullOrWhiteSpace(favicon))
                {
                    app.IconUrl = favicon;
                }
            }

            // 7. Try load local icon immediately if available
            if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && File.Exists(app.LocalIconPath))
            {
                var localImg = IconCacheService.GetLocalFileIcon(app.LocalIconPath);
                if (localImg != null)
                {
                    app.IconImageSource = localImg;
                }
            }
        }

        public static bool IsAppInstalled(AppItem app)
        {
            if (app == null) return false;

            // Special case for Microsoft Store itself
            if (string.Equals(app.Id, "Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 1. Windows Registry uninstall check (covers Win32 / x64 / x86 software)
            var reg = GetRegistryInfo(app.Name, app.Id);
            if (reg != null)
            {
                if (!string.IsNullOrWhiteSpace(reg.DisplayVersion) && (string.IsNullOrWhiteSpace(app.Version) || app.Version == "Latest"))
                {
                    app.Version = reg.DisplayVersion;
                }
                if (!string.IsNullOrWhiteSpace(reg.InstallLocation) && Directory.Exists(reg.InstallLocation))
                {
                    app.InstallLocation = reg.InstallLocation;
                }
                if (!string.IsNullOrWhiteSpace(reg.DisplayIcon) && string.IsNullOrWhiteSpace(app.LocalIconPath))
                {
                    var cleaned = CleanIconPath(reg.DisplayIcon);
                    if (File.Exists(cleaned)) app.LocalIconPath = cleaned;
                }
                return true;
            }

            // 2. Canonical verified installation filepaths on disk (instant, 100% reliable)
            if (CheckCanonicalInstallationPath(app.Id, app.Name))
            {
                return true;
            }

            // 3. Check registered App Paths
            var appPathExe = ResolveFromAppPaths(app.Name) ?? ResolveFromAppPaths(CleanPackageId(app.Id));
            if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
            {
                if (string.IsNullOrWhiteSpace(app.LocalIconPath)) app.LocalIconPath = appPathExe;
                return true;
            }

            // 4. Check local icon path if it points to an installed executable
            if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && app.LocalIconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(app.LocalIconPath))
            {
                return true;
            }

            // 5. Check App InstallLocation if present
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                if (DirectoryContainsExe(app.InstallLocation))
                {
                    return true;
                }
            }

            // 6. Check standard program directories (fast hash set lookup)
            string normName = NormalizeAppName(app.Name);
            string strippedName = StripPublisherPrefix(normName);
            string cleanAlphaName = CompactAlpha(normName);

            EnsureDiskFolderCaches();
            if (_installedProgramFoldersCache != null &&
                (_installedProgramFoldersCache.Contains(app.Name) ||
                 _installedProgramFoldersCache.Contains(normName) ||
                 (!string.IsNullOrWhiteSpace(strippedName) && _installedProgramFoldersCache.Contains(strippedName))))
            {
                return true;
            }

            // 7. Check Start Menu shortcuts (fast hash set lookup)
            if (_startMenuLinksCache != null &&
                (_startMenuLinksCache.Contains(app.Name) ||
                 _startMenuLinksCache.Contains(normName) ||
                 (!string.IsNullOrWhiteSpace(strippedName) && _startMenuLinksCache.Contains(strippedName)) ||
                 (!string.IsNullOrWhiteSpace(normName) && normName.Length >= 4 && _startMenuLinksCache.Any(l => l.Contains(normName, StringComparison.OrdinalIgnoreCase))) ||
                 (!string.IsNullOrWhiteSpace(strippedName) && strippedName.Length >= 4 && _startMenuLinksCache.Any(l => l.Contains(strippedName, StringComparison.OrdinalIgnoreCase)))))
            {
                return true;
            }

            // 8. Check AppModel / Store / Appx packages (both user & system stores)
            if (IsAppxInstalled(app.Id, app.Name, normName, cleanAlphaName))
            {
                return true;
            }

            return false;
        }

        public static bool DirectoryContainsExe(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            try
            {
                return Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                    .Any(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static readonly Dictionary<string, string[]> StorePackageNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["9PLM9XGG6VKS"] = new[] { "OpenAI.Codex", "ChatGPT" },
            ["OpenAI.ChatGPT"] = new[] { "OpenAI.Codex", "ChatGPT" },
            ["9NCBCSZSJRSB"] = new[] { "SpotifyAB.SpotifyMusic", "Spotify" },
            ["Spotify.Spotify"] = new[] { "SpotifyAB.SpotifyMusic", "Spotify" },
            ["9NKSQGP7F2NH"] = new[] { "5319275A.WhatsAppDesktop", "WhatsApp" },
            ["WhatsApp.WhatsApp"] = new[] { "5319275A.WhatsAppDesktop", "WhatsApp" },
            ["9NZTWSQNTD0S"] = new[] { "Telegram", "TelegramDesktop" },
            ["Telegram.TelegramDesktop"] = new[] { "Telegram", "TelegramDesktop" },
            ["XP89DCGQ3K6VLD"] = new[] { "Microsoft.PowerToys", "PowerToys" },
            ["Microsoft.PowerToys"] = new[] { "Microsoft.PowerToys", "PowerToys" },
            ["9N0DX20HK701"] = new[] { "Microsoft.WindowsTerminal", "WindowsTerminal" },
            ["Microsoft.WindowsTerminal"] = new[] { "Microsoft.WindowsTerminal", "WindowsTerminal" },
            ["Anthropic.Claude"] = new[] { "Claude" }
        };

        public static bool IsAppxInstalled(string id, string name, string normName, string cleanAlphaName)
        {
            try
            {
                if (_appxPackageCache == null || (DateTime.UtcNow - _appxCacheTime).TotalSeconds > 30)
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string[] subKeys = {
                        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages",
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications"
                    };

                    foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                    {
                        foreach (var path in subKeys)
                        {
                            try
                            {
                                using var key = root.OpenSubKey(path);
                                if (key != null)
                                {
                                    foreach (var sub in key.GetSubKeyNames())
                                    {
                                        set.Add(sub);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    _appxPackageCache = set;
                    _appxCacheTime = DateTime.UtcNow;
                }

                if (_appxPackageCache == null || _appxPackageCache.Count == 0) return false;

                var tokens = new List<string>();
                if (!string.IsNullOrWhiteSpace(id)) tokens.Add(id);
                if (!string.IsNullOrWhiteSpace(cleanAlphaName) && cleanAlphaName.Length >= 4) tokens.Add(cleanAlphaName);
                if (!string.IsNullOrWhiteSpace(normName) && normName.Length >= 4) tokens.Add(normName);

                if (!string.IsNullOrWhiteSpace(id) && StorePackageNameMap.TryGetValue(id, out var mappedId))
                {
                    tokens.AddRange(mappedId);
                }
                if (!string.IsNullOrWhiteSpace(name) && StorePackageNameMap.TryGetValue(name, out var mappedName))
                {
                    tokens.AddRange(mappedName);
                }

                foreach (var pkg in _appxPackageCache)
                {
                    foreach (var token in tokens)
                    {
                        if (token.Length >= 3 && pkg.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool CheckCanonicalInstallationPath(string id, string name)
        {
            var candidates = new List<string>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var norm = NormalizeAppName(name).ToLowerInvariant();
            var lowerId = (id ?? "").ToLowerInvariant();

            if (norm.Contains("code") || lowerId.Contains("visualstudiocode"))
            {
                candidates.Add(Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"));
                candidates.Add(Path.Combine(progFiles, "Microsoft VS Code", "Code.exe"));
            }
            if (norm.Contains("chrome") || lowerId.Contains("google.chrome"))
            {
                candidates.Add(Path.Combine(progFiles, "Google", "Chrome", "Application", "chrome.exe"));
                candidates.Add(Path.Combine(progFilesX86, "Google", "Chrome", "Application", "chrome.exe"));
            }
            if (norm.Contains("firefox") || lowerId.Contains("mozilla.firefox"))
            {
                candidates.Add(Path.Combine(progFiles, "Mozilla Firefox", "firefox.exe"));
                candidates.Add(Path.Combine(progFilesX86, "Mozilla Firefox", "firefox.exe"));
            }
            if (norm.Contains("brave") || lowerId.Contains("brave.brave"))
            {
                candidates.Add(Path.Combine(progFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
                candidates.Add(Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
            }
            if (norm.Contains("opera") || lowerId.Contains("opera.opera"))
            {
                candidates.Add(Path.Combine(localAppData, "Programs", "Opera", "launcher.exe"));
                candidates.Add(Path.Combine(localAppData, "Programs", "Opera GX", "launcher.exe"));
                candidates.Add(Path.Combine(progFiles, "Opera", "launcher.exe"));
            }
            if (norm.Contains("discord") || lowerId.Contains("discord.discord"))
            {
                candidates.Add(Path.Combine(localAppData, "Discord", "Update.exe"));
            }
            if (norm.Contains("telegram") || lowerId.Contains("telegram.telegramdesktop"))
            {
                candidates.Add(Path.Combine(appData, "Telegram Desktop", "Telegram.exe"));
            }
            if (norm.Contains("steam") || lowerId.Contains("valve.steam"))
            {
                candidates.Add(Path.Combine(progFilesX86, "Steam", "steam.exe"));
                candidates.Add(Path.Combine(progFiles, "Steam", "steam.exe"));
            }
            if (norm.Contains("vlc") || lowerId.Contains("videolan.vlc"))
            {
                candidates.Add(Path.Combine(progFiles, "VideoLAN", "VLC", "vlc.exe"));
                candidates.Add(Path.Combine(progFilesX86, "VideoLAN", "VLC", "vlc.exe"));
            }
            if (norm.Contains("spotify") || lowerId.Contains("spotify.spotify"))
            {
                candidates.Add(Path.Combine(appData, "Spotify", "Spotify.exe"));
            }
            if (norm.Contains("afterburner") || lowerId.Contains("afterburner"))
            {
                candidates.Add(Path.Combine(progFilesX86, "MSI Afterburner", "MSIAfterburner.exe"));
            }
            if (norm.Contains("rtss") || lowerId.Contains("rtss") || norm.Contains("statistics server"))
            {
                candidates.Add(Path.Combine(progFilesX86, "RivaTuner Statistics Server", "RTSS.exe"));
            }
            if (norm.Contains("7-zip") || lowerId.Contains("7zip"))
            {
                candidates.Add(Path.Combine(progFiles, "7-Zip", "7zFM.exe"));
                candidates.Add(Path.Combine(progFilesX86, "7-Zip", "7zFM.exe"));
            }
            if (norm.Contains("winrar") || lowerId.Contains("winrar"))
            {
                candidates.Add(Path.Combine(progFiles, "WinRAR", "WinRAR.exe"));
            }
            if (norm.Contains("everything") || lowerId.Contains("everything"))
            {
                candidates.Add(Path.Combine(progFiles, "Everything", "Everything.exe"));
                candidates.Add(Path.Combine(progFilesX86, "Everything", "Everything.exe"));
            }
            if (norm.Contains("notepad++") || lowerId.Contains("notepad++"))
            {
                candidates.Add(Path.Combine(progFiles, "Notepad++", "notepad++.exe"));
                candidates.Add(Path.Combine(progFilesX86, "Notepad++", "notepad++.exe"));
            }
            if (norm.Contains("git") || lowerId.Contains("git.git"))
            {
                candidates.Add(Path.Combine(progFiles, "Git", "cmd", "git.exe"));
            }
            if (norm.Contains("node") || lowerId.Contains("nodejs"))
            {
                candidates.Add(Path.Combine(progFiles, "nodejs", "node.exe"));
            }
            if (norm.Contains("zoom") || lowerId.Contains("zoom.zoom"))
            {
                candidates.Add(Path.Combine(appData, "Zoom", "bin", "Zoom.exe"));
            }
            if (norm.Contains("obs") || lowerId.Contains("obsstudio"))
            {
                candidates.Add(Path.Combine(progFiles, "obs-studio", "bin", "64bit", "obs64.exe"));
            }

            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return true;
                }
                catch { }
            }
            return false;
        }

        public static AppItem? FindCatalogMatch(AppItem app, IEnumerable<AppItem> catalog)
        {
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                var match = catalog.FirstOrDefault(c => string.Equals(c.Id, app.Id, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                var cleanId = CleanPackageId(app.Id);
                if (!string.IsNullOrWhiteSpace(cleanId))
                {
                    match = catalog.FirstOrDefault(c =>
                        string.Equals(c.Id, cleanId, StringComparison.OrdinalIgnoreCase) ||
                        cleanId.StartsWith(c.Id, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.EndsWith(cleanId, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }

            return FindCatalogMatchForName(app.Name, catalog);
        }

        public static AppItem? FindCatalogMatchForName(string name, IEnumerable<AppItem> catalog)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // 1. Exact Name match
            var match = catalog.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. DetectionNames match
            match = catalog.FirstOrDefault(c => c.DetectionNames != null && c.DetectionNames.Any(d =>
                string.Equals(d, name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(d, StringComparison.OrdinalIgnoreCase)));
            if (match != null) return match;

            // 3. Normalized Name match (strips (x64), versions, bitness, etc.)
            var normApp = NormalizeAppName(name);
            if (!string.IsNullOrWhiteSpace(normApp) && normApp.Length >= 3)
            {
                match = catalog.FirstOrDefault(c =>
                {
                    var normCat = NormalizeAppName(c.Name);
                    return string.Equals(normCat, normApp, StringComparison.OrdinalIgnoreCase) ||
                           normApp.StartsWith(normCat, StringComparison.OrdinalIgnoreCase) ||
                           normCat.StartsWith(normApp, StringComparison.OrdinalIgnoreCase) ||
                           normApp.Contains(normCat, StringComparison.OrdinalIgnoreCase);
                });
                if (match != null) return match;
            }

            return null;
        }

        private static readonly Dictionary<string, string> KnownDomainMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Artificial Intelligence (AI Desktop Applications)
            ["9PLM9XGG6VKS"] = "https://openai.com/chatgpt/",
            ["OpenAI.ChatGPT"] = "https://openai.com/chatgpt/",
            ["Anthropic.Claude"] = "https://claude.ai/",
            ["Anthropic.ClaudeCode"] = "https://claude.ai/code",
            ["ElementLabs.LMStudio"] = "https://lmstudio.ai/",
            ["Ollama.Ollama"] = "https://ollama.com/",
            ["Jan.Jan"] = "https://jan.ai/",
            ["Anysphere.Cursor"] = "https://cursor.com/",
            ["Codeium.Windsurf"] = "https://codeium.com/windsurf",
            ["ByteDance.Trae"] = "https://www.trae.ai/",
            ["Perplexity.Perplexity"] = "https://www.perplexity.ai/",
            ["GitHub.CopilotApp"] = "https://github.com/features/copilot",
            ["nomic.gpt4all"] = "https://gpt4all.io/",
            ["pinokiocomputer.pinokio"] = "https://pinokio.computer/",
            ["Comfy.ComfyUI-Desktop"] = "https://comfy.org/",
            ["Upscayl.Upscayl"] = "https://upscayl.org/",
            ["Bin-Huang.Chatbox"] = "https://chatboxai.app/",
            ["kangfenmao.CherryStudio"] = "https://cherry-ai.com/",
            ["CloudStack.Msty"] = "https://msty.app/",
            ["Quora.Poe"] = "https://poe.com/",
            ["AhoyLabs.BackyardAI"] = "https://backyard.ai/",
            ["ChidiWilliams.Buzz"] = "https://chidiwilliams.com/",
            ["zcx960.DeepSeekDesktop"] = "https://github.com/zcx960/deepseek-desktop",
            ["Microsoft.365Copilot"] = "https://www.microsoft.com/microsoft-365/copilot",
            ["SuperUltra.superwhisper"] = "https://superwhisper.com/",

            // Browsers & Web
            ["Google.Chrome"] = "https://www.google.com/chrome/",
            ["Mozilla.Firefox"] = "https://www.mozilla.org/firefox/",
            ["Brave.Brave"] = "https://brave.com/",
            ["Microsoft.Edge"] = "https://www.microsoft.com/edge/",
            ["Opera.Opera"] = "https://www.opera.com/",
            ["Opera.OperaGX"] = "https://www.opera.com/gx",
            ["Vivaldi.Vivaldi"] = "https://vivaldi.com/",
            ["TorProject.TorBrowser"] = "https://www.torproject.org/",
            ["Floorp.Floorp"] = "https://floorp.app/",
            ["Waterfox.Waterfox"] = "https://www.waterfox.net/",
            ["Arc.Arc"] = "https://arc.net/",

            // Communication & Chat
            ["Discord.Discord"] = "https://discord.com/",
            ["Telegram.TelegramDesktop"] = "https://desktop.telegram.org/",
            ["WhatsApp.WhatsApp"] = "https://www.whatsapp.com/",
            ["Signal.Signal"] = "https://signal.org/",
            ["SlackTechnologies.Slack"] = "https://slack.com/",
            ["Microsoft.Teams"] = "https://www.microsoft.com/microsoft-teams/",
            ["Zoom.Zoom"] = "https://zoom.us/",
            ["Skype.Skype"] = "https://www.skype.com/",
            ["Element.Element"] = "https://element.io/",
            ["Viber.Viber"] = "https://www.viber.com/",

            // Media & Audio / Video
            ["VideoLAN.VLC"] = "https://www.videolan.org/vlc/",
            ["Spotify.Spotify"] = "https://www.spotify.com/",
            ["OBSProject.OBSStudio"] = "https://obsproject.com/",
            ["Audacity.Audacity"] = "https://www.audacityteam.org/",
            ["HandBrake.HandBrake"] = "https://handbrake.fr/",
            ["mpv.mpv"] = "https://mpv.io/",
            ["K-Lite.CodecPack"] = "https://codecguide.com/",
            ["Plex.Plex"] = "https://www.plex.tv/",
            ["Kodi.Kodi"] = "https://kodi.tv/",
            ["Tidal.Tidal"] = "https://tidal.com/",
            ["Deezer.Deezer"] = "https://www.deezer.com/",
            ["Foobar2000.Foobar2000"] = "https://www.foobar2000.org/",
            ["AIMP.AIMP"] = "https://www.aimp.ru/",
            ["MusicBee.MusicBee"] = "https://getmusicbee.com/",
            ["DaVinciResolve.DaVinciResolve"] = "https://www.blackmagicdesign.com/products/davinciresolve",

            // Gaming & Launchers
            ["Valve.Steam"] = "https://store.steampowered.com/",
            ["EpicGames.EpicGamesLauncher"] = "https://store.epicgames.com/",
            ["ElectronicArts.EADesktop"] = "https://www.ea.com/ea-app",
            ["Ubisoft.Connect"] = "https://ubisoftconnect.com/",
            ["GOG.Galaxy"] = "https://www.gog.com/galaxy",
            ["Battle.net"] = "https://battle.net/",
            ["PrismLauncher.PrismLauncher"] = "https://prismlauncher.org/",
            ["MoonlightGameStreamingProject.Moonlight"] = "https://moonlight-stream.org/",
            ["Parsec.Parsec"] = "https://parsec.app/",
            ["Playnite.Playnite"] = "https://playnite.link/",
            ["Razer.Synapse"] = "https://www.razer.com/synapse-3",
            ["Logitech.GHUB"] = "https://www.logitechg.com/innovation/g-hub.html",
            ["Corsair.iCUE"] = "https://www.corsair.com/icue",

            // Utilities & Tools
            ["7zip.7zip"] = "https://www.7-zip.org/",
            ["RARLab.WinRAR"] = "https://www.rarlab.com/",
            ["voidtools.Everything"] = "https://www.voidtools.com/",
            ["Notepad++.Notepad++"] = "https://notepad-plus-plus.org/",
            ["Microsoft.PowerToys"] = "https://github.com/microsoft/PowerToys",
            ["ShareX.ShareX"] = "https://getsharex.com/",
            ["Greenshot.Greenshot"] = "https://getgreenshot.org/",
            ["Lightshot.Lightshot"] = "https://app.prntscr.com/",
            ["Rufus.Rufus"] = "https://rufus.ie/",
            ["Balena.Etcher"] = "https://etcher.balena.io/",
            ["BleachBit.BleachBit"] = "https://www.bleachbit.org/",
            ["Piriform.CCleaner"] = "https://www.ccleaner.com/",
            ["RevoUninstaller.RevoUninstaller"] = "https://www.revouninstaller.com/",
            ["IObit.Uninstaller"] = "https://www.iobit.com/advanceduninstaller.php",
            ["CrystalDewWorld.CrystalDiskInfo"] = "https://crystalmark.info/",
            ["CrystalDewWorld.CrystalDiskMark"] = "https://crystalmark.info/",
            ["CPUID.CPU-Z"] = "https://www.cpuid.com/softwares/cpu-z.html",
            ["TechPowerUp.GPU-Z"] = "https://www.techpowerup.com/gpuz/",
            ["REALiX.HWiNFO"] = "https://www.hwinfo.com/",
            ["Guru3D.RTSS"] = "https://www.guru3d.com/",
            ["MSI.Afterburner"] = "https://www.msi.com/Landing/afterburner/graphics-cards",
            ["AutoHotkey.AutoHotkey"] = "https://www.autohotkey.com/",
            ["JAMSoftware.TreeSize.Free"] = "https://www.jam-software.com/treesize_free",
            ["qBittorrent.qBittorrent"] = "https://www.qbittorrent.org/",
            ["Transmission.Transmission"] = "https://transmissionbt.com/",
            ["BitTorrent.uTorrent"] = "https://www.utorrent.com/",
            ["FileZilla.FileZilla"] = "https://filezilla-project.org/",
            ["WinSCP.WinSCP"] = "https://winscp.net/",
            ["PuTTY.PuTTY"] = "https://www.putty.org/",
            ["AnyDeskSoftwareGmbH.AnyDesk"] = "https://anydesk.com/",
            ["TeamViewer.TeamViewer"] = "https://www.teamviewer.com/",
            ["RustDesk.RustDesk"] = "https://rustdesk.com/",

            // Security & Privacy
            ["Bitwarden.Bitwarden"] = "https://bitwarden.com/",
            ["1Password.1Password"] = "https://1password.com/",
            ["KeePassXCTeam.KeePassXC"] = "https://keepassxc.org/",
            ["ProtonTechnologies.ProtonVPN"] = "https://protonvpn.com/",
            ["NordVPN.NordVPN"] = "https://nordvpn.com/",
            ["Surfshark.Surfshark"] = "https://surfshark.com/",
            ["Tailscale.Tailscale"] = "https://tailscale.com/",
            ["WireGuard.WireGuard"] = "https://www.wireguard.com/",
            ["Malwarebytes.Malwarebytes"] = "https://www.malwarebytes.com/",
            ["Kaspersky.Kaspersky"] = "https://www.kaspersky.com/",
            ["ESET.NOD32"] = "https://www.eset.com/",

            // Developer & Design
            ["Microsoft.VisualStudioCode"] = "https://code.visualstudio.com/",
            ["Microsoft.VisualStudio.2022.Community"] = "https://visualstudio.microsoft.com/",
            ["Git.Git"] = "https://git-scm.com/",
            ["GitHub.GitHubDesktop"] = "https://desktop.github.com/",
            ["GitHub.cli"] = "https://cli.github.com/",
            ["Docker.DockerDesktop"] = "https://www.docker.com/products/docker-desktop/",
            ["Postman.Postman"] = "https://www.postman.com/",
            ["Insomnia.Insomnia"] = "https://insomnia.rest/",
            ["DBeaver.DBeaver.Community"] = "https://dbeaver.io/",
            ["DBeaver.DBeaver.Enterprise"] = "https://dbeaver.com/",
            ["Alacritty.Alacritty"] = "https://alacritty.org/",
            ["wez.wezterm"] = "https://wezfurlong.org/wezterm/",
            ["Neovim.Neovim"] = "https://neovim.io/",
            ["GodotEngine.GodotEngine"] = "https://godotengine.org/",
            ["Unity.UnityHub"] = "https://unity.com/",
            ["Google.AndroidStudio"] = "https://developer.android.com/studio",
            ["Figma.Figma"] = "https://www.figma.com/",
            ["BlenderFoundation.Blender"] = "https://www.blender.org/",
            ["GIMP.GIMP"] = "https://www.gimp.org/",
            ["Inkscape.Inkscape"] = "https://inkscape.org/",
            ["Krita.Krita"] = "https://krita.org/",
            ["WiresharkFoundation.Wireshark"] = "https://www.wireshark.org/",
            ["Python.Python.3.12"] = "https://www.python.org/",
            ["OpenJS.NodeJS"] = "https://nodejs.org/",
            ["Rustlang.Rustup"] = "https://www.rust-lang.org/",
            ["Golang.Go"] = "https://go.dev/",
            ["Oracle.JDK.21"] = "https://www.oracle.com/java/",
            ["JetBrains.IntelliJIDEA.Community"] = "https://www.jetbrains.com/idea/",
            ["JetBrains.PyCharm.Community"] = "https://www.jetbrains.com/pycharm/",
            ["SublimeHQ.SublimeText.4"] = "https://www.sublimetext.com/",
            ["Termius.Termius"] = "https://termius.com/",
            ["Oracle.VirtualBox"] = "https://www.virtualbox.org/",
            ["JanDeDobbeleer.OhMyPosh"] = "https://ohmyposh.dev/",
            ["Starship.Starship"] = "https://starship.rs/",
            ["Eugeny.Tabby"] = "https://tabby.sh/",
            ["Anysphere.Cursor"] = "https://www.cursor.com/",
            ["DenoLand.Deno"] = "https://deno.com/",
            ["BurntSushi.ripgrep.MSVC"] = "https://github.com/BurntSushi/ripgrep",
            ["NickeManarin.ScreenToGif"] = "https://www.screentogif.com/",
            ["Upscayl.Upscayl"] = "https://upscayl.org/",
            ["Toinane.Colorpicker"] = "https://github.com/toinane/colorpicker",
            ["HeroicGamesLauncher.HeroicGamesLauncher"] = "https://heroicgameslauncher.com/",
            ["shinchiro.mpv"] = "https://mpv.io/",
            ["Cockos.REAPER"] = "https://www.reaper.fm/",
            ["Safing.Portmaster"] = "https://safing.io/",
            ["OO-Software.ShutUp10"] = "https://www.oo-software.com/en/shutup10",
            ["zhongyang219.TrafficMonitor.Full"] = "https://github.com/zhongyang219/TrafficMonitor",
            ["WinsiderSS.SystemInformer"] = "https://systeminformer.sourceforge.io/",
            ["Open-Shell.Open-Shell-Menu"] = "https://open-shell.github.io/Open-Shell-Menu/",
            ["GNU.Octave"] = "https://octave.org/",

            // Documents & Office
            ["TheDocumentFoundation.LibreOffice"] = "https://www.libreoffice.org/",
            ["Adobe.Acrobat.Reader.64-bit"] = "https://get.adobe.com/reader/",
            ["Foxit.FoxitReader"] = "https://www.foxit.com/pdf-reader/",
            ["Calibre.Calibre"] = "https://calibre-ebook.com/",
            ["Obsidian.Obsidian"] = "https://obsidian.md/",
            ["Notion.Notion"] = "https://www.notion.so/",
            ["SumatraPDF.SumatraPDF"] = "https://www.sumatrapdfreader.org/",
            ["Anki.Anki"] = "https://apps.ankiweb.net/",
            ["Zotero.Zotero"] = "https://www.zotero.org/",
            ["DigitalScholar.Zotero"] = "https://www.zotero.org/",
            ["Miro.Miro"] = "https://miro.com/",
            ["Logseq.Logseq"] = "https://logseq.com/",
            ["Freeplane.Freeplane"] = "https://www.freeplane.org/",
            ["geeksoftwareGmbH.PDF24Creator"] = "https://tools.pdf24.org/"
        };

        public static string? ResolveOfficialUrl(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            if (!string.IsNullOrWhiteSpace(app.WebUrl) && app.WebUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return app.WebUrl;
            }

            // 1. Direct ID match in KnownDomainMap
            if (!string.IsNullOrWhiteSpace(app.Id) && KnownDomainMap.TryGetValue(app.Id, out var directUrl))
            {
                return directUrl;
            }

            // 2. Prefix / Contains match in KnownDomainMap
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                var clean = CleanPackageId(app.Id);
                foreach (var (key, val) in KnownDomainMap)
                {
                    if (key.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                        clean.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith(clean, StringComparison.OrdinalIgnoreCase))
                    {
                        return val;
                    }
                }
            }

            // 3. By App Name in KnownDomainMap
            if (!string.IsNullOrWhiteSpace(app.Name))
            {
                var norm = NormalizeAppName(app.Name);
                foreach (var (key, val) in KnownDomainMap)
                {
                    var lastPart = key.Contains('.') ? key.Split('.').Last() : key;
                    if (norm.Contains(lastPart, StringComparison.OrdinalIgnoreCase) ||
                        lastPart.Contains(norm, StringComparison.OrdinalIgnoreCase))
                    {
                        return val;
                    }
                }
            }

            // 4. Catalog match
            if (catalog != null)
            {
                var match = FindCatalogMatch(app, catalog);
                if (!string.IsNullOrWhiteSpace(match?.WebUrl))
                {
                    return match.WebUrl;
                }
            }

            // 5. Registry URLInfoAbout
            var reg = GetRegistryInfo(app.Name, app.Id);
            if (!string.IsNullOrWhiteSpace(reg?.WebUrl) && reg.WebUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return reg.WebUrl;
            }

            // 6. Derive smart domain from Id or Name
            return DeriveDomainFromId(app.Id, app.Name);
        }

        public static string? DeriveDomainFromId(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) return null;

            var cleanId = CleanPackageId(id);
            var parts = cleanId.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                var pub = parts[0].ToLowerInvariant();
                var prod = parts[1].ToLowerInvariant();

                // Specific publisher shortcuts
                if (pub.Contains("github")) return "https://github.com";
                if (pub.Contains("microsoft")) return "https://www.microsoft.com";
                if (pub.Contains("google")) return "https://www.google.com";

                // Heuristic domain
                if (prod.Length > 2 && !prod.Contains("installer") && !prod.Contains("portable"))
                {
                    return $"https://{prod}.org";
                }

                if (pub.Length > 2)
                {
                    return $"https://{pub}.com";
                }
            }

            return null;
        }

        public static string NormalizeAppName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();
            s = Regex.Replace(s, @"\s*\([^)]*\)", "");
            s = Regex.Replace(s, @"\s+(v|version\s+)?\d+(\.\d+)*.*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*-\s*[a-z]{2}-[a-z]{2}$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*-\s*(64-bit|32-bit|x64|x86)", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+(64-bit|32-bit|x64|x86)", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        public static string StripPublisherPrefix(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string[] prefixes = {
                "microsoft ", "google ", "mozilla ", "oracle ", "jetbrains ",
                "valve ", "adobe ", "msi ", "razer ", "piriform ", "iobit ",
                "videolan ", "discord ", "apple ", "blizzard ", "electronic arts ",
                "ubisoft ", "logitech ", "corsair ", "epic games ", "gog ", "cpuid ",
                "realix ", "techpowerup ", "bytedance ", "anysphere ", "codeium "
            };
            var s = name.Trim();
            var lower = s.ToLowerInvariant();
            foreach (var p in prefixes)
            {
                if (lower.StartsWith(p))
                {
                    return s.Substring(p.Length).Trim();
                }
            }
            return s;
        }

        public static string CompactAlpha(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return Regex.Replace(name.ToLowerInvariant(), @"[\s\W]", "");
        }

        public static string CleanPackageId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            var s = id.Trim();
            if (s.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(5);
                var underscore = s.IndexOf('_');
                if (underscore > 0) s = s.Substring(0, underscore);
            }
            else if (s.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
            {
                var lastSlash = s.LastIndexOf('\\');
                if (lastSlash >= 0 && lastSlash + 1 < s.Length)
                {
                    s = s.Substring(lastSlash + 1);
                }
            }
            return s.Trim();
        }

        public static string? FindLocalIconPath(string appName)
        {
            var regInfo = GetRegistryInfo(appName, "");
            if (regInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(regInfo.DisplayIcon))
                {
                    var cleaned = CleanIconPath(regInfo.DisplayIcon);
                    if (File.Exists(cleaned)) return cleaned;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    try
                    {
                        var exes = Directory.GetFiles(regInfo.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        var matchExe = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(appName, StringComparison.OrdinalIgnoreCase))
                                       ?? exes.FirstOrDefault(e => !Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
                        if (matchExe != null && File.Exists(matchExe)) return matchExe;
                    }
                    catch { }
                }
            }

            var appPathExe = ResolveFromAppPaths(appName);
            if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
            {
                return appPathExe;
            }

            return null;
        }

        public static string? ResolveFromAppPaths(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            var clean = query.Trim().Trim('\"', ' ');
            var candidates = new List<string>();

            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(clean + ".exe");
            }
            candidates.Add(clean);

            var firstWord = clean.Split(' ', '-', '_').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstWord))
            {
                if (!firstWord.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(firstWord + ".exe");
                }
                candidates.Add(firstWord);
            }

            string[] appPathRoots = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
            };

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var basePath in appPathRoots)
                {
                    foreach (var cand in candidates)
                    {
                        try
                        {
                            using var key = root.OpenSubKey($@"{basePath}\{cand}");
                            if (key != null)
                            {
                                var path = (key.GetValue("") as string) ?? (key.GetValue("Path") as string);
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    var cleaned = CleanIconPath(path);
                                    if (File.Exists(cleaned)) return cleaned;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        public static string CleanIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var clean = raw.Trim();
            var commaIdx = clean.IndexOf(',');
            if (commaIdx > 0)
            {
                clean = clean.Substring(0, commaIdx).Trim();
            }
            return clean.Trim('\"', ' ');
        }

        private static string CalculateDirectorySize(string folderPath)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                long totalBytes = 0;
                int fileCount = 0;
                foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += fi.Length;
                    if (++fileCount > 1500) break; // Speed-capped at 1500 files
                }
                if (totalBytes > 0)
                {
                    double mb = totalBytes / (1024.0 * 1024.0);
                    return mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb:0.0} MB";
                }
            }
            catch { }
            return "";
        }

        private static readonly Dictionary<string, string[]> CommonAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["VS Code"] = new[] { "Visual Studio Code", "Microsoft Visual Studio Code", "Code" },
            ["Visual Studio Code"] = new[] { "VS Code", "Microsoft Visual Studio Code", "Code" },
            ["Microsoft.VisualStudioCode"] = new[] { "Visual Studio Code", "VS Code", "Code", "XP9KHM4BK9FZ7Q" },
            ["XP9KHM4BK9FZ7Q"] = new[] { "Microsoft.VisualStudioCode", "Visual Studio Code", "VS Code" },
            ["RTSS"] = new[] { "RivaTuner Statistics Server", "RivaTuner" },
            ["RivaTuner Statistics Server"] = new[] { "RTSS", "RivaTuner" },
            ["Guru3D.RTSS"] = new[] { "RivaTuner Statistics Server", "RTSS" },
            ["Afterburner"] = new[] { "MSI Afterburner" },
            ["MSI Afterburner"] = new[] { "Afterburner" },
            ["Guru3D.Afterburner"] = new[] { "MSI Afterburner", "Afterburner" },
            ["MSI.Afterburner"] = new[] { "MSI Afterburner", "Afterburner" },
            ["Node.js"] = new[] { "NodeJS", "NodeJS LTS" },
            ["NodeJS"] = new[] { "Node.js", "NodeJS LTS" },
            ["NodeJS LTS"] = new[] { "Node.js", "NodeJS" },
            ["OpenJS.NodeJS"] = new[] { "Node.js", "NodeJS", "NodeJS LTS" },
            ["OpenJS.NodeJS.LTS"] = new[] { "Node.js", "NodeJS", "NodeJS LTS" },
            ["Python3"] = new[] { "Python 3", "Python" },
            ["Python 3.13"] = new[] { "Python", "Python 3", "Python 3.13" },
            ["Python.Python.3.13"] = new[] { "Python", "Python 3", "Python 3.13" },
            ["Python.Python.3.14"] = new[] { "Python", "Python 3", "Python 3.14" },
            ["VLC"] = new[] { "VLC media player", "VideoLAN VLC" },
            ["VLC media player"] = new[] { "VLC", "VideoLAN VLC" },
            ["VideoLAN.VLC"] = new[] { "VLC media player", "VLC", "XPDM1ZW6815MQM", "9NBLGGH4VVNH" },
            ["Telegram"] = new[] { "Telegram Desktop" },
            ["Telegram Desktop"] = new[] { "Telegram" },
            ["Telegram.TelegramDesktop"] = new[] { "Telegram Desktop", "Telegram", "9NZTWSQNTD0S" },
            ["WhatsApp"] = new[] { "WhatsApp Desktop" },
            ["WhatsApp.WhatsApp"] = new[] { "WhatsApp Desktop", "WhatsApp", "5319275A.WhatsAppDesktop", "9NKSQGP7F2NH" },
            ["Spotify"] = new[] { "Spotify Music" },
            ["Spotify.Spotify"] = new[] { "Spotify Music", "Spotify", "9NCBCSZSJRSB", "SpotifyAB.SpotifyMusic" },
            ["ChatGPT"] = new[] { "OpenAI.Codex", "OpenAI ChatGPT" },
            ["OpenAI.ChatGPT"] = new[] { "ChatGPT", "OpenAI.Codex", "9PLM9XGG6VKS" },
            ["9PLM9XGG6VKS"] = new[] { "ChatGPT", "OpenAI.Codex", "OpenAI.ChatGPT" },
            ["PowerToys"] = new[] { "Microsoft PowerToys" },
            ["Microsoft.PowerToys"] = new[] { "PowerToys", "Microsoft PowerToys", "XP89DCGQ3K6VLD" },
            ["Chrome"] = new[] { "Google Chrome" },
            ["Google.Chrome"] = new[] { "Google Chrome", "Chrome" },
            ["Firefox"] = new[] { "Mozilla Firefox" },
            ["Mozilla.Firefox"] = new[] { "Mozilla Firefox", "Firefox" },
            ["Opera"] = new[] { "Opera Stable", "Opera Browser" },
            ["Opera.Opera"] = new[] { "Opera Stable", "Opera Browser", "Opera" },
            ["Zoom"] = new[] { "Zoom Workplace", "Zoom Meetings" },
            ["Zoom.Zoom"] = new[] { "Zoom Workplace", "Zoom Meetings", "Zoom" },
            ["Steam"] = new[] { "Steam Client" },
            ["Valve.Steam"] = new[] { "Steam", "Steam Client", "Valve.SteamCMD" },
            ["Discord"] = new[] { "Discord" },
            ["Discord.Discord"] = new[] { "Discord", "XPDC2RH70K22MN" },
            ["Git"] = new[] { "Git for Windows" },
            ["Git.Git"] = new[] { "Git", "Git for Windows" },
            ["7-Zip"] = new[] { "7zip" },
            ["7zip.7zip"] = new[] { "7-Zip", "7zip" },
            ["WinRAR"] = new[] { "WinRAR archiver" },
            ["RARLab.WinRAR"] = new[] { "WinRAR", "WinRAR archiver" },
            ["Everything"] = new[] { "voidtools Everything" },
            ["voidtools.Everything"] = new[] { "Everything", "voidtools Everything" }
        };

        public static RegistryAppInfo? GetRegistryInfo(string name, string id = "")
        {
            EnsureRegistryCache();
            if (_registryCache == null || _registryCache.Count == 0) return null;

            // 1. Direct key lookups
            if (!string.IsNullOrWhiteSpace(id) && _registryCache.TryGetValue(id.ToLowerInvariant(), out var infoId))
                return infoId;

            var cleanId = CleanPackageId(id).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(cleanId) && _registryCache.TryGetValue(cleanId, out var infoCleanId))
                return infoCleanId;

            if (!string.IsNullOrWhiteSpace(name) && _registryCache.TryGetValue(name.ToLowerInvariant(), out var infoName))
                return infoName;

            var normName = NormalizeAppName(name).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normName) && _registryCache.TryGetValue(normName, out var infoNorm))
                return infoNorm;

            var stripped = StripPublisherPrefix(normName);
            if (!string.IsNullOrWhiteSpace(stripped) && _registryCache.TryGetValue(stripped, out var infoStripped))
                return infoStripped;

            var compact = CompactAlpha(name);
            if (compact.Length >= 4 && _registryCache.TryGetValue(compact, out var infoCompact))
                return infoCompact;

            // 2. Alias lookups
            var aliasCandidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(id) && CommonAliases.TryGetValue(id, out var aId)) aliasCandidates.AddRange(aId);
            if (!string.IsNullOrWhiteSpace(name) && CommonAliases.TryGetValue(name, out var aName)) aliasCandidates.AddRange(aName);
            if (!string.IsNullOrWhiteSpace(normName) && CommonAliases.TryGetValue(normName, out var aNorm)) aliasCandidates.AddRange(aNorm);

            foreach (var alias in aliasCandidates)
            {
                var lower = alias.ToLowerInvariant();
                if (_registryCache.TryGetValue(lower, out var infoAlias)) return infoAlias;
                var normAlias = NormalizeAppName(alias).ToLowerInvariant();
                if (_registryCache.TryGetValue(normAlias, out var infoNormAlias)) return infoNormAlias;
                var strippedAlias = StripPublisherPrefix(normAlias);
                if (_registryCache.TryGetValue(strippedAlias, out var infoStripAlias)) return infoStripAlias;
                var compAlias = CompactAlpha(alias);
                if (compAlias.Length >= 4 && _registryCache.TryGetValue(compAlias, out var infoCompAlias)) return infoCompAlias;
            }

            // 3. Sequential search with word-bounded phrase matching
            var targetsToMatch = new List<string>();
            if (!string.IsNullOrWhiteSpace(normName) && normName.Length >= 3) targetsToMatch.Add(normName);
            if (!string.IsNullOrWhiteSpace(stripped) && stripped.Length >= 3 && stripped != normName) targetsToMatch.Add(stripped);
            foreach (var a in aliasCandidates)
            {
                var na = NormalizeAppName(a).ToLowerInvariant();
                if (na.Length >= 3 && !targetsToMatch.Contains(na)) targetsToMatch.Add(na);
            }

            foreach (var kvp in _registryCache)
            {
                var k = kvp.Key;
                if (k.Length < 3) continue;

                foreach (var target in targetsToMatch)
                {
                    if (k.Equals(target, StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;

                    // Word-bounded containment (e.g. "Visual Studio Code" inside "Microsoft Visual Studio Code (User)")
                    if (target.Length >= 4 && (k.StartsWith(target + " ") || k.EndsWith(" " + target) || k.Contains(" " + target + " ")))
                        return kvp.Value;

                    // Reverse word-bounded containment (e.g. entry is "Opera", target is "Opera Stable")
                    if (k.Length >= 4 && (target.StartsWith(k + " ") || target.EndsWith(" " + k) || target.Contains(" " + k + " ")))
                        return kvp.Value;
                }
            }

            return null;
        }

        private static void EnsureRegistryCache()
        {
            if (_registryCache != null) return;
            _registryCache = new Dictionary<string, RegistryAppInfo>(StringComparer.OrdinalIgnoreCase);

            string[] subKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            var hives = new[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64),
                (RegistryHive.LocalMachine, RegistryView.Registry32),
                (RegistryHive.CurrentUser, RegistryView.Registry64),
                (RegistryHive.CurrentUser, RegistryView.Registry32)
            };

            foreach (var (hive, view) in hives)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var path in subKeys)
                    {
                        try
                        {
                            using var key = baseKey.OpenSubKey(path);
                            if (key == null) continue;

                            foreach (var subName in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using var appKey = key.OpenSubKey(subName);
                                    if (appKey == null) continue;

                                    var dispName = appKey.GetValue("DisplayName") as string;
                                    var dispVer = appKey.GetValue("DisplayVersion") as string;
                                    var sizeObj = appKey.GetValue("EstimatedSize");
                                    var installLoc = appKey.GetValue("InstallLocation") as string;
                                    var dispIcon = appKey.GetValue("DisplayIcon") as string;
                                    var uninstStr = (appKey.GetValue("UninstallString") as string)
                                                 ?? (appKey.GetValue("QuietUninstallString") as string);
                                    var pubStr = appKey.GetValue("Publisher") as string;
                                    var webUrl = (appKey.GetValue("URLInfoAbout") as string)
                                                 ?? (appKey.GetValue("HelpLink") as string)
                                                 ?? (appKey.GetValue("URLUpdateInfo") as string);

                                    string sizeStr = "";
                                    if (sizeObj is int sizeKb && sizeKb > 0)
                                    {
                                        double mb = sizeKb / 1024.0;
                                        sizeStr = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
                                    }

                                    var rawInstallDate = appKey.GetValue("InstallDate") as string;
                                    DateTime? installDate = null;
                                    if (!string.IsNullOrWhiteSpace(rawInstallDate) && rawInstallDate.Length == 8 &&
                                        DateTime.TryParseExact(rawInstallDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                                    {
                                        installDate = parsedDate;
                                    }
                                    else if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
                                    {
                                        try
                                        {
                                            installDate = Directory.GetCreationTime(installLoc);
                                        }
                                        catch { }
                                    }

                                    var entry = new RegistryAppInfo
                                    {
                                        DisplayName = dispName ?? subName,
                                        DisplayVersion = dispVer ?? "",
                                        Size = sizeStr,
                                        InstallLocation = installLoc,
                                        DisplayIcon = dispIcon,
                                        UninstallString = uninstStr,
                                        Publisher = pubStr,
                                        WebUrl = webUrl,
                                        InstallDate = installDate
                                    };

                                    // Index by subName & clean subName
                                    _registryCache[subName.Trim().ToLowerInvariant()] = entry;
                                    var cleanSub = CleanPackageId(subName).ToLowerInvariant();
                                    if (!string.IsNullOrWhiteSpace(cleanSub))
                                    {
                                        _registryCache[cleanSub] = entry;
                                    }

                                    if (!string.IsNullOrWhiteSpace(dispName))
                                    {
                                        string trimDisp = dispName.Trim().ToLowerInvariant();
                                        _registryCache[trimDisp] = entry;

                                        var norm = NormalizeAppName(dispName).ToLowerInvariant();
                                        if (!string.IsNullOrWhiteSpace(norm))
                                        {
                                            _registryCache[norm] = entry;
                                            var stripped = StripPublisherPrefix(norm);
                                            if (!string.IsNullOrWhiteSpace(stripped) && stripped != norm)
                                            {
                                                _registryCache[stripped] = entry;
                                            }
                                        }

                                        var alpha = CompactAlpha(dispName);
                                        if (alpha.Length >= 4 && !_registryCache.ContainsKey(alpha))
                                        {
                                            _registryCache[alpha] = entry;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }
}
