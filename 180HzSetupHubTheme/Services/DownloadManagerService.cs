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

    public class DownloadHistoryItem
    {
        public AppItem App { get; set; } = null!;
        public DateTime CompletedAt { get; set; } = DateTime.Now;
        public bool Success { get; set; } = true;
        public string Message { get; set; } = "";
        public string FormattedTime => CompletedAt.ToString("hh:mm tt");
        public string FormattedDate => CompletedAt.ToString("MMM dd, yyyy");
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
        private readonly List<DownloadHistoryItem> _completedHistory = new();
        private readonly List<double> _speedHistory = new();
        private readonly System.Timers.Timer _speedSampleTimer;
        private readonly object _lock = new();

        private CancellationTokenSource? _currentCts;
        private TaskCompletionSource<bool>? _resumeTcs;
        private volatile bool _isPaused;
        private volatile bool _isSkipping;
        private volatile bool _isCancelled;
        private volatile bool _isQueueRunning;
        private int _currentIndex;

        private double _currentSpeedBps;
        private double _peakSpeedBps;
        private long _totalDownloadedBytes;

        public event Action<DownloadProgressInfo>? ProgressChanged;
        public event Action? QueueChanged;
        public event Action? QueueCompleted;
        public event Action<double>? SpeedSampled;

        public bool IsRunning => _isQueueRunning;
        public bool IsPaused => _isPaused;
        public double CurrentSpeedBps => _currentSpeedBps;
        public double PeakSpeedBps => _peakSpeedBps;
        public long TotalDownloadedBytes => _totalDownloadedBytes;
        public string CurrentSpeedFormatted => FormatSpeed(_currentSpeedBps);
        public string PeakSpeedFormatted => FormatSpeed(_peakSpeedBps);
        public string TotalDownloadedFormatted => FormatBytes(_totalDownloadedBytes);
        public DownloadProgressInfo? CurrentProgressInfo { get; private set; }

        public IReadOnlyList<double> SpeedHistory
        {
            get
            {
                lock (_lock) return _speedHistory.ToList();
            }
        }

        public IReadOnlyList<AppItem> Queue
        {
            get
            {
                lock (_lock) return _queue.ToList();
            }
        }

        public IReadOnlyList<AppItem> RemainingQueue
        {
            get
            {
                lock (_lock)
                {
                    if (_currentIndex + 1 < _queue.Count)
                    {
                        return System.Linq.Enumerable.ToList(System.Linq.Enumerable.Skip(_queue, _currentIndex + 1));
                    }
                    return new List<AppItem>();
                }
            }
        }

        public IReadOnlyList<DownloadHistoryItem> CompletedHistory
        {
            get
            {
                lock (_lock) return _completedHistory.ToList();
            }
        }

        public int QueueTotal
        {
            get
            {
                lock (_lock) return _queue.Count;
            }
        }

        public int QueueRemaining
        {
            get
            {
                lock (_lock) return Math.Max(0, _queue.Count - (_currentIndex + 1));
            }
        }

        public int CurrentIndex => _currentIndex;

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

            for (int i = 0; i < 60; i++)
            {
                _speedHistory.Add(0);
            }

            _speedSampleTimer = new System.Timers.Timer(1000);
            _speedSampleTimer.Elapsed += (_, _) =>
            {
                double mbps = 0;
                lock (_lock)
                {
                    if (_isQueueRunning && !_isPaused)
                    {
                        mbps = _currentSpeedBps / (1024.0 * 1024.0);
                    }
                    else
                    {
                        _currentSpeedBps = 0;
                    }

                    if (_speedHistory.Count >= 60)
                    {
                        _speedHistory.RemoveAt(0);
                    }
                    _speedHistory.Add(mbps);
                }

                SpeedSampled?.Invoke(mbps);
            };
            _speedSampleTimer.Start();
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
                        item.Status = "⏳ Queued";
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
            QueueChanged?.Invoke();
        }

        public void RemoveFromQueue(AppItem item)
        {
            lock (_lock)
            {
                int idx = _queue.IndexOf(item);
                if (idx > _currentIndex)
                {
                    _queue.RemoveAt(idx);
                    item.IsBusy = false;
                    item.Status = "Install";
                }
                else if (idx == _currentIndex && _isQueueRunning)
                {
                    SkipCurrent();
                }
            }
            QueueChanged?.Invoke();
        }

        public void MoveToTop(AppItem item)
        {
            lock (_lock)
            {
                int idx = _queue.IndexOf(item);
                if (idx > _currentIndex + 1)
                {
                    _queue.RemoveAt(idx);
                    _queue.Insert(_currentIndex + 1, item);
                }
            }
            QueueChanged?.Invoke();
        }

        public void ClearCompletedHistory()
        {
            lock (_lock)
            {
                _completedHistory.Clear();
            }
            QueueChanged?.Invoke();
        }

        public void RemoveFromHistory(DownloadHistoryItem item)
        {
            lock (_lock)
            {
                _completedHistory.Remove(item);
            }
            QueueChanged?.Invoke();
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

                _currentSpeedBps = 0;
                foreach (var item in _queue)
                {
                    if (!item.IsInstalled)
                    {
                        item.Status = "Install";
                        item.IsBusy = false;
                        item.DownloadProgress = 0;
                        item.DownloadSpeed = "";
                        item.DownloadEta = "";
                        item.IsPaused = false;
                    }
                }
                _queue.Clear();
                _isQueueRunning = false;
            }

            QueueChanged?.Invoke();
            QueueCompleted?.Invoke();
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                AppItem? item = null;
                int idx = 0;
                int total = 0;

                while (_isPaused && !_isCancelled)
                {
                    if (_resumeTcs != null)
                    {
                        try { await _resumeTcs.Task; } catch { }
                    }
                    else
                    {
                        await Task.Delay(250);
                    }
                }

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

                try
                {
                    await ProcessSingleAppAsync(item, idx, total);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Error processing {item.Name}: {ex.Message}", ActivityType.Error);
                }

                if (_isPaused)
                {
                    while (_isPaused && !_isCancelled)
                    {
                        if (_resumeTcs != null)
                        {
                            try { await _resumeTcs.Task; } catch { }
                        }
                        else
                        {
                            await Task.Delay(250);
                        }
                    }

                    if (!_isSkipping && !_isCancelled)
                    {
                        continue;
                    }
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
                StatusMessage = $"Preparing to install {app.Name}…",
                SpeedFormatted = "Connecting…",
                EtaFormatted = "Starting…"
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

            // Direct Winget install with live output streaming and high-speed progress parsing
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

                                _currentSpeedBps = speedBytesPerSec;
                                _peakSpeedBps = Math.Max(_peakSpeedBps, speedBytesPerSec);
                                _totalDownloadedBytes += Math.Max(0, downloadedTotal - lastBytesSample);

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
            _currentCts = new CancellationTokenSource();
            var ct = _currentCts.Token;

            app.Status = "Downloading…";
            Notify(new DownloadProgressInfo
            {
                App = app,
                QueueIndex = queueIndex,
                QueueTotal = queueTotal,
                State = DownloadState.Downloading,
                Percentage = 15,
                StatusMessage = $"Connecting to repository for {app.Name}…",
                SpeedFormatted = "High-speed",
                EtaFormatted = "In progress…"
            });

            var percentRegex = new Regex(@"(\d{1,3})%", RegexOptions.Compiled);
            var speedRegex = new Regex(@"([\d\.]+\s*(?:KB|MB|GB)/s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var etaRegex = new Regex(@"--\s*(\d+[smh])", RegexOptions.Compiled);
            var sizeRegex = new Regex(@"([\d\.]+\s*(?:KB|MB|GB))\s*/\s*([\d\.]+\s*(?:KB|MB|GB))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            double currentPct = 15;

            bool success = await _winget.InstallAsync(app.Id, app.Source, line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                double percent = currentPct;
                var matchPct = percentRegex.Match(line);
                if (matchPct.Success && double.TryParse(matchPct.Groups[1].Value, out var p))
                {
                    percent = p;
                    currentPct = p;
                }
                else if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
                {
                    percent = Math.Max(currentPct, 30);
                    currentPct = percent;
                }
                else if (line.Contains("verified installer hash", StringComparison.OrdinalIgnoreCase))
                {
                    percent = Math.Max(currentPct, 70);
                    currentPct = percent;
                }
                else if (line.Contains("Starting package install", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Installing", StringComparison.OrdinalIgnoreCase))
                {
                    percent = Math.Max(currentPct, 85);
                    currentPct = percent;
                }

                string speed = "High-speed";
                var matchSpeed = speedRegex.Match(line);
                if (matchSpeed.Success)
                {
                    speed = matchSpeed.Groups[1].Value;
                    _currentSpeedBps = ParseSpeedStringToBps(speed);
                    _peakSpeedBps = Math.Max(_peakSpeedBps, _currentSpeedBps);
                }

                string eta = "Working…";
                var matchEta = etaRegex.Match(line);
                if (matchEta.Success)
                {
                    eta = $"{matchEta.Groups[1].Value} remaining";
                }
                else if (currentPct >= 85)
                {
                    eta = "Installing…";
                }

                string sizeStr = "";
                var matchSize = sizeRegex.Match(line);
                if (matchSize.Success)
                {
                    sizeStr = matchSize.Value;
                }

                var state = currentPct >= 85 ? DownloadState.Installing : DownloadState.Downloading;
                string statusMsg = currentPct >= 85
                    ? $"Installing {app.Name} quietly in background…"
                    : $"Downloading authentic {app.Name} package…";

                app.Status = $"{statusMsg} ({percent:0}%)";

                Notify(new DownloadProgressInfo
                {
                    App = app,
                    QueueIndex = queueIndex,
                    QueueTotal = queueTotal,
                    State = state,
                    Percentage = percent,
                    SpeedFormatted = speed,
                    EtaFormatted = eta,
                    SizeFormatted = sizeStr,
                    StatusMessage = statusMsg
                });
            }, ct);

            if (_isCancelled)
            {
                return;
            }

            if (_isPaused)
            {
                return;
            }

            FinalizeAppStatus(app, success, queueIndex, queueTotal);
        }

        private void FinalizeAppStatus(AppItem app, bool success, int queueIndex, int queueTotal)
        {
            app.IsBusy = false;
            app.DownloadSpeed = "";
            app.DownloadEta = "";
            app.IsPaused = false;
            _currentSpeedBps = 0;
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

            lock (_lock)
            {
                _completedHistory.Insert(0, new DownloadHistoryItem
                {
                    App = app,
                    CompletedAt = DateTime.Now,
                    Success = success,
                    Message = success ? "Installed successfully" : "Installation failed"
                });
            }
            QueueChanged?.Invoke();
        }

        private void Notify(DownloadProgressInfo info)
        {
            CurrentProgressInfo = info;
            if (info.App != null)
            {
                info.App.DownloadProgress = info.Percentage;
                info.App.DownloadSpeed = info.SpeedFormatted;
                info.App.DownloadEta = info.EtaFormatted;
                info.App.IsPaused = (info.State == DownloadState.Paused);

                if (info.State == DownloadState.Downloading)
                {
                    info.App.Status = info.Percentage > 0 ? $"⏸ {info.Percentage:0}%" : "⏸ Pause";
                }
                else if (info.State == DownloadState.Paused)
                {
                    info.App.Status = "▶ Resume";
                }
                else if (info.State == DownloadState.Installing)
                {
                    info.App.Status = "Installing…";
                }
            }
            ProgressChanged?.Invoke(info);
        }

        private static double ParseSpeedStringToBps(string speedStr)
        {
            if (string.IsNullOrWhiteSpace(speedStr)) return 0;
            try
            {
                var parts = speedStr.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && double.TryParse(parts[0], out double val))
                {
                    string unit = parts[1].ToUpperInvariant();
                    if (unit.StartsWith("GB")) return val * 1024 * 1024 * 1024;
                    if (unit.StartsWith("MB")) return val * 1024 * 1024;
                    if (unit.StartsWith("KB")) return val * 1024;
                    return val;
                }
            }
            catch { }
            return 0;
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
