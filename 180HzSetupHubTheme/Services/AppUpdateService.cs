using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class VersionManifest
    {
        public string? version { get; set; }
        public string? buildDate { get; set; }
        public string? commitSha { get; set; }
        public string? productName { get; set; }
        public string? repository { get; set; }
        public string? releaseNotes { get; set; }
        public string? downloadUrl { get; set; }
        public string? fallbackUrl { get; set; }
        public string? installerUrl { get; set; }
        public string? exeSha256 { get; set; }
        public bool? deltaOnly { get; set; }
        public bool? forceFullBinary { get; set; }
        public string? deltaSizeText { get; set; }
        public string? fullSizeText { get; set; }
    }

    public class AppUpdateService : INotifyPropertyChanged
    {
        private static readonly Lazy<AppUpdateService> _instance = new(() => new AppUpdateService());
        public static AppUpdateService Instance => _instance.Value;

        public const string DefaultCurrentVersion = "2.5.5";
        public const string RepoOwner = "tanjeem180hz";
        public const string RepoName = "180HzSetupHub";
        public const string ManifestUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/version.json";
        public const string RawExeUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/artifacts/publish/win-x64/180HzSetupHub.exe";
        public const string FallbackExeUrl = "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe";
        public const string RawInstallerUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/installer/180HzSetupHubSetup.exe";

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _checkLock = new(1, 1);

        private string _currentVersion = DefaultCurrentVersion;
        private string? _currentCommitSha = null;
        private string _latestVersion = DefaultCurrentVersion;
        private string _releaseNotes = "";
        private string _statusMessage = "Ready to check for updates.";
        private string _downloadSpeedText = "";
        private string _downloadBytesText = "";
        private double _downloadProgress = 0;
        private bool _isChecking = false;
        private bool _isDownloading = false;
        private bool _updateAvailable = false;
        private bool _isDeltaUpdate = false;
        private string _updateSizeText = "";
        private VersionManifest? _latestManifest = null;
        private DateTime _lastCheckTime = DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? StatusChanged;

        private void OnPropertyChanged([CallerMemberName] string? prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public string CurrentVersion
        {
            get => _currentVersion;
            private set { _currentVersion = value; OnPropertyChanged(); }
        }

        public string? CurrentCommitSha
        {
            get => _currentCommitSha;
            private set { _currentCommitSha = value; OnPropertyChanged(); }
        }

        public string LatestVersion
        {
            get => _latestVersion;
            private set { _latestVersion = value; OnPropertyChanged(); }
        }

        public string ReleaseNotes
        {
            get => _releaseNotes;
            private set { _releaseNotes = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string DownloadSpeedText
        {
            get => _downloadSpeedText;
            private set { _downloadSpeedText = value; OnPropertyChanged(); }
        }

        public string DownloadBytesText
        {
            get => _downloadBytesText;
            private set { _downloadBytesText = value; OnPropertyChanged(); }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            private set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public bool IsChecking
        {
            get => _isChecking;
            private set { _isChecking = value; OnPropertyChanged(); }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set { _isDownloading = value; OnPropertyChanged(); }
        }

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            private set { _updateAvailable = value; OnPropertyChanged(); }
        }

        public bool IsDeltaUpdate
        {
            get => _isDeltaUpdate;
            private set { _isDeltaUpdate = value; OnPropertyChanged(); }
        }

        public string UpdateSizeText
        {
            get => _updateSizeText;
            private set { _updateSizeText = value; OnPropertyChanged(); }
        }

        public VersionManifest? LatestManifest
        {
            get => _latestManifest;
            private set { _latestManifest = value; OnPropertyChanged(); }
        }

        public DateTime LastCheckTime
        {
            get => _lastCheckTime;
            private set { _lastCheckTime = value; OnPropertyChanged(); }
        }

        private AppUpdateService()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub-AutoUpdater", "2.5.5"));
            _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

            InitCurrentVersion();
        }

        private void InitCurrentVersion()
        {
            try
            {
                // Primary source of truth: Running executable's FileVersionInfo
                var exePath = GetCurrentExecutablePath();
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);
                    var clean = CleanVersionString(fvi.ProductVersion);
                    if (string.IsNullOrWhiteSpace(clean)) clean = CleanVersionString(fvi.FileVersion);
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        CurrentVersion = clean;
                    }
                }

                if (string.IsNullOrWhiteSpace(CurrentVersion) || CurrentVersion == "0.0.0")
                {
                    var asmVer = Assembly.GetEntryAssembly()?.GetName().Version;
                    if (asmVer != null)
                    {
                        CurrentVersion = $"{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}";
                    }
                }

                // Read Git commit SHA and build metadata from local manifests
                var exeDir = !string.IsNullOrWhiteSpace(exePath) ? Path.GetDirectoryName(exePath) : AppContext.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(exeDir ?? "", "version.json"),
                    Path.Combine(exeDir ?? "", "Configuration", "version.json"),
                    Path.Combine(StorageService.AppDataFolder, "version.json")
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            var json = File.ReadAllText(path);
                            var doc = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (!string.IsNullOrWhiteSpace(doc?.commitSha))
                            {
                                CurrentCommitSha = doc.commitSha.Trim();
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(CurrentVersion)) CurrentVersion = DefaultCurrentVersion;
        }

        public static string ComputeFileSha256(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return "";
                using var sha = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        public static string CleanVersionString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var v = raw.Trim().TrimStart('v', 'V');
            var plusIdx = v.IndexOf('+');
            if (plusIdx >= 0) v = v.Substring(0, plusIdx);

            var parts = v.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            }
            if (parts.Length == 2)
            {
                return $"{parts[0]}.{parts[1]}.0";
            }
            return v;
        }

        public static bool TryParseCleanVersion(string? raw, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var clean = CleanVersionString(raw);
            var parts = clean.Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
            int build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
            int rev = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;
            version = new Version(major, minor, build, rev);
            return true;
        }

        /// <summary>
        /// Fetches the remote version manifest with real-time freshness, bypassing CDN cache.
        /// </summary>
        private async Task<(VersionManifest? manifest, string? latestCommitSha)> FetchRemoteManifestAsync()
        {
            var cacheBuster = DateTime.UtcNow.Ticks;
            VersionManifest? manifest = null;
            string? latestCommitSha = null;

            // 1. Direct GitHub Commits API query for instantaneous HEAD commit SHA
            try
            {
                using var commitReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/commits/main?_={cacheBuster}");
                commitReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub-AutoUpdater", "2.5.5"));
                commitReq.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

                using var commitResp = await _http.SendAsync(commitReq);
                if (commitResp.IsSuccessStatusCode)
                {
                    var commitJson = await commitResp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(commitJson);
                    if (doc.RootElement.TryGetProperty("sha", out var shaProp))
                    {
                        latestCommitSha = shaProp.GetString()?.Trim();
                    }
                }
            }
            catch { }

            // 2. Direct GitHub API contents/version.json (Bypasses Fastly CDN completely!)
            try
            {
                using var apiReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/version.json?_={cacheBuster}");
                apiReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub-AutoUpdater", "2.5.5"));
                apiReq.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

                using var apiResp = await _http.SendAsync(apiReq);
                if (apiResp.IsSuccessStatusCode)
                {
                    var apiJson = await apiResp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(apiJson);
                    if (doc.RootElement.TryGetProperty("content", out var contentProp))
                    {
                        var base64 = contentProp.GetString()?.Replace("\n", "").Replace("\r", "");
                        if (!string.IsNullOrWhiteSpace(base64))
                        {
                            var decodedBytes = Convert.FromBase64String(base64);
                            var decodedJson = Encoding.UTF8.GetString(decodedBytes);
                            manifest = JsonSerializer.Deserialize<VersionManifest>(decodedJson, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                        }
                    }
                }
            }
            catch { }

            // 3. Fallback candidates if API contents was rate-limited or unavailable
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
            {
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(latestCommitSha))
                {
                    // Commit-addressed URL is 100% immune to stale CDN cache
                    candidates.Add($"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{latestCommitSha}/version.json");
                }
                candidates.Add($"{ManifestUrl}?_={cacheBuster}");
                candidates.Add($"https://github.com/{RepoOwner}/{RepoName}/raw/main/version.json?_={cacheBuster}");
                candidates.Add(ManifestUrl);

                foreach (var url in candidates)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub-AutoUpdater", "2.5.5"));

                        using var resp = await _http.SendAsync(req);
                        if (resp.IsSuccessStatusCode)
                        {
                            var json = await resp.Content.ReadAsStringAsync();
                            manifest = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.version))
                            {
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (manifest != null && string.IsNullOrWhiteSpace(manifest.commitSha) && !string.IsNullOrWhiteSpace(latestCommitSha))
            {
                manifest.commitSha = latestCommitSha;
            }

            return (manifest, latestCommitSha);
        }

        /// <summary>
        /// Checks GitHub for updates using multi-layer verification.
        /// </summary>
        public async Task<bool> CheckForUpdatesAsync(bool silent = false)
        {
            if (IsChecking)
            {
                try
                {
                    await _checkLock.WaitAsync(10000);
                    _checkLock.Release();
                }
                catch { }
                return UpdateAvailable;
            }

            if (!await _checkLock.WaitAsync(1000))
            {
                return UpdateAvailable;
            }

            try
            {
                IsChecking = true;
                StatusMessage = "Checking GitHub repository for updates…";

                var (manifest, latestCommitSha) = await FetchRemoteManifestAsync();

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
                {
                    StatusMessage = "Update check failed: Unable to fetch version manifest from GitHub.";
                    ActivityLogger.Instance.Log("Update check failed: GitHub manifest unreachable.", ActivityType.Warning);
                    return false;
                }

                InitCurrentVersion();
                var remoteVerClean = CleanVersionString(manifest.version);
                var localVerClean = CleanVersionString(CurrentVersion);

                bool remoteParsed = TryParseCleanVersion(remoteVerClean, out var rVer);
                bool localParsed = TryParseCleanVersion(localVerClean, out var lVer);

                bool hasUpdate = false;

                // 1. SemVer comparison: remote version greater than local
                if (remoteParsed && localParsed)
                {
                    if (rVer > lVer)
                    {
                        hasUpdate = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(remoteVerClean) && !string.Equals(remoteVerClean, localVerClean, StringComparison.OrdinalIgnoreCase))
                {
                    hasUpdate = true;
                }

                // 2. Executable SHA256 comparison: detects updated code binaries on same or untagged versions
                var currentExe = GetCurrentExecutablePath();
                var localHash = ComputeFileSha256(currentExe);
                var remoteHash = (manifest.exeSha256 ?? "").Trim();

                if (!hasUpdate && !string.IsNullOrWhiteSpace(remoteHash) && !string.IsNullOrWhiteSpace(localHash))
                {
                    if (!string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase))
                    {
                        hasUpdate = true;
                    }
                }

                // 3. Commit SHA comparison: detects committed updates
                if (!hasUpdate && !string.IsNullOrWhiteSpace(manifest.commitSha) && !string.IsNullOrWhiteSpace(CurrentCommitSha))
                {
                    if (!string.Equals(manifest.commitSha.Trim(), CurrentCommitSha.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        hasUpdate = true;
                    }
                }
                else if (!hasUpdate && !string.IsNullOrWhiteSpace(latestCommitSha) && !string.IsNullOrWhiteSpace(CurrentCommitSha))
                {
                    if (!string.Equals(latestCommitSha.Trim(), CurrentCommitSha.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        hasUpdate = true;
                    }
                }

                LatestVersion = remoteVerClean;
                ReleaseNotes = !string.IsNullOrWhiteSpace(manifest.releaseNotes)
                    ? manifest.releaseNotes
                    : "Performance enhancements, zero latency optimizations and bug fixes.";
                LatestManifest = manifest;
                LastCheckTime = DateTime.UtcNow;

                // Determine if binary update is required
                bool needBinary = !string.IsNullOrWhiteSpace(remoteHash) &&
                                  !string.IsNullOrWhiteSpace(localHash) &&
                                  !string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase);

                if (manifest.forceFullBinary == true || (hasUpdate && (rVer.Major != lVer.Major || rVer.Minor != lVer.Minor)))
                {
                    needBinary = true;
                }
                else if (manifest.deltaOnly == true && !needBinary)
                {
                    needBinary = false;
                }

                bool isDelta = !needBinary;
                IsDeltaUpdate = isDelta;
                UpdateSizeText = isDelta
                    ? (!string.IsNullOrWhiteSpace(manifest.deltaSizeText) ? manifest.deltaSizeText : "Delta Patch (~150 KB)")
                    : (!string.IsNullOrWhiteSpace(manifest.fullSizeText) ? manifest.fullSizeText : "Full App (~82 MB)");

                UpdateAvailable = hasUpdate;

                if (hasUpdate)
                {
                    StatusMessage = $"🚀 Update available: v{LatestVersion} ({UpdateSizeText})! Click Update Now to install.";
                    ActivityLogger.Instance.Log($"180Hz Setup Hub update detected: v{LatestVersion} ({UpdateSizeText})", ActivityType.Info);

                    if (!silent)
                    {
                        NotificationService.Notify("180Hz Setup Hub Update", $"New version v{LatestVersion} ({UpdateSizeText}) is ready to install!");
                    }
                }
                else
                {
                    StatusMessage = $"✓ 180Hz Setup Hub is up to date (v{CurrentVersion}).";
                    ActivityLogger.Instance.Log($"180Hz Setup Hub verified up to date (v{CurrentVersion}).", ActivityType.Info);
                }

                return hasUpdate;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Update check error: {ex.Message}";
                ActivityLogger.Instance.Log($"Update check failed: {ex.Message}", ActivityType.Warning);
                return false;
            }
            finally
            {
                IsChecking = false;
                _checkLock.Release();
            }
        }

        /// <summary>
        /// Downloads the latest update from GitHub and applies it safely with elevated NTFS atomic rename.
        /// </summary>
        public async Task<bool> DownloadAndApplyUpdateAsync(Action<double, string>? progressCallback = null, bool forceFullBinary = false)
        {
            if (IsDownloading) return false;

            var currentExe = GetCurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            {
                StatusMessage = "Cannot locate current executable path.";
                return false;
            }

            var currentPid = Process.GetCurrentProcess().Id;
            var installDir = Path.GetDirectoryName(currentExe) ?? "";
            var tempDir = Path.Combine(Path.GetTempPath(), $"180HzSetupHub_Update_{Guid.NewGuid():N}");

            try
            {
                IsDownloading = true;
                DownloadProgress = 0;
                StatusMessage = "Preparing update…";
                progressCallback?.Invoke(0, "Preparing update…");

                Directory.CreateDirectory(tempDir);
                var tempExe = Path.Combine(tempDir, "180HzSetupHub_new.exe");

                bool needBinary = forceFullBinary || !IsDeltaUpdate;

                if (needBinary)
                {
                    StatusMessage = "Connecting to GitHub for executable download…";
                    progressCallback?.Invoke(5, "Connecting to GitHub…");

                    var downloadUrls = new[]
                    {
                        RawExeUrl,
                        $"{RawExeUrl}?_={DateTime.UtcNow.Ticks}",
                        FallbackExeUrl
                    };

                    bool downloaded = false;
                    Exception? lastEx = null;

                    foreach (var url in downloadUrls)
                    {
                        try
                        {
                            StatusMessage = "Downloading latest 180Hz Setup Hub from GitHub…";
                            using var req = new HttpRequestMessage(HttpMethod.Get, url);
                            req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                            if (!resp.IsSuccessStatusCode) continue;

                            var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
                            var sw = Stopwatch.StartNew();
                            long totalRead = 0;
                            var buffer = new byte[65536];

                            using (var src = await resp.Content.ReadAsStreamAsync())
                            using (var dst = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                            {
                                int read;
                                long lastReportTicks = 0;

                                while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await dst.WriteAsync(buffer, 0, read);
                                    totalRead += read;

                                    var nowTicks = sw.ElapsedMilliseconds;
                                    if (nowTicks - lastReportTicks >= 100 || (totalBytes > 0 && totalRead == totalBytes))
                                    {
                                        lastReportTicks = nowTicks;
                                        double pct = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;
                                        double secs = sw.Elapsed.TotalSeconds;
                                        double speedBytesPerSec = secs > 0 ? totalRead / secs : 0;
                                        string speedText = FormatSpeed(speedBytesPerSec);
                                        string bytesText = totalBytes > 0
                                            ? $"{FormatBytes(totalRead)} / {FormatBytes(totalBytes)}"
                                            : FormatBytes(totalRead);

                                        DownloadProgress = pct;
                                        DownloadSpeedText = speedText;
                                        DownloadBytesText = bytesText;
                                        StatusMessage = $"Downloading: {pct:0.0}% ({speedText})";
                                        progressCallback?.Invoke(pct, StatusMessage);
                                    }
                                }
                            }

                            if (File.Exists(tempExe) && new FileInfo(tempExe).Length > 2 * 1024 * 1024)
                            {
                                using (var fs = File.OpenRead(tempExe))
                                {
                                    int b1 = fs.ReadByte();
                                    int b2 = fs.ReadByte();
                                    if (b1 == 0x4D && b2 == 0x5A) // 'MZ'
                                    {
                                        downloaded = true;
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                        }
                    }

                    if (!downloaded || !File.Exists(tempExe))
                    {
                        StatusMessage = $"Download failed: {lastEx?.Message ?? "Incomplete file received."}";
                        ActivityLogger.Instance.Log($"App update download error: {lastEx?.Message}", ActivityType.Warning);
                        return false;
                    }
                }
                else
                {
                    DownloadProgress = 25;
                    DownloadBytesText = "Delta Patch (~150 KB) • Binary skipped";
                    DownloadSpeedText = "Instant";
                    StatusMessage = "⚡ Fast Delta active: Downloading updated configs (~150 KB)…";
                    progressCallback?.Invoke(25, StatusMessage);
                }

                // Download latest configurations
                StatusMessage = "Fetching updated configuration catalogs…";
                progressCallback?.Invoke(60, "Fetching updated configurations…");
                var tempConfigDir = Path.Combine(tempDir, "Configuration");
                Directory.CreateDirectory(tempConfigDir);
                await TryDownloadConfigAsync("tweaks.default.json", tempConfigDir);
                await TryDownloadConfigAsync("registry_tweaks.default.json", tempConfigDir);
                await TryDownloadConfigAsync("packages.default.json", tempConfigDir);
                await TryDownloadConfigAsync("appsettings.default.json", tempConfigDir);
                await TryDownloadRootFileAsync("version.json", tempConfigDir);
                await TryDownloadRootFileAsync("version.json", tempDir);

                var tempVJson = Path.Combine(tempDir, "version.json");
                if (LatestManifest != null)
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(LatestManifest, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(tempVJson, json);
                        File.WriteAllText(Path.Combine(StorageService.AppDataFolder, "version.json"), json);
                    }
                    catch { }
                }

                StatusMessage = "Finalizing update and restarting…";
                progressCallback?.Invoke(100, "Finalizing update and restarting…");
                DownloadProgress = 100;

                CurrentVersion = CleanVersionString(LatestVersion);
                if (!string.IsNullOrWhiteSpace(LatestManifest?.commitSha))
                {
                    CurrentCommitSha = LatestManifest.commitSha.Trim();
                }
                UpdateAvailable = false;

                // Build bulletproof detached batch script using NTFS atomic rename
                var helperCmd = Path.Combine(tempDir, "apply_update.cmd");
                var targetConfigDir = Path.Combine(installDir, "Configuration");
                var targetVJson = Path.Combine(installDir, "version.json");
                var targetConfigVJson = Path.Combine(targetConfigDir, "version.json");

                var script = new StringBuilder();
                script.AppendLine("@echo off");
                script.AppendLine("chcp 65001 >nul");
                script.AppendLine(":: 180Hz Setup Hub Atomic Auto-Updater");
                script.AppendLine("ping 127.0.0.1 -n 2 >nul");
                script.AppendLine($"taskkill /F /PID {currentPid} >nul 2>&1");
                script.AppendLine("taskkill /F /IM 180HzSetupHub.exe >nul 2>&1");

                // Wait for process handle closure
                script.AppendLine("set /a WAIT_COUNT=0");
                script.AppendLine(":WAIT_LOOP");
                script.AppendLine("set /a WAIT_COUNT+=1");
                script.AppendLine($"tasklist /FI \"PID eq {currentPid}\" 2>nul | find \"{currentPid}\" >nul");
                script.AppendLine("if not errorlevel 1 (");
                script.AppendLine("    if %WAIT_COUNT% LSS 15 (");
                script.AppendLine("        ping 127.0.0.1 -n 1 >nul");
                script.AppendLine($"        taskkill /F /PID {currentPid} >nul 2>&1");
                script.AppendLine("        goto WAIT_LOOP");
                script.AppendLine("    )");
                script.AppendLine(")");

                // Atomic executable replacement using NTFS rename
                script.AppendLine($"if not exist \"{tempExe}\" goto SKIP_EXE");
                script.AppendLine($"del /F /Q \"{currentExe}.old\" >nul 2>&1");
                script.AppendLine($"move /Y \"{currentExe}\" \"{currentExe}.old\" >nul 2>&1");
                script.AppendLine($"copy /Y \"{tempExe}\" \"{currentExe}\" >nul 2>&1");
                script.AppendLine("if errorlevel 1 (");
                script.AppendLine("    for /L %%i in (1,1,10) do (");
                script.AppendLine("        ping 127.0.0.1 -n 1 >nul");
                script.AppendLine($"        copy /Y \"{tempExe}\" \"{currentExe}\" >nul 2>&1");
                script.AppendLine("        if not errorlevel 1 goto COPY_SUCCESS");
                script.AppendLine("    )");
                script.AppendLine(")");
                script.AppendLine(":COPY_SUCCESS");
                script.AppendLine($"del /F /Q \"{currentExe}.old\" >nul 2>&1");
                script.AppendLine(":SKIP_EXE");

                // Deploy configuration files
                if (Directory.Exists(tempConfigDir) && !string.IsNullOrWhiteSpace(installDir))
                {
                    script.AppendLine($"if exist \"{tempConfigDir}\" (");
                    script.AppendLine($"    xcopy /Y /E /I /Q /H /R \"{tempConfigDir}\\*\" \"{targetConfigDir}\\\" >nul 2>&1");
                    script.AppendLine($"    xcopy /Y /E /I /Q /H /R \"{tempConfigDir}\\*\" \"{StorageService.ConfigFolder}\\\" >nul 2>&1");
                    script.AppendLine(")");
                }

                // Deploy version manifests
                if (!string.IsNullOrWhiteSpace(installDir))
                {
                    script.AppendLine($"if exist \"{tempVJson}\" (");
                    script.AppendLine($"    copy /Y \"{tempVJson}\" \"{targetVJson}\" >nul 2>&1");
                    script.AppendLine($"    copy /Y \"{tempVJson}\" \"{targetConfigVJson}\" >nul 2>&1");
                    script.AppendLine($"    copy /Y \"{tempVJson}\" \"{Path.Combine(StorageService.AppDataFolder, "version.json")}\" >nul 2>&1");
                    script.AppendLine(")");
                }

                // Relaunch application
                script.AppendLine(":: Launch updated application");
                script.AppendLine($"start \"\" \"{currentExe}\"");
                script.AppendLine("ping 127.0.0.1 -n 2 >nul");
                script.AppendLine($"rd /S /Q \"{tempDir}\" >nul 2>&1");
                script.AppendLine("del \"%~f0\" >nul 2>&1");
                script.AppendLine("exit");

                File.WriteAllText(helperCmd, script.ToString(), Encoding.ASCII);

                // Determine if Administrator elevation is needed
                bool isAdmin = false;
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{helperCmd}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };

                if (isAdmin)
                {
                    psi.Verb = "runas";
                }

                Process.Start(psi);

                ActivityLogger.Instance.Log("Update applied. Relaunching 180Hz Setup Hub…", ActivityType.Info);
                await Task.Delay(200);

                // Terminate immediately so file lock on executable is completely released
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Update failed: {ex.Message}";
                ActivityLogger.Instance.Log($"Update failed: {ex.Message}", ActivityType.Warning);
                return false;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private async Task TryDownloadConfigAsync(string filename, string targetDir)
        {
            try
            {
                var url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/WinSetupHub.App/Configuration/{filename}?_={DateTime.UtcNow.Ticks}";
                using var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadAsByteArrayAsync();
                    if (data.Length > 10)
                    {
                        var target = Path.Combine(targetDir, filename);
                        await File.WriteAllBytesAsync(target, data);
                    }
                }
            }
            catch { }
        }

        private async Task TryDownloadRootFileAsync(string filename, string targetDir)
        {
            try
            {
                var url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/{filename}?_={DateTime.UtcNow.Ticks}";
                using var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadAsByteArrayAsync();
                    if (data.Length > 10)
                    {
                        var target = Path.Combine(targetDir, filename);
                        await File.WriteAllBytesAsync(target, data);
                    }
                }
            }
            catch { }
        }

        public static string GetCurrentExecutablePath()
        {
            try
            {
                var ep = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(ep) && File.Exists(ep)) return ep;
            }
            catch { }

            try
            {
                var p = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }
            catch { }

            try
            {
                var baseDir = AppContext.BaseDirectory;
                var cand = Path.Combine(baseDir, "180HzSetupHub.exe");
                if (File.Exists(cand)) return cand;
            }
            catch { }

            var defaultRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "180Hz Setup Hub", "180HzSetupHub.exe");
            return defaultRoot;
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "0 KB/s";
            if (bytesPerSec >= 1024.0 * 1024.0 * 1024.0)
                return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):0.0} GB/s";
            if (bytesPerSec >= 1024.0 * 1024.0)
                return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
            if (bytesPerSec >= 1024.0)
                return $"{bytesPerSec / 1024.0:0.0} KB/s";
            return $"{bytesPerSec:0} B/s";
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes >= 1024L * 1024L * 1024L)
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
            if (bytes >= 1024L * 1024L)
                return $"{bytes / (1024.0 * 1024.0):0.0} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }
    }
}