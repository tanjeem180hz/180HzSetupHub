using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public enum DownloadState
    {
        Queued,
        Downloading,
        Paused,
        Installing,
        Completed,
        Error,
        Cancelled
    }

    public class DownloadProgressInfo
    {
        public AppItem App { get; set; } = null!;
        public int QueueIndex { get; set; }
        public int QueueTotal { get; set; }
        public DownloadState State { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double Percentage { get; set; }
        public string SpeedFormatted { get; set; } = "0 MB/s";
        public string EtaFormatted { get; set; } = "Calculating…";
        public string SizeFormatted { get; set; } = "";
        public string StatusMessage { get; set; } = "";
        public string? ErrorMessage { get; set; }
    }

    public class DownloadManagerService
    {
        private static readonly Lazy<DownloadManagerService> _instance = new(() => new DownloadManagerService());
        public static DownloadManagerService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly WingetService _winget = new();
        private readonly string _downloadDir;

        private readonly List<AppItem> _queue = new();
        private readonly object _lock = new();

        private CancellationTokenSource? _currentCts;
        private TaskCompletionSource<bool>? _resumeTcs;
        private volatile bool _isPaused;
        private volatile bool _isSkipping;
        private volatile bool _isCancelled;
        private volatile bool _isQueueRunning;
        private int _currentIndex;

        public event Action<DownloadProgressInfo>? ProgressChanged;
        public event Action? QueueCompleted;

        public bool IsRunning => _isQueueRunning;
        public bool IsPaused => _isPaused;
        public AppItem? CurrentApp
        {
            get
            {
                lock (_lock)
                {
                    return (_currentIndex >= 0 && _currentIndex < _queue.Count) ? _queue[_currentIndex] : null;
                }
            }
        }

        public DownloadManagerService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(15)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

            _downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "180HzSetupHub", "Downloads");

            try
            {
                if (!Directory.Exists(_downloadDir))
                {
                    Directory.CreateDirectory(_downloadDir);
                }
            }
            catch { }
        }

        public void Enqueue(AppItem item)
        {
            EnqueueRange(new[] { item });
        }

        public void EnqueueRange(IEnumerable<AppItem> items)
        {
            lock (_lock)
            {
                foreach (var item in items)
                {
                    if (!_queue.Contains(item) && !item.IsInstalled)
                    {
                        item.Status = "Queued";
                        item.IsBusy = true;
                        _queue.Add(item);
                    }
                }

                if (!_isQueueRunning && _queue.Count > 0)
                {
                    _isQueueRunning = true;
                    _isCancelled = false;
                    _currentIndex = 0;
                    Task.Run(ProcessQueueAsync);
                }
            }
        }

        public void Pause()
        {
            if (!_isQueueRunning || _isPaused) return;

            _isPaused = true;
            _resumeTcs = new TaskCompletionSource<bool>();
            _currentCts?.Cancel();

            var current = CurrentApp;
            if (current != null)
            {
                current.Status = "Paused";
                ProgressChanged?.Invoke(new DownloadProgressInfo
                {
                    App = current,
                    QueueIndex = _currentIndex + 1,
                    QueueTotal = _queue.Count,
                    State = DownloadState.Paused,
                    StatusMessage = "Download paused. Partial bytes preserved on disk.",
                    SpeedFormatted = "0 MB/s",
                    EtaFormatted = "Paused"
                });
            }
        }

        public void Resume()
        {
            if (!_isQueueRunning || !_isPaused) return;

            _isPaused = false;
            _resumeTcs?.TrySetResult(true);
        }

        public void SkipCurrent()
        {
            if (!_isQueueRunning) return;

            _isSkipping = true;
            _isPaused = false;
            _resumeTcs?.TrySetResult(false);
            _currentCts?.Cancel();
        }

        public void CancelAll()
        {
            lock (_lock)
            {
                _isCancelled = true;
                _isPaused = false;
                _resumeTcs?.TrySetResult(false);
                _currentCts?.Cancel();

                foreach (var item in _queue)
                {
                    if (!item.IsInstalled)
                    {
                        item.Status = "Install";
                        item.IsBusy = false;
                    }
                }
                _queue.Clear();
                _isQueueRunning = false;
            }

            QueueCompleted?.Invoke();
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                AppItem? item = null;
                int idx = 0;
                int total = 0;

                lock (_lock)
                {
                    if (_isCancelled || _currentIndex >= _queue.Count)
                    {
                        _isQueueRunning = false;
                        break;
                    }

                    item = _queue[_currentIndex];
                    idx = _currentIndex + 1;
                    total = _queue.Count;
                }

                if (item == null) break;

                _isSkipping = false;
                _isPaused = false;

                try
                {
                    await ProcessSingleAppAsync(item, idx, total);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Error processing {item.Name}: {ex.Message}", ActivityType.Error);
                }

                lock (_lock)
                {
                    _currentIndex++;
                }
            }

            lock (_lock)
            {
                _isQueueRunning = false;
            }

            QueueCompleted?.Invoke();
        }

        private async Task ProcessSingleAppAsync(AppItem app, int queueIndex, int queueTotal)
        {
            app.IsBusy = true;
            app.Status = "Preparing…";

            Notify(new DownloadProgressInfo
            {
                App = app,
                QueueIndex = queueIndex,
                QueueTotal = queueTotal,
                State = DownloadState.Downloading,
                StatusMessage = $"Resolving authentic package for {app.Name}…",
                SpeedFormatted = "0 MB/s",
                EtaFormatted = "Connecting…"
            });

            // Handle Microsoft Store app itself
            if (app.Id.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-windows-store://") { UseShellExecute = true });
                    app.Status = "Installed";
                    app.IsInstalled = true;
                    app.IsBusy = false;
                    app.IsSelected = false;
                    Notify(new DownloadProgressInfo
                    {
                        App = app,
                        QueueIndex = queueIndex,
                        QueueTotal = queueTotal,
                        State = DownloadState.Completed,
                        Percentage = 100,
                        StatusMessage = "Launched Microsoft Store."
                    });
                    return;
                }
                catch { }
            }

            // Attempt to retrieve installer URL and type from winget manifest
            var installerInfo = await _winget.GetInstallerInfoAsync(app.Id);

            if (installerInfo != null && !string.IsNullOrWhiteSpace(installerInfo.Url) && installerInfo.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bool downloaded = await DownloadWithResumeAsync(app, installerInfo.Url, installerInfo.Type, queueIndex, queueTotal);
                if (downloaded)
                {
                    await InstallDownloadedPackageAsync(app, installerInfo, queueIndex, queueTotal);
                    return;
                }
            }

            // Fallback to direct Winget install with live output parsing
            await InstallViaWingetDirectAsync(app, queueIndex, queueTotal);
        }

        private async Task<bool> DownloadWithResumeAsync(AppItem app, string url, string? installerType, int queueIndex, int queueTotal)
        {
            string ext = GetExtensionForType(installerType, url);
            string safeId = MakeSafeFilename(app.Id);
            string partPath = Path.Combine(_downloadDir, $"{safeId}_setup.part");
            string finalPath = Path.Combine(_downloadDir, $"{safeId}_setup{ext}");

            while (!_isCancelled && !_isSkipping)
            {
                if (_isPaused && _resumeTcs != null)
                {
                    await _resumeTcs.Task;
                    if (_isCancelled || _isSkipping) return false;
                }

                _currentCts = new CancellationTokenSource();
                var ct = _currentCts.Token;

                try
                {
                    long existingBytes = 0;
                    if (File.Exists(partPath))
                    {
                        existingBytes = new FileInfo(partPath).Length;
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (existingBytes > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    bool isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                    bool isOk = response.StatusCode == HttpStatusCode.OK;

                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // File might be already complete or server range invalid; restart fresh
                        existingBytes = 0;
                        if (File.Exists(partPath)) File.Delete(partPath);
                        continue;
                    }

                    if (!isPartial && !isOk)
                    {
                        throw new HttpRequestException($"Server returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    }

                    long totalBytes = 0;
                    if (isPartial)
                    {
                        totalBytes = existingBytes + (response.Content.Headers.ContentLength ?? 0);
                    }
                    else
                    {
                        existingBytes = 0;
                        totalBytes = response.Content.Headers.ContentLength ?? 0;
                    }

                    var fileMode = isPartial && existingBytes > 0 ? FileMode.Append : FileMode.Create;

                    using (var fs = new FileStream(partPath, fileMode, FileAccess.Write, FileShare.ReadWrite))
                    using (var stream = await response.Content.ReadAsStreamAsync(ct))
                    {
                        byte[] buffer = new byte[64 * 1024];
                        long downloadedTotal = existingBytes;
                        var stopwatch = Stopwatch.StartNew();
                        long lastBytesSample = downloadedTotal;
                        var lastSampleTime = stopwatch.Elapsed;

                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, bytesRead, ct);
                            downloadedTotal += bytesRead;

                            var elapsed = stopwatch.Elapsed;
                            var sampleDuration = (elapsed - lastSampleTime).TotalSeconds;

                            if (sampleDuration >= 0.2 || downloadedTotal == totalBytes)
                            {
                                double speedBytesPerSec = sampleDuration > 0
                                    ? (downloadedTotal - lastBytesSample) / sampleDuration
                                    : 0;

                                lastBytesSample = downloadedTotal;
                                lastSampleTime = elapsed;

                                double percent = totalBytes > 0 ? (double)downloadedTotal / totalBytes * 100.0 : 0.0;
                                percent = Math.Clamp(percent, 0, 100);

                                double remainingBytes = Math.Max(0, totalBytes - downloadedTotal);
                                double etaSec = speedBytesPerSec > 0 ? remainingBytes / speedBytesPerSec : 0;

                                string speedStr = FormatSpeed(speedBytesPerSec);
                                string etaStr = FormatEta(etaSec);
                                string sizeStr = totalBytes > 0
                                    ? $"({FormatBytes(downloadedTotal)} / {FormatBytes(totalBytes)})"
                                    : $"({FormatBytes(downloadedTotal)})";

                                app.Status = $"Downloading {percent:0}%";

                                Notify(new DownloadProgressInfo
                                {
                                    App = app,
                                    QueueIndex = queueIndex,
                                    QueueTotal = queueTotal,
                                    State = DownloadState.Downloading,
                                    BytesDownloaded = downloadedTotal,
                                    TotalBytes = totalBytes,
                                    Percentage = percent,
                                    SpeedFormatted = speedStr,
                                    EtaFormatted = etaStr,
                                    SizeFormatted = sizeStr,
                                    StatusMessage = $"Downloading {app.Name}…"
                                });
                            }
                        }
                    }

                    // Download completed successfully
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(partPath, finalPath);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    if (_isSkipping || _isCancelled)
                    {
                        return false;
                    }

                    if (_isPaused)
                    {
                        // Paused cleanly; wait for resume loop
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Download interrupted for {app.Name}: {ex.Message}", ActivityType.Warning);

                    app.Status = "Connection Error";
                    Notify(new DownloadProgressInfo
                    {
                        App = app,
                        QueueIndex = queueIndex,
                        QueueTotal = queueTotal,
                        State = DownloadState.Error,
                        StatusMessage = $"Connection interrupted: {ex.Message}. Download saved. Click Resume to continue.",
                        SpeedFormatted = "0 MB/s",
                        EtaFormatted = "Interrupted",
                        ErrorMessage = ex.Message
                    });

                    _isPaused = true;
                    _resumeTcs = new TaskCompletionSource<bool>();
                    await _resumeTcs.Task;

                    if (_isCancelled || _isSkipping) return false;
                }
            }

            return false;
        }

        private async Task InstallDownloadedPackageAsync(AppItem app, WingetService.WingetInstallerInfo info, int queueIndex, int queueTotal)
        {
            string ext = GetExtensionForType(info.Type, info.Url ?? "");
            string safeId = MakeSafeFilename(app.Id);
            string finalPath = Path.Combine(_downloadDir, $"{safeId}_setup{ext}");

            if (!File.Exists(finalPath))
            {
                await InstallViaWingetDirectAsync(app, queueIndex, queueTotal);
                return;
            }

            app.Status = "Installing…";
            Notify(new DownloadProgressInfo
            {
                App = app,
                QueueIndex = queueIndex,
                QueueTotal = queueTotal,
                State = DownloadState.Installing,
                Percentage = 100,
                StatusMessage = $"Installing {app.Name} silently…",
                SpeedFormatted = "Ready",
                EtaFormatted = "Installing…"
            });

            bool success = false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string lowerType = (info.Type ?? "").ToLowerInvariant();

                if (lowerType == "wix" || lowerType == "msi" || ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = "msiexec.exe";
                    psi.Arguments = $"/i \"{finalPath}\" /quiet /norestart";
                }
                else if (lowerType == "inno")
                {
                    psi.FileName = finalPath;
                    psi.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
                }
                else if (lowerType == "nullsoft" || lowerType == "nsis")
                {
                    psi.FileName = finalPath;
                    psi.Arguments = "/S";
                }
                else if (lowerType == "burn")
                {
                    psi.FileName = finalPath;
                    psi.Arguments = "/quiet /norestart";
                }
                else
                {
                    psi.FileName = finalPath;
                    psi.Arguments = "/silent /verysilent /quiet /qn /s";
                }

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    success = (proc.ExitCode == 0 || proc.ExitCode == 3010);
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Direct installer failed for {app.Name}: {ex.Message}. Falling back to Winget.", ActivityType.Warning);
            }

            if (!success)
            {
                // Fallback to winget install command
                success = await _winget.InstallAsync(app.Id, app.Source);
            }

            FinalizeAppStatus(app, success, queueIndex, queueTotal);
        }

        private async Task InstallViaWingetDirectAsync(AppItem app, int queueIndex, int queueTotal)
        {
            app.Status = "Installing…";
            Notify(new DownloadProgressInfo
            {
                App = app,
                QueueIndex = queueIndex,
                QueueTotal = queueTotal,
                State = DownloadState.Downloading,
                StatusMessage = $"Downloading & Installing {app.Name} via Winget…",
                SpeedFormatted = "Live",
                EtaFormatted = "In Progress…"
            });

            var percentRegex = new Regex(@"(\d{1,3})%", RegexOptions.Compiled);
            var speedRegex = new Regex(@"([\d\.]+\s*(?:KB|MB|GB)/s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var etaRegex = new Regex(@"--\s*(\d+[smh])", RegexOptions.Compiled);

            bool success = await _winget.InstallAsync(app.Id, app.Source, line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                double percent = 0;
                var matchPct = percentRegex.Match(line);
                if (matchPct.Success && double.TryParse(matchPct.Groups[1].Value, out var p))
                {
                    percent = p;
                }

                string speed = "Active";
                var matchSpeed = speedRegex.Match(line);
                if (matchSpeed.Success)
                {
                    speed = matchSpeed.Groups[1].Value;
                }

                string eta = "Working…";
                var matchEta = etaRegex.Match(line);
                if (matchEta.Success)
                {
                    eta = $"{matchEta.Groups[1].Value} remaining";
                }

                Notify(new DownloadProgressInfo
                {
                    App = app,
                    QueueIndex = queueIndex,
                    QueueTotal = queueTotal,
                    State = DownloadState.Downloading,
                    Percentage = percent > 0 ? percent : 50,
                    SpeedFormatted = speed,
                    EtaFormatted = eta,
                    StatusMessage = $"Deploying {app.Name}…"
                });
            });

            FinalizeAppStatus(app, success, queueIndex, queueTotal);
        }

        private void FinalizeAppStatus(AppItem app, bool success, int queueIndex, int queueTotal)
        {
            app.IsBusy = false;
            if (success)
            {
                app.IsInstalled = true;
                app.Status = "Installed";
                app.IsSelected = false;
                ActivityLogger.Instance.Log($"Successfully deployed {app.Name}.", ActivityType.Success);
                Notify(new DownloadProgressInfo
                {
                    App = app,
                    QueueIndex = queueIndex,
                    QueueTotal = queueTotal,
                    State = DownloadState.Completed,
                    Percentage = 100,
                    StatusMessage = $"Successfully installed {app.Name}!"
                });
            }
            else
            {
                app.Status = "Failed";
                ActivityLogger.Instance.Log($"Failed to deploy {app.Name}.", ActivityType.Error);
                Notify(new DownloadProgressInfo
                {
                    App = app,
                    QueueIndex = queueIndex,
                    QueueTotal = queueTotal,
                    State = DownloadState.Error,
                    Percentage = 0,
                    StatusMessage = $"Installation failed for {app.Name}."
                });
            }
        }

        private void Notify(DownloadProgressInfo info)
        {
            ProgressChanged?.Invoke(info);
        }

        private static string GetExtensionForType(string? type, string url)
        {
            if (url.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return ".msi";
            if (url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return ".exe";
            if (url.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)) return ".msixbundle";
            if (url.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)) return ".msix";
            if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ".zip";

            var lower = (type ?? "").ToLowerInvariant();
            if (lower == "wix" || lower == "msi") return ".msi";
            return ".exe";
        }

        private static string MakeSafeFilename(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.0} MB";
            double gb = mb / 1024.0;
            return $"{gb:0.00} GB";
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
            double kb = bytesPerSec / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB/s";
            double mb = kb / 1024.0;
            return $"{mb:0.1} MB/s";
        }

        private static string FormatEta(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "Calculating…";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m remaining";
            return $"{ts.Minutes:00}:{ts.Seconds:00} remaining";
        }
    }
}
