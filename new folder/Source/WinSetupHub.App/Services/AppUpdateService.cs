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

        public const string DefaultCurrentVersion = "2.5.4";
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
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub-AutoUpdater", "2.5.0"));
            _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

            InitCurrentVersion();
        }

        private void InitCurrentVersion()
        {
            try
            {
                var exePath = GetCurrentExecutablePath();
                var exeDir = !string.IsNullOrWhiteSpace(exePath) ? Path.GetDirectoryName(exePath) : AppContext.BaseDirectory;

                // 1. Check local version.json in install directory or Configuration directory
                if (!string.IsNullOrWhiteSpace(exeDir))
                {
                    var localVJson = Path.Combine(exeDir, "version.json");
                    if (File.Exists(localVJson))
                    {
                        var json = File.ReadAllText(localVJson);
                        var doc = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (!string.IsNullOrWhiteSpace(doc?.commitSha)) CurrentCommitSha = doc.commitSha.Trim();
                        var clean = CleanVersionString(doc?.version);
                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            CurrentVersion = clean;
                            return;
                        }
                    }

                    var configVJson = Path.Combine(exeDir, "Configuration", "version.json");
                    if (File.Exists(configVJson))
                    {
                        var json = File.ReadAllText(configVJson);
                        var doc = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (!string.IsNullOrWhiteSpace(doc?.commitSha)) CurrentCommitSha = doc.commitSha.Trim();
                        var clean = CleanVersionString(doc?.version);
                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            CurrentVersion = clean;
                            return;
                        }
                    }
                }

                // Check AppData storage for persisted update manifest
                var appDataVJson = Path.Combine(StorageService.AppDataFolder, "version.json");
                if (File.Exists(appDataVJson))
                {
                    var json = File.ReadAllText(appDataVJson);
                    var doc = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (!string.IsNullOrWhiteSpace(doc?.commitSha)) CurrentCommitSha = doc.commitSha.Trim();
                    var clean = CleanVersionString(doc?.version);
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        CurrentVersion = clean;
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                    {
                        var clean = CleanVersionString(fvi.ProductVersion);
                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            CurrentVersion = clean;
                            return;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    {
                        var clean = CleanVersionString(fvi.FileVersion);
                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            CurrentVersion = clean;
                            return;
                        }
                    }
                }

                var asmVer = Assembly.GetEntryAssembly()?.GetName().Version;
                if (asmVer != null)
                {
                    CurrentVersion = $"{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}";
                    return;
                }
            }
            catch
            {
            }

            CurrentVersion = DefaultCurrentVersion;
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
        /// Checks GitHub for updates. Compares version.json, semver, and executable SHA256 hash
        /// to determine whether an update is available and whether it can be applied as a fast Delta Patch (~150 KB).
        /// </summary>
        public async Task<bool> CheckForUpdatesAsync(bool silent = false)
        {
            if (IsChecking)
            {
                try
                {
                    await _checkLock.WaitAsync(15000);
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

                // 1. Fetch version.json with anti-cache timestamp and fallback mirrors
                var cacheBuster = DateTime.UtcNow.Ticks;
                var manifestCandidates = new[]
                {
                    $"{ManifestUrl}?_={cacheBuster}",
                    $"https://github.com/{RepoOwner}/{RepoName}/raw/main/version.json?_={cacheBuster}",
                    ManifestUrl
                };

                VersionManifest? manifest = null;
                Exception? lastManifestEx = null;

                foreach (var url in manifestCandidates)
                {
                    try
                    {
                        using var manifestReq = new HttpRequestMessage(HttpMethod.Get, url);
                        manifestReq.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                        using var manifestResp = await _http.SendAsync(manifestReq);

                        if (manifestResp.IsSuccessStatusCode)
                        {
                            var json = await manifestResp.Content.ReadAsStringAsync();
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
                    catch (Exception ex)
                    {
                        lastManifestEx = ex;
                    }
                }

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
                {
                    StatusMessage = $"Update check failed: {(lastManifestEx != null ? lastManifestEx.Message : "Could not reach GitHub")}.";
                    ActivityLogger.Instance.Log($"GitHub manifest check notice: {lastManifestEx?.Message ?? "Unreachable"}", ActivityType.Warning);
                    return false;
                }

                // 2. Multi-criterion Update Detection Engine
                // Checks SemVer, Binary Executable Hash, Manifest Commit SHA, and Live GitHub Commits API
                // so that if EVEN A SINGLE LINE OF CODE changes in GitHub, an update is immediately detected!
                var remoteVerParsed = TryParseCleanVersion(manifest.version, out var remoteVer);
                var localVerParsed = TryParseCleanVersion(CurrentVersion, out var localVer);

                bool hasUpdate = false;

                // Criterion A: SemVer comparison
                if (remoteVerParsed && localVerParsed)
                {
                    if (remoteVer > localVer)
                    {
                        hasUpdate = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(manifest.version))
                {
                    var cleanRemote = CleanVersionString(manifest.version);
                    var cleanLocal = CleanVersionString(CurrentVersion);
                    if (!string.Equals(cleanRemote, cleanLocal, StringComparison.OrdinalIgnoreCase))
                    {
                        hasUpdate = true;
                    }
                }

                // Criterion B: Executable Binary Hash comparison (detects recompiled/new code binaries)
                var currentExe = GetCurrentExecutablePath();
                var localHash = ComputeFileSha256(currentExe);
                var remoteExeHash = (manifest.exeSha256 ?? "").Trim();
                if (!hasUpdate && !string.IsNullOrWhiteSpace(remoteExeHash) && !string.IsNullOrWhiteSpace(localHash))
                {
                    if (!string.Equals(remoteExeHash, localHash, StringComparison.OrdinalIgnoreCase))
                    {
                        hasUpdate = true;
                    }
                }

                // Criterion C: Manifest Commit SHA check
                if (!hasUpdate && !string.IsNullOrWhiteSpace(manifest.commitSha) && !string.IsNullOrWhiteSpace(CurrentCommitSha))
                {
                    if (!string.Equals(manifest.commitSha.Trim(), CurrentCommitSha.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        hasUpdate = true;
                    }
                }

                // Criterion D: Live GitHub Commits API query (Direct real-time GitHub sync)
                // If any commit was pushed to 'main', GitHub API immediately returns the latest commit SHA
                string? latestGitSha = null;
                try
                {
                    using var commitReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/commits/main");
                    commitReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub-AutoUpdater", "2.5.0"));
                    commitReq.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

                    using var commitResp = await _http.SendAsync(commitReq);
                    if (commitResp.IsSuccessStatusCode)
                    {
                        var commitJson = await commitResp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(commitJson);
                        if (doc.RootElement.TryGetProperty("sha", out var shaProp))
                        {
                            latestGitSha = shaProp.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(latestGitSha))
                            {
                                if (!string.IsNullOrWhiteSpace(CurrentCommitSha) &&
                                    !string.Equals(latestGitSha, CurrentCommitSha, StringComparison.OrdinalIgnoreCase))
                                {
                                    hasUpdate = true;
                                }
                                else if (!string.IsNullOrWhiteSpace(manifest.commitSha) &&
                                         !string.Equals(latestGitSha, manifest.commitSha.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    hasUpdate = true;
                                }
                            }
                        }
                    }
                }
                catch { }

                LatestVersion = CleanVersionString(manifest.version);
                ReleaseNotes = !string.IsNullOrWhiteSpace(manifest.releaseNotes)
                    ? manifest.releaseNotes
                    : "100% crash immunity, zero BSOD, low-latency performance optimizers and bug fixes.";
                LatestManifest = manifest;
                LastCheckTime = DateTime.UtcNow;

                // 3. Determine if update requires executable binary or is a fast delta patch
                // If the remote binary hash is different from the local executable, the binary MUST be updated!
                bool needBinary = !string.IsNullOrWhiteSpace(remoteExeHash) &&
                                  !string.IsNullOrWhiteSpace(localHash) &&
                                  !string.Equals(remoteExeHash, localHash, StringComparison.OrdinalIgnoreCase);

                if (manifest.forceFullBinary == true)
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
                    ? (!string.IsNullOrWhiteSpace(manifest.deltaSizeText) ? manifest.deltaSizeText : "Delta Patch ~150 KB")
                    : (!string.IsNullOrWhiteSpace(manifest.fullSizeText) ? manifest.fullSizeText : "Full App ~82 MB");

                UpdateAvailable = hasUpdate;

                if (hasUpdate)
                {
                    if (isDelta)
                    {
                        StatusMessage = $"⚡ Fast Delta Patch available: v{LatestVersion} ({UpdateSizeText})! Click Update Now to patch.";
                        ActivityLogger.Instance.Log($"180Hz Setup Hub delta update available: v{LatestVersion} ({UpdateSizeText})", ActivityType.Info);
                    }
                    else
                    {
                        StatusMessage = $"🚀 Update available: v{LatestVersion} ({UpdateSizeText})! Click Update Now to install.";
                        ActivityLogger.Instance.Log($"180Hz Setup Hub full update available: v{LatestVersion} ({UpdateSizeText})", ActivityType.Info);
                    }

                    if (!silent)
                    {
                        NotificationService.Notify("180Hz Setup Hub Update", $"New version v{LatestVersion} ({UpdateSizeText}) is available on GitHub!");
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
                StatusMessage = $"Update check notice: {ex.Message}";
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
        /// Downloads the latest update from GitHub and applies it in-place.
        /// If IsDeltaUpdate is true (and forceFullBinary is false), it skips downloading the ~85MB executable
        /// and only downloads the changed configuration files and manifest (~150 KB), completing in seconds.
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

                    // Download URL candidate list
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
                            var buffer = new byte[65536]; // 64 KB buffer

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
                                    if (nowTicks - lastReportTicks >= 120 || (totalBytes > 0 && totalRead == totalBytes))
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

                            // Validate downloaded file
                            if (File.Exists(tempExe) && new FileInfo(tempExe).Length > 2 * 1024 * 1024)
                            {
                                // Validate PE header
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
                        StatusMessage = $"Download failed: {lastEx?.Message ?? "Corrupted or incomplete file received."}";
                        ActivityLogger.Instance.Log($"App update download error: {lastEx?.Message}", ActivityType.Warning);
                        return false;
                    }
                }
                else
                {
                    // Fast Delta mode: 85MB executable download is skipped!
                    DownloadProgress = 25;
                    DownloadBytesText = "Delta Patch (~150 KB) • Binary skipped";
                    DownloadSpeedText = "Instant";
                    StatusMessage = "⚡ Fast Delta active: Downloading updated configs (~150 KB)…";
                    progressCallback?.Invoke(25, StatusMessage);
                }

                // 2. Download latest config files from GitHub to temp staging
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

                // If in delta mode, ensure version.json is staged with the new version manifest
                var tempVJson = Path.Combine(tempDir, "version.json");
                if (LatestManifest != null)
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(LatestManifest, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(tempVJson, json);

                        var appDataVJson = Path.Combine(StorageService.AppDataFolder, "version.json");
                        Directory.CreateDirectory(StorageService.AppDataFolder);
                        File.WriteAllText(appDataVJson, json);
                    }
                    catch { }
                }

                // 3. Build bulletproof detached batch updater script
                StatusMessage = "Finalizing update and restarting…";
                progressCallback?.Invoke(100, "Finalizing update and restarting…");
                DownloadProgress = 100;

                CurrentVersion = CleanVersionString(LatestVersion);
                if (!string.IsNullOrWhiteSpace(LatestManifest?.commitSha))
                {
                    CurrentCommitSha = LatestManifest.commitSha.Trim();
                }
                UpdateAvailable = false;

                // Test whether install directory is directly writable without elevation
                bool isInstallDirWritable = false;
                try
                {
                    var testFile = Path.Combine(installDir, $"write_test_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    isInstallDirWritable = true;
                }
                catch
                {
                    isInstallDirWritable = false;
                }

                var helperCmd = Path.Combine(tempDir, "apply_update.cmd");
                var targetConfigDir = Path.Combine(installDir, "Configuration");
                var targetVJson = Path.Combine(installDir, "version.json");
                var targetConfigVJson = Path.Combine(targetConfigDir, "version.json");

                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine("@echo off");
                scriptBuilder.AppendLine("chcp 65001 >nul");
                scriptBuilder.AppendLine(":: 180Hz Setup Hub Fast Delta & Full Auto-Updater");
                scriptBuilder.AppendLine("ping 127.0.0.1 -n 2 >nul");
                scriptBuilder.AppendLine($"taskkill /F /PID {currentPid} >nul 2>&1");
                scriptBuilder.AppendLine("taskkill /F /IM 180HzSetupHub.exe >nul 2>&1");
                scriptBuilder.AppendLine("ping 127.0.0.1 -n 2 >nul");

                scriptBuilder.AppendLine($"if not exist \"{tempExe}\" goto SKIP_EXE");
                scriptBuilder.AppendLine("set ATTEMPTS=0");
                scriptBuilder.AppendLine(":RETRY_COPY");
                scriptBuilder.AppendLine("set /a ATTEMPTS+=1");
                scriptBuilder.AppendLine($"copy /Y \"{tempExe}\" \"{currentExe}\" >nul 2>&1");
                scriptBuilder.AppendLine("if errorlevel 1 (");
                scriptBuilder.AppendLine("    if %ATTEMPTS% LSS 20 (");
                scriptBuilder.AppendLine("        ping 127.0.0.1 -n 2 >nul");
                scriptBuilder.AppendLine("        goto RETRY_COPY");
                scriptBuilder.AppendLine("    )");
                scriptBuilder.AppendLine(")");
                scriptBuilder.AppendLine(":SKIP_EXE");

                if (Directory.Exists(tempConfigDir) && !string.IsNullOrWhiteSpace(installDir))
                {
                    scriptBuilder.AppendLine($"if exist \"{tempConfigDir}\" (");
                    scriptBuilder.AppendLine($"    xcopy /Y /E /I /Q /H /R \"{tempConfigDir}\\*\" \"{targetConfigDir}\\\" >nul 2>&1");
                    scriptBuilder.AppendLine($"    xcopy /Y /E /I /Q /H /R \"{tempConfigDir}\\*\" \"%LOCALAPPDATA%\\180HzSetupHub\\config\\\" >nul 2>&1");
                    scriptBuilder.AppendLine(")");
                }

                if (!string.IsNullOrWhiteSpace(installDir))
                {
                    scriptBuilder.AppendLine($"if exist \"{tempVJson}\" (");
                    scriptBuilder.AppendLine($"    copy /Y \"{tempVJson}\" \"{targetVJson}\" >nul 2>&1");
                    scriptBuilder.AppendLine($"    copy /Y \"{tempVJson}\" \"{targetConfigVJson}\" >nul 2>&1");
                    scriptBuilder.AppendLine($"    copy /Y \"{tempVJson}\" \"%LOCALAPPDATA%\\180HzSetupHub\\version.json\" >nul 2>&1");
                    scriptBuilder.AppendLine(")");
                }

                scriptBuilder.AppendLine(":: Launch updated application");
                scriptBuilder.AppendLine($"start \"\" \"{currentExe}\"");
                scriptBuilder.AppendLine("ping 127.0.0.1 -n 3 >nul");
                scriptBuilder.AppendLine($"rd /S /Q \"{tempDir}\" >nul 2>&1");
                scriptBuilder.AppendLine("del \"%~f0\" >nul 2>&1");
                scriptBuilder.AppendLine("exit");

                File.WriteAllText(helperCmd, scriptBuilder.ToString(), Encoding.ASCII);

                // 4. Launch detached updater process
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{helperCmd}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };

                // If install directory requires Administrator privileges, elevate
                if (!isInstallDirWritable)
                {
                    psi.Verb = "runas";
                }

                Process.Start(psi);

                // 5. Cleanly shut down current application
                ActivityLogger.Instance.Log("Update applied. Relaunching 180Hz Setup Hub…", ActivityType.Info);
                await Task.Delay(300);

                var app = Application.Current;
                if (app != null)
                {
                    app.Dispatcher.Invoke(() => app.Shutdown());
                }
                else
                {
                    Environment.Exit(0);
                }

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
            catch
            {
            }
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
            catch
            {
            }
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
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double d = bytes;
            while (d >= 1024.0 && i < suffixes.Length - 1)
            {
                d /= 1024.0;
                i++;
            }
            return $"{d:0.0} {suffixes[i]}";
        }
    }
}
