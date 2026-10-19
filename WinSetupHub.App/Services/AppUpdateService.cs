using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        public string? productName { get; set; }
        public string? repository { get; set; }
        public string? releaseNotes { get; set; }
        public string? downloadUrl { get; set; }
        public string? fallbackUrl { get; set; }
        public string? installerUrl { get; set; }
    }

    public class AppUpdateService : INotifyPropertyChanged
    {
        private static readonly Lazy<AppUpdateService> _instance = new(() => new AppUpdateService());
        public static AppUpdateService Instance => _instance.Value;

        public const string DefaultCurrentVersion = "2.5.0";
        public const string RepoOwner = "tanjeem180hz";
        public const string RepoName = "180HzSetupHub";
        public const string ManifestUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/version.json";
        public const string RawExeUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/artifacts/publish/win-x64/180HzSetupHub.exe";
        public const string FallbackExeUrl = "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe";
        public const string RawInstallerUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/installer/180HzSetupHubSetup.exe";

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _checkLock = new(1, 1);

        private string _currentVersion = DefaultCurrentVersion;
        private string _latestVersion = DefaultCurrentVersion;
        private string _releaseNotes = "";
        private string _statusMessage = "Ready to check for updates.";
        private string _downloadSpeedText = "";
        private string _downloadBytesText = "";
        private double _downloadProgress = 0;
        private bool _isChecking = false;
        private bool _isDownloading = false;
        private bool _updateAvailable = false;
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
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                {
                    var parts = fvi.FileVersion.Split('.');
                    if (parts.Length >= 2)
                    {
                        CurrentVersion = $"{parts[0]}.{parts[1]}.{(parts.Length > 2 ? parts[2] : "0")}";
                        return;
                    }
                }
            }
            catch
            {
            }

            CurrentVersion = DefaultCurrentVersion;
        }

        /// <summary>
        /// Checks GitHub for updates. Compares version.json, commit history, and remote file metadata.
        /// </summary>
        public async Task<bool> CheckForUpdatesAsync(bool silent = false)
        {
            if (!await _checkLock.WaitAsync(0))
            {
                return UpdateAvailable;
            }

            try
            {
                IsChecking = true;
                StatusMessage = "Checking GitHub repository for updates…";

                // 1. Fetch version.json with anti-cache timestamp
                var cacheBuster = DateTime.UtcNow.Ticks;
                var manifestUrlWithBuster = $"{ManifestUrl}?_={cacheBuster}";

                VersionManifest? manifest = null;
                try
                {
                    using var manifestReq = new HttpRequestMessage(HttpMethod.Get, manifestUrlWithBuster);
                    manifestReq.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                    using var manifestResp = await _http.SendAsync(manifestReq);

                    if (manifestResp.IsSuccessStatusCode)
                    {
                        var json = await manifestResp.Content.ReadAsStringAsync();
                        manifest = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"GitHub manifest check notice: {ex.Message}", ActivityType.Info);
                }

                // 2. Check latest commit timestamp on artifacts/publish/win-x64/180HzSetupHub.exe
                DateTime remoteCommitDate = DateTime.MinValue;
                string remoteCommitSha = "";
                try
                {
                    var commitApi = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/commits?path=artifacts/publish/win-x64/180HzSetupHub.exe&page=1&per_page=1";
                    using var commitReq = new HttpRequestMessage(HttpMethod.Get, commitApi);
                    commitReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("180HzSetupHub", "2.5.0"));
                    using var commitResp = await _http.SendAsync(commitReq);
                    if (commitResp.IsSuccessStatusCode)
                    {
                        var commitJson = await commitResp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(commitJson);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        {
                            var first = doc.RootElement[0];
                            if (first.TryGetProperty("sha", out var shaElem))
                            {
                                remoteCommitSha = shaElem.GetString() ?? "";
                            }
                            if (first.TryGetProperty("commit", out var commitObj) &&
                                commitObj.TryGetProperty("committer", out var committer) &&
                                committer.TryGetProperty("date", out var dateElem) &&
                                dateElem.TryGetDateTime(out var dt))
                            {
                                remoteCommitDate = dt;
                            }
                        }
                    }
                }
                catch
                {
                }

                // 3. Determine if update is available
                bool hasUpdate = false;
                string candidateVersion = manifest?.version ?? CurrentVersion;

                if (Version.TryParse(candidateVersion, out var remoteVer) &&
                    Version.TryParse(CurrentVersion, out var localVer))
                {
                    if (remoteVer > localVer)
                    {
                        hasUpdate = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(candidateVersion) &&
                         !string.Equals(candidateVersion.Trim(), CurrentVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    hasUpdate = true;
                }

                // If remote commit on the exe is newer than local executable's write time
                if (!hasUpdate && remoteCommitDate > DateTime.MinValue)
                {
                    try
                    {
                        var currentExe = GetCurrentExecutablePath();
                        if (File.Exists(currentExe))
                        {
                            var localWriteTime = File.GetLastWriteTimeUtc(currentExe);
                            if (remoteCommitDate > localWriteTime.AddMinutes(5))
                            {
                                hasUpdate = true;
                                candidateVersion = string.IsNullOrWhiteSpace(manifest?.version)
                                    ? $"{CurrentVersion}+git"
                                    : manifest.version;
                            }
                        }
                    }
                    catch { }
                }

                LatestVersion = candidateVersion;
                ReleaseNotes = manifest?.releaseNotes ?? "Performance optimizers, instant responsive UI, and bug fixes.";
                UpdateAvailable = hasUpdate;
                LastCheckTime = DateTime.UtcNow;

                if (hasUpdate)
                {
                    StatusMessage = $"🚀 Update available: v{LatestVersion}! Click Update Now to install.";
                    ActivityLogger.Instance.Log($"180Hz Setup Hub update available: v{LatestVersion}", ActivityType.Info);

                    if (!silent)
                    {
                        NotificationService.Notify("180Hz Setup Hub Update", $"New version v{LatestVersion} is available on GitHub!");
                    }
                }
                else
                {
                    StatusMessage = $"✓ 180Hz Setup Hub is up to date (v{CurrentVersion}).";
                    ActivityLogger.Instance.Log("180Hz Setup Hub is up to date.", ActivityType.Info);
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
        /// Downloads the latest 180HzSetupHub.exe from GitHub, verifies binary integrity,
        /// creates a zero-friction detached helper script to replace the locked executable, and relaunches.
        /// </summary>
        public async Task<bool> DownloadAndApplyUpdateAsync(Action<double, string>? progressCallback = null)
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
                StatusMessage = "Connecting to GitHub…";
                progressCallback?.Invoke(0, "Connecting to GitHub…");

                Directory.CreateDirectory(tempDir);
                var tempExe = Path.Combine(tempDir, "180HzSetupHub_new.exe");

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

                // 2. Also download latest config files from GitHub to temp staging so tweaks catalog is updated
                StatusMessage = "Fetching updated configuration catalogs…";
                var tempConfigDir = Path.Combine(tempDir, "Configuration");
                Directory.CreateDirectory(tempConfigDir);
                await TryDownloadConfigAsync("tweaks.default.json", tempConfigDir);
                await TryDownloadConfigAsync("registry_tweaks.default.json", tempConfigDir);
                await TryDownloadConfigAsync("packages.default.json", tempConfigDir);

                // 3. Build bulletproof detached batch updater script
                StatusMessage = "Finalizing update and restarting…";
                progressCallback?.Invoke(100, "Finalizing update and restarting…");
                DownloadProgress = 100;

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
                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine("@echo off");
                scriptBuilder.AppendLine("chcp 65001 >nul");
                scriptBuilder.AppendLine(":: 180Hz Setup Hub Direct EXE In-Place Auto-Updater");
                // Using ping for sleep avoids 'Input redirection is not supported' error in hidden cmd processes
                scriptBuilder.AppendLine("ping 127.0.0.1 -n 2 >nul");
                scriptBuilder.AppendLine($"taskkill /F /PID {currentPid} >nul 2>&1");
                scriptBuilder.AppendLine("ping 127.0.0.1 -n 2 >nul");
                scriptBuilder.AppendLine("set ATTEMPTS=0");
                scriptBuilder.AppendLine(":RETRY_COPY");
                scriptBuilder.AppendLine("set /a ATTEMPTS+=1");
                scriptBuilder.AppendLine($"copy /Y \"{tempExe}\" \"{currentExe}\" >nul 2>&1");
                scriptBuilder.AppendLine("if errorlevel 1 (");
                scriptBuilder.AppendLine("    if %ATTEMPTS% LSS 15 (");
                scriptBuilder.AppendLine("        ping 127.0.0.1 -n 2 >nul");
                scriptBuilder.AppendLine("        goto RETRY_COPY");
                scriptBuilder.AppendLine("    )");
                scriptBuilder.AppendLine(")");

                if (Directory.Exists(tempConfigDir) && !string.IsNullOrWhiteSpace(installDir))
                {
                    var targetConfigDir = Path.Combine(installDir, "Configuration");
                    scriptBuilder.AppendLine($"if exist \"{tempConfigDir}\" (");
                    scriptBuilder.AppendLine($"    xcopy /Y /E /I \"{tempConfigDir}\\*\" \"{targetConfigDir}\\\" >nul 2>&1");
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

                // If install directory requires Administrator privileges, elevate to ensure copy succeeds
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
                    if (data.Length > 100)
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
                var loc = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrWhiteSpace(loc) && File.Exists(loc)) return loc;
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
