using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    /// <summary>
    /// Thin wrapper around the `winget` CLI. All calls are async and stream
    /// console output back via onOutputLine so the UI can show live progress
    /// instead of freezing on a single big call.
    /// </summary>
    public class WingetService
    {
        private static string? _resolvedWingetPath;

        public static string ResolveWingetBinaryPath()
        {
            if (!string.IsNullOrWhiteSpace(_resolvedWingetPath) && File.Exists(_resolvedWingetPath))
                return _resolvedWingetPath;

            // 1. Direct AppData location (User execution alias)
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidate = Path.Combine(localApp, "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(candidate))
            {
                _resolvedWingetPath = candidate;
                return candidate;
            }

            // 2. Scan ProgramFiles WindowsApps for DesktopAppInstaller
            try
            {
                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var winApps = Path.Combine(progFiles, "WindowsApps");
                if (Directory.Exists(winApps))
                {
                    var appInstallerDirs = Directory.GetDirectories(winApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe");
                    foreach (var dir in appInstallerDirs)
                    {
                        var exe = Path.Combine(dir, "winget.exe");
                        if (File.Exists(exe))
                        {
                            _resolvedWingetPath = exe;
                            return exe;
                        }
                    }
                }
            }
            catch { }

            // 3. Check App Paths in Registry (HKLM and HKCU)
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\winget.exe")
                             ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\winget.exe");
                if (key != null)
                {
                    var p = key.GetValue("") as string;
                    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    {
                        _resolvedWingetPath = p;
                        return p;
                    }
                }
            }
            catch { }

            // 4. Fallback to "winget"
            _resolvedWingetPath = "winget";
            return _resolvedWingetPath;
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var output = await RunWingetAsync("--version");
                return !string.IsNullOrWhiteSpace(output);
            }
            catch
            {
                return false;
            }
        }

        private static CacheEntry<List<AppItem>>? _installedAppsCache;
        private static readonly System.Threading.SemaphoreSlim _installedListSemaphore = new(1, 1);

        public static void InvalidateInstalledCache()
        {
            _installedAppsCache = null;
        }

        public async Task<List<AppItem>> GetInstalledAppsAsync(bool forceRefresh = false)
        {
            var cached = _installedAppsCache;
            if (!forceRefresh && cached != null && (DateTime.UtcNow - cached.CachedAt).TotalSeconds < 25)
            {
                return cached.Value;
            }

            await _installedListSemaphore.WaitAsync();
            try
            {
                cached = _installedAppsCache;
                if (!forceRefresh && cached != null && (DateTime.UtcNow - cached.CachedAt).TotalSeconds < 25)
                {
                    return cached.Value;
                }

                var output = await RunWingetAsync("list --accept-source-agreements");
                var parsed = ParseTable(output);
                if (parsed.Count > 0)
                {
                    _installedAppsCache = new CacheEntry<List<AppItem>>(parsed);
                }
                return parsed;
            }
            finally
            {
                _installedListSemaphore.Release();
            }
        }

        public async Task<List<AppItem>> GetUpgradableAppsAsync()
        {
            var output = await RunWingetAsync("upgrade --accept-source-agreements");
            return ParseTable(output);
        }

        public async Task<List<AppItem>> SearchAsync(string query, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<AppItem>();

            var cleanQuery = query.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(cleanQuery)) return new List<AppItem>();

            var allFound = new List<AppItem>();

            // 1. Exact quoted search across Winget Community (20,000+ packages) and Microsoft Store (~0.8s)
            var wingetOutput = await RunWingetAsync($"search \"{cleanQuery}\" --accept-source-agreements", ct: ct);
            var wingetItems = ParseTable(wingetOutput);
            if (wingetItems.Count > 0)
            {
                allFound.AddRange(wingetItems);
            }

            // 2. Intelligent Stop-Word Stripping Fallback (e.g. "vlc player" -> "vlc", "chrome browser" -> "chrome")
            if (allFound.Count < 3 && !ct.IsCancellationRequested)
            {
                var stripped = Regex.Replace(cleanQuery, @"\b(player|browser|desktop|app|apps|client|pc|software|tool|tools|official|windows|win|edition|free|pro|setup|installer|game|for pc|for windows|for win)\b", "", RegexOptions.IgnoreCase).Trim();
                stripped = Regex.Replace(stripped, @"\s+", " ").Trim();

                if (!string.IsNullOrWhiteSpace(stripped) && !string.Equals(stripped, cleanQuery, StringComparison.OrdinalIgnoreCase) && stripped.Length >= 2)
                {
                    try
                    {
                        var strippedOutput = await RunWingetAsync($"search \"{stripped}\" --accept-source-agreements", ct: ct);
                        var strippedItems = ParseTable(strippedOutput);
                        allFound.AddRange(strippedItems);
                    }
                    catch { }
                }
            }

            // 3. Keyword Token Fallback: If still few/no results and query has multiple words, search primary keyword (len >= 3)
            if (allFound.Count == 0 && cleanQuery.Contains(' ') && !ct.IsCancellationRequested)
            {
                var words = cleanQuery.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Where(w => w.Length >= 3 && !Regex.IsMatch(w, @"^(download|install|free|latest|app|soft)$", RegexOptions.IgnoreCase))
                                      .ToList();

                foreach (var word in words.Take(2))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var wordOutput = await RunWingetAsync($"search \"{word}\" --accept-source-agreements", ct: ct);
                        var wordItems = ParseTable(wordOutput);
                        allFound.AddRange(wordItems);
                        if (allFound.Count >= 5) break;
                    }
                    catch { }
                }
            }

            return PackageCatalogService.DeduplicatePackages(allFound);
        }

        private class CacheEntry<T>
        {
            public T Value { get; }
            public DateTime CachedAt { get; } = DateTime.UtcNow;
            public CacheEntry(T value) => Value = value;
            public bool IsExpired(double minutes = 30) => (DateTime.UtcNow - CachedAt).TotalMinutes > minutes;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry<AppMetadata?>> _metadataCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Threading.SemaphoreSlim _metadataSemaphore = new(2, 2);

        private static DateTime _lastSourceUpdate = DateTime.MinValue;
        private static readonly System.Threading.SemaphoreSlim _sourceUpdateSemaphore = new(1, 1);

        public async Task<bool> EnsureSourcesUpdatedAsync(bool force = false, Action<string>? onOutputLine = null)
        {
            if (!force && (DateTime.UtcNow - _lastSourceUpdate).TotalMinutes < 60)
            {
                return true;
            }

            if (!await _sourceUpdateSemaphore.WaitAsync(0))
            {
                return false;
            }

            try
            {
                onOutputLine?.Invoke("Updating repository sources to latest releases…");
                var (exitCode, _) = await RunWingetWithCodeAsync("source update", onOutputLine, timeoutMs: 90000);
                if (exitCode == 0)
                {
                    _lastSourceUpdate = DateTime.UtcNow;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                _sourceUpdateSemaphore.Release();
            }
        }

        public async Task<AppMetadata?> GetMetadataAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_metadataCache.TryGetValue(id, out var cached) && !cached.IsExpired(30)) return cached.Value;

            await _metadataSemaphore.WaitAsync();
            try
            {
                if (_metadataCache.TryGetValue(id, out cached) && !cached.IsExpired(30)) return cached.Value;

                var output = await RunWingetAsync($"show --id \"{id}\" --exact --accept-source-agreements");
                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                string? homepage = null;
                string? publisherUrl = null;
                string? supportUrl = null;
                string? licenseUrl = null;
                string? installerUrl = null;
                string? version = null;

                using var reader = new System.IO.StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Homepage:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Homepage:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) homepage = val;
                    }
                    else if (trimmed.StartsWith("Publisher Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisherUrl = val;
                    }
                    else if (trimmed.StartsWith("Publisher Support Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher Support Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) supportUrl = val;
                    }
                    else if (trimmed.StartsWith("License Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("License Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) licenseUrl = val;
                    }
                    else if (trimmed.StartsWith("Release Notes Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Release Notes Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val) && supportUrl == null) supportUrl = val;
                    }
                    else if (trimmed.StartsWith("Installer Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) installerUrl = val;
                    }
                    else if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Version:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) version = val;
                    }
                }

                // If no direct site found, extract root domain from Installer Url
                string? derivedInstallerDomain = null;
                if (homepage == null && publisherUrl == null && supportUrl == null && !string.IsNullOrWhiteSpace(installerUrl))
                {
                    try
                    {
                        var uri = new Uri(installerUrl);
                        if (uri.Host.Contains('.'))
                        {
                            derivedInstallerDomain = $"{uri.Scheme}://{uri.Host}/";
                        }
                    }
                    catch { }
                }

                var chosen = homepage ?? publisherUrl ?? supportUrl ?? licenseUrl ?? derivedInstallerDomain;
                var result = chosen != null
                    ? new AppMetadata(homepage ?? chosen, publisherUrl, supportUrl, version)
                    : null;

                if (result != null)
                {
                    _metadataCache[id] = new CacheEntry<AppMetadata?>(result);
                }
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                _metadataSemaphore.Release();
            }
        }

        public record WingetInstallerInfo(
            string? Url,
            string? Type,
            string? Sha256,
            string? Homepage = null,
            string? PublisherUrl = null,
            string? Publisher = null,
            string? Version = null,
            string? SilentSwitch = null);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry<WingetInstallerInfo?>> _installerInfoCache =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task<WingetInstallerInfo?> GetInstallerInfoAsync(string id, bool forceRefresh = false, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) return null;
            if (!forceRefresh && !string.IsNullOrWhiteSpace(id) && _installerInfoCache.TryGetValue(id, out var cached) && !cached.IsExpired(15))
                return cached.Value;

            await _metadataSemaphore.WaitAsync();
            try
            {
                if (!forceRefresh && !string.IsNullOrWhiteSpace(id) && _installerInfoCache.TryGetValue(id, out cached) && !cached.IsExpired(15))
                    return cached.Value;

                string output = "";
                if (!string.IsNullOrWhiteSpace(id))
                {
                    output = await RunWingetAsync($"show --id \"{id}\" --source winget --exact --accept-source-agreements");
                    if (string.IsNullOrWhiteSpace(output) || !output.Contains("Installer Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        output = await RunWingetAsync($"show --id \"{id}\" --exact --accept-source-agreements");
                    }
                    if (string.IsNullOrWhiteSpace(output) || !output.Contains("Installer Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var fallbackIdOutput = await RunWingetAsync($"show \"{id}\" --accept-source-agreements");
                        if (!string.IsNullOrWhiteSpace(fallbackIdOutput) && fallbackIdOutput.Contains("Installer Url:", StringComparison.OrdinalIgnoreCase))
                        {
                            output = fallbackIdOutput;
                        }
                    }
                }

                if ((string.IsNullOrWhiteSpace(output) || !output.Contains("Installer Url:", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    var fallbackNameOutput = await RunWingetAsync($"show --name \"{name}\" --source winget --accept-source-agreements");
                    if (!string.IsNullOrWhiteSpace(fallbackNameOutput))
                    {
                        output = fallbackNameOutput;
                    }
                    else
                    {
                        output = await RunWingetAsync($"show --name \"{name}\" --accept-source-agreements");
                    }
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                string? installerUrl = null;
                string? installerType = null;
                string? sha256 = null;
                string? homepage = null;
                string? publisherUrl = null;
                string? publisher = null;
                string? version = null;
                string? silentSwitch = null;

                using var reader = new System.IO.StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Installer Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) installerUrl = val;
                    }
                    else if (trimmed.StartsWith("Installer Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer Type:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) installerType = val;
                    }
                    else if (trimmed.StartsWith("Installer SHA256:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer SHA256:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) sha256 = val;
                    }
                    else if (trimmed.StartsWith("Homepage:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Homepage:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) homepage = val;
                    }
                    else if (trimmed.StartsWith("Publisher Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisherUrl = val;
                    }
                    else if (trimmed.StartsWith("Publisher:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisher = val;
                    }
                    else if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Version:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) version = val;
                    }
                    else if (trimmed.StartsWith("Silent:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Silent:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) silentSwitch = val;
                    }
                    else if (trimmed.StartsWith("SilentWithProgress:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(silentSwitch))
                    {
                        var val = trimmed.Substring("SilentWithProgress:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) silentSwitch = val;
                    }
                }

                var result = (!string.IsNullOrWhiteSpace(installerUrl) || !string.IsNullOrWhiteSpace(version) || !string.IsNullOrWhiteSpace(homepage))
                    ? new WingetInstallerInfo(installerUrl, installerType, sha256, homepage, publisherUrl, publisher, version, silentSwitch)
                    : null;

                if (result != null && !string.IsNullOrWhiteSpace(id))
                {
                    _installerInfoCache[id] = new CacheEntry<WingetInstallerInfo?>(result);
                }
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                _metadataSemaphore.Release();
            }
        }

        public Task<bool> InstallAsync(string id, string? source = null, Action<string>? onOutputLine = null, System.Threading.CancellationToken ct = default)
            => InstallAsync(id, source, name: null, onOutputLine, ct);

        public async Task<bool> InstallAsync(string id, string? source, string? name, Action<string>? onOutputLine = null, System.Threading.CancellationToken ct = default)
        {
            _ = Task.Run(() => EnsureSourcesUpdatedAsync());
            try
            {
                if (id.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("ms-windows-store://") { UseShellExecute = true });
                        onOutputLine?.Invoke("Opened Microsoft Store.");
                        return true;
                    }
                    catch
                    {
                        // Fallback to winget install
                    }
                }

                // Detect source if missing
                bool isMsStoreId = (id.Length == 12 || id.Length == 14) && !id.Contains('.') && !id.Contains('-');
                string effectiveSource = source ?? "";
                if (string.IsNullOrWhiteSpace(effectiveSource) && isMsStoreId)
                {
                    effectiveSource = "msstore";
                }
                bool isMsStore = effectiveSource.Equals("msstore", StringComparison.OrdinalIgnoreCase) || isMsStoreId;

                // MS Store apps: AppX packages managed natively by Windows Store. Do NOT pass --silent.
                if (isMsStore)
                {
                    onOutputLine?.Invoke($"Initiating Microsoft Store package installation for {name ?? id}…");
                    var successMsStore = await RunActionAsync($"install --id \"{id}\" --source msstore -e --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (successMsStore || ct.IsCancellationRequested) return successMsStore;

                    successMsStore = await RunActionAsync($"install \"{id}\" --source msstore --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (successMsStore || ct.IsCancellationRequested) return successMsStore;

                    // Fallback: Launch native Microsoft Store Product Page for 1-click install
                    onOutputLine?.Invoke($"Redirecting {name ?? id} to Microsoft Store app…");
                    try
                    {
                        Process.Start(new ProcessStartInfo($"ms-windows-store://pdp/?ProductId={id}") { UseShellExecute = true });
                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(2000, ct);
                            if (AppMetadataHelper.IsAppInstalled(new AppItem { Id = id, Name = name ?? id, Source = "msstore" }))
                            {
                                onOutputLine?.Invoke($"Verified {name ?? id} is installed on system.");
                                return true;
                            }
                        }
                        return true;
                    }
                    catch { }
                }

                var srcArg = !string.IsNullOrWhiteSpace(effectiveSource) && !effectiveSource.Equals("winget", StringComparison.OrdinalIgnoreCase)
                    ? $"--source \"{effectiveSource}\" "
                    : (!string.IsNullOrWhiteSpace(effectiveSource) ? "--source winget " : "");

                // Tier 1: Exact ID match with silent, force, disable-interactivity, and ignore-security-hash
                var success = await RunActionAsync($"install --id \"{id}\" {srcArg}-e --silent --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 2: Exact ID match WITHOUT --silent (handles packages whose installer rejects or does not support silent mode)
                success = await RunActionAsync($"install --id \"{id}\" {srcArg}-e --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 3: Non-exact ID match with silent & force
                success = await RunActionAsync($"install \"{id}\" {srcArg}--silent --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 4: Non-exact ID match without silent
                success = await RunActionAsync($"install \"{id}\" {srcArg}--force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 5: Source fallback (if specific source was specified, try without source restriction)
                if (!string.IsNullOrWhiteSpace(effectiveSource))
                {
                    success = await RunActionAsync($"install --id \"{id}\" -e --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (success || ct.IsCancellationRequested) return success;
                }

                // Tier 6: Fallback search and install by Name
                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Exact Name with silent
                    success = await RunActionAsync($"install --name \"{name}\" -e --silent --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (success || ct.IsCancellationRequested) return success;

                    // Exact Name without silent
                    success = await RunActionAsync($"install --name \"{name}\" -e --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (success || ct.IsCancellationRequested) return success;

                    // Fuzzy Name
                    success = await RunActionAsync($"install --name \"{name}\" --force --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (success || ct.IsCancellationRequested) return success;
                }

                // Tier 7: Final verification with cache invalidation and retry delay
                AppMetadataHelper.InvalidateCache();
                for (int i = 0; i < 4; i++)
                {
                    if (AppMetadataHelper.IsAppInstalled(new AppItem { Id = id, Name = name ?? id, Source = source ?? "" }))
                    {
                        onOutputLine?.Invoke($"Verified {name ?? id} is installed on system.");
                        return true;
                    }
                    await Task.Delay(500, ct);
                    AppMetadataHelper.InvalidateCache();
                }

                return false;
            }
            finally
            {
                InvalidateInstalledCache();
            }
        }

        public async Task<bool> UpgradeAsync(string id, Action<string>? onOutputLine = null, System.Threading.CancellationToken ct = default)
        {
            _ = Task.Run(() => EnsureSourcesUpdatedAsync());
            try
            {
                bool isMsStoreId = (id.Length == 12 || id.Length == 14) && !id.Contains('.') && !id.Contains('-');
                if (isMsStoreId)
                {
                    var storeSuccess = await RunActionAsync($"upgrade --id \"{id}\" --source msstore -e --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                    if (storeSuccess || ct.IsCancellationRequested) return storeSuccess;
                }

                // Tier 1: Exact ID upgrade with force & include-unknown & disable-interactivity
                var success = await RunActionAsync($"upgrade --id \"{id}\" -e --silent --force --include-unknown --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 1b: Exact ID upgrade WITHOUT silent
                success = await RunActionAsync($"upgrade --id \"{id}\" -e --force --include-unknown --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 2: Non-exact ID upgrade
                success = await RunActionAsync($"upgrade \"{id}\" --force --include-unknown --disable-interactivity --ignore-security-hash --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
                if (success || ct.IsCancellationRequested) return success;

                // Tier 3: Fallback to install with --force (Winget will pull latest version and overwrite)
                success = await InstallAsync(id, source: null, name: null, onOutputLine, ct);
                return success;
            }
            finally
            {
                InvalidateInstalledCache();
            }
        }

        public async Task<bool> UpgradeAllAsync(Action<string>? onOutputLine = null)
        {
            try
            {
                return await RunActionAsync("upgrade --all --silent --accept-package-agreements --accept-source-agreements", onOutputLine);
            }
            finally
            {
                InvalidateInstalledCache();
            }
        }

        public async Task<bool> UninstallAsync(string id, string? name = null, Action<string>? onOutputLine = null)
        {
            try
            {
                // 1. Try exact match by ID with silent mode and force
                var success = await RunActionAsync($"uninstall --id \"{id}\" -e --silent --force --accept-source-agreements", onOutputLine);
                if (!success)
                {
                    // 2. Try partial/case-insensitive match by ID with silent mode and force
                    success = await RunActionAsync($"uninstall --id \"{id}\" --silent --force --accept-source-agreements", onOutputLine);
                }
                if (!success && !string.IsNullOrWhiteSpace(name))
                {
                    // 3. Try exact match by Name with silent mode and force
                    success = await RunActionAsync($"uninstall --name \"{name}\" -e --silent --force --accept-source-agreements", onOutputLine);
                    if (!success)
                    {
                        // 4. Try partial match by Name
                        success = await RunActionAsync($"uninstall --name \"{name}\" --silent --force --accept-source-agreements", onOutputLine);
                    }
                }
                if (!success)
                {
                    // 5. Fallback: interactive mode with force (in case uninstaller displays confirmation)
                    success = await RunActionAsync($"uninstall --id \"{id}\" --force --accept-source-agreements", onOutputLine);
                }
                return success;
            }
            finally
            {
                InvalidateInstalledCache();
            }
        }

        // ---- internals ----

        private async Task<bool> RunActionAsync(string arguments, Action<string>? onOutputLine, System.Threading.CancellationToken ct = default)
        {
            var (exitCode, output) = await RunWingetWithCodeAsync(arguments, onOutputLine, timeoutMs: 300000, ct: ct);

            uint uExitCode = unchecked((uint)exitCode);
            // 0 = Success, 3010 = Reboot required / Success, 1641 = Reboot initiated
            // 0x8A15002B = Already installed / up to date, 0x8A15002C = No applicable update
            // 0x8A15001E = Package in use (already installed)
            if (exitCode == 0 || exitCode == 3010 || exitCode == 1641 ||
                uExitCode == 0x8A15002B || uExitCode == 0x8A15002C || uExitCode == 0x8A15001E)
            {
                return true;
            }

            bool isUninstall = arguments.TrimStart().StartsWith("uninstall", StringComparison.OrdinalIgnoreCase);

            if (output.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Successfully upgraded", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Successfully uninstalled", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("No applicable update found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("No available upgrade found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("already installed", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("is already installed", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("A newer version was found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Found an existing package already installed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (isUninstall)
            {
                if (output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("No package found matching", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("0x8a150014", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("0x8a150056", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private Task<string> RunWingetAsync(string arguments, Action<string>? onOutputLine = null, System.Threading.CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                var (_, output) = await RunWingetWithCodeAsync(arguments, onOutputLine, timeoutMs: 120000, ct: ct);
                return output;
            }, ct);
        }

        private Task<(int ExitCode, string Output)> RunWingetWithCodeAsync(string arguments, Action<string>? onOutputLine = null, int timeoutMs = 300000, System.Threading.CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!arguments.Contains("--accept-source-agreements", StringComparison.OrdinalIgnoreCase))
                {
                    arguments += " --accept-source-agreements";
                }

                if (!arguments.Contains("--disable-interactivity", StringComparison.OrdinalIgnoreCase) &&
                    (arguments.StartsWith("install", StringComparison.OrdinalIgnoreCase) ||
                     arguments.StartsWith("upgrade", StringComparison.OrdinalIgnoreCase) ||
                     arguments.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)))
                {
                    arguments += " --disable-interactivity";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = ResolveWingetBinaryPath(),
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var sb = new StringBuilder();

                using var reg = ct.Register(() =>
                {
                    try { process.Kill(true); } catch { }
                });

                try
                {
                    process.Start();

                    var readOutputTask = Task.Run(async () =>
                    {
                        try
                        {
                            var reader = process.StandardOutput;
                            var lineBuf = new StringBuilder();
                            char[] buf = new char[512];
                            int read;
                            while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                for (int i = 0; i < read; i++)
                                {
                                    char c = buf[i];
                                    if (c == '\r' || c == '\n')
                                    {
                                        if (lineBuf.Length > 0)
                                        {
                                            string line = lineBuf.ToString().TrimEnd();
                                            lineBuf.Clear();
                                            if (!string.IsNullOrWhiteSpace(line) && line.Trim().Length > 1)
                                            {
                                                lock (sb) sb.AppendLine(line);
                                                onOutputLine?.Invoke(line.Trim());
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lineBuf.Append(c);
                                    }
                                }
                            }
                            if (lineBuf.Length > 0)
                            {
                                string line = lineBuf.ToString().TrimEnd();
                                if (!string.IsNullOrWhiteSpace(line) && line.Trim().Length > 1)
                                {
                                    lock (sb) sb.AppendLine(line);
                                    onOutputLine?.Invoke(line.Trim());
                                }
                            }
                        }
                        catch { }
                    });

                    var readErrorTask = Task.Run(async () =>
                    {
                        try
                        {
                            var reader = process.StandardError;
                            var lineBuf = new StringBuilder();
                            char[] buf = new char[512];
                            int read;
                            while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                for (int i = 0; i < read; i++)
                                {
                                    char c = buf[i];
                                    if (c == '\r' || c == '\n')
                                    {
                                        if (lineBuf.Length > 0)
                                        {
                                            string line = lineBuf.ToString().Trim();
                                            lineBuf.Clear();
                                            if (!string.IsNullOrWhiteSpace(line))
                                            {
                                                lock (sb) sb.AppendLine(line);
                                                onOutputLine?.Invoke(line);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lineBuf.Append(c);
                                    }
                                }
                            }
                        }
                        catch { }
                    });

                    bool exited = process.WaitForExit(timeoutMs);
                    try { Task.WaitAll(new[] { readOutputTask, readErrorTask }, 1500); } catch { }

                    if (!exited || ct.IsCancellationRequested)
                    {
                        try { process.Kill(true); } catch { }
                        lock (sb) return (-1, sb.ToString());
                    }

                    lock (sb) return (process.ExitCode, sb.ToString());
                }
                catch (Exception ex)
                {
                    return (-1, $"Error running winget: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// winget prints a fixed-width, column-aligned table. We find the header row,
        /// use its column start offsets to slice every following line, and skip the
        /// "----" separator row beneath it. Supports multiple tables in `winget upgrade`.
        /// </summary>
        private static List<AppItem> ParseTable(string raw)
        {
            var items = new List<AppItem>();
            if (string.IsNullOrWhiteSpace(raw)) return items;

            // Strip ANSI escape sequences (colors, cursor movements)
            raw = Regex.Replace(raw, @"\x1B\[[^@-~]*[@-~]", "");

            var lines = raw.Replace("\r", "").Split('\n');

            int headerIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Name") && lines[i].Contains("Id") && lines[i].Contains("Version"))
                {
                    headerIndex = i;
                    break;
                }
            }
            if (headerIndex == -1 || headerIndex + 1 >= lines.Length) return items;

            int nameCol = -1, idCol = -1, versionCol = -1, matchCol = -1, availCol = -1, sourceCol = -1, versionEnd = -1;

            void ParseHeaderColumns(string hdr)
            {
                nameCol = hdr.IndexOf("Name", StringComparison.Ordinal);
                idCol = hdr.IndexOf("Id", StringComparison.Ordinal);
                versionCol = hdr.IndexOf("Version", StringComparison.Ordinal);
                matchCol = hdr.IndexOf("Match", StringComparison.Ordinal);
                availCol = hdr.IndexOf("Available", StringComparison.Ordinal);
                sourceCol = hdr.IndexOf("Source", StringComparison.Ordinal);

                versionEnd = sourceCol > 0 ? sourceCol : -1;
                if (availCol > 0 && (versionEnd <= 0 || availCol < versionEnd)) versionEnd = availCol;
                if (matchCol > 0 && (versionEnd <= 0 || matchCol < versionEnd)) versionEnd = matchCol;
            }

            ParseHeaderColumns(lines[headerIndex]);

            static string Slice(string line, int start, int end)
            {
                if (start < 0 || start >= line.Length) return "";
                int safeEnd = end < 0 ? line.Length : Math.Min(end, line.Length);
                return safeEnd <= start ? "" : line[start..safeEnd].Trim();
            }

            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("-")) continue;

                // Handle secondary table headers in `winget upgrade` output
                if (line.Contains("Name") && line.Contains("Id") && line.Contains("Version") &&
                    line.IndexOf("Name", StringComparison.Ordinal) <= 2)
                {
                    ParseHeaderColumns(line);
                    continue;
                }

                // Skip summary / prose lines printed by winget at bottom of tables
                if (trimmed.EndsWith("upgrades available.", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith("upgrade available.", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("The following packages", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("No installed package", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("No package found", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("package(s) have version numbers", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("package(s) have pins", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawVer = Slice(line, versionCol, versionEnd);
                foreach (var prefix in new[] { "Tag:", "Moniker:", "ProductCode:", "Command:", "Custom:" })
                {
                    var idx = rawVer.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        rawVer = rawVer.Substring(0, idx).Trim();
                    }
                }
                rawVer = rawVer.TrimStart('<', '>', ' ').TrimEnd('…').Trim();

                var availVer = availCol > 0 ? Slice(line, availCol, sourceCol).TrimEnd('…').Trim() : "";
                var nameStr = Slice(line, nameCol, idCol).TrimEnd('…').Trim();
                var idStr = Slice(line, idCol, versionCol).TrimEnd('…').Trim();

                // If idStr contains spaces due to long Name overflowing into idCol, recover ID from the last token
                if (idStr.Contains(' '))
                {
                    var parts = idStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        var candidateId = parts[^1];
                        if (candidateId.Contains('.') || candidateId.Length >= 4)
                        {
                            nameStr = (nameStr + " " + string.Join(" ", parts.Take(parts.Length - 1))).Trim();
                            idStr = candidateId;
                        }
                    }
                }

                // Filter out non-package rows (valid Winget/Store/ARP IDs do not contain spaces)
                if (string.IsNullOrWhiteSpace(nameStr) || string.IsNullOrWhiteSpace(idStr) || idStr.Contains(' '))
                    continue;

                var item = new AppItem
                {
                    Name = nameStr,
                    Id = idStr,
                    Version = rawVer,
                    AvailableVersion = availVer,
                    Source = sourceCol > 0 ? Slice(line, sourceCol, -1) : "winget",
                    Category = PackageCatalogService.InferCategory(idStr, nameStr)
                };

                if (string.IsNullOrWhiteSpace(item.Source)) item.Source = "winget";

                items.Add(item);
            }

            return items;
        }
    }
}
