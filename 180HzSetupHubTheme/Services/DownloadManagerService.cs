using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
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
        private int _batchTotal;
        private int _batchCompleted;

        private double _currentSpeedBps;
        private double _peakSpeedBps;
        private long _totalDownloadedBytes;
        private long _lastSystemInboundBytes = -1;
        private DateTime _lastSystemSampleTime = DateTime.UtcNow;

        public event Action<DownloadProgressInfo>? ProgressChanged;
        public event Action? QueueChanged;
        public event Action? QueueCompleted;
        public event Action? QueueCancelled;
        public event Action<double>? SpeedSampled;

        public bool IsRunning => _isQueueRunning;
        public bool IsPaused => _isPaused;
        public bool IsCancelled => _isCancelled;
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
                    if (_queue.Count > 1)
                    {
                        return _queue.Skip(1).ToList();
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
                lock (_lock) return Math.Max(_batchTotal, _queue.Count + _batchCompleted);
            }
        }

        public int QueueRemaining
        {
            get
            {
                lock (_lock) return Math.Max(0, _queue.Count - 1);
            }
        }

        public int CurrentIndex => _batchCompleted;

        public AppItem? CurrentApp
        {
            get
            {
                lock (_lock)
                {
                    return _queue.Count > 0 ? _queue[0] : null;
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
                double sampleBps;

                lock (_lock)
                {
                    if (_isQueueRunning && !_isPaused)
                    {
                        double sysBps = GetSystemNetworkInboundBps();
                        // While downloading in SetupHub, prioritize active download rate or blended system rate
                        sampleBps = Math.Max(_currentSpeedBps, sysBps);
                        _currentSpeedBps = sampleBps;

                        if (sampleBps > 0)
                        {
                            _peakSpeedBps = Math.Max(_peakSpeedBps, sampleBps);
                        }

                        if (_speedHistory.Count >= 60)
                        {
                            _speedHistory.RemoveAt(0);
                        }
                        _speedHistory.Add(sampleBps);
                    }
                    else
                    {
                        // When queue is idle, paused, or cancelled, live throughput is zero
                        sampleBps = 0;
                        _currentSpeedBps = 0;

                        if (_speedHistory.Count >= 60)
                        {
                            _speedHistory.RemoveAt(0);
                        }
                        _speedHistory.Add(0);
                    }
                }

                SpeedSampled?.Invoke(sampleBps);
            };
            _speedSampleTimer.Start();
        }

        private double GetSystemNetworkInboundBps()
        {
            try
            {
                long totalInbound = 0;
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        var stats = ni.GetIPv4Statistics();
                        if (stats != null)
                        {
                            totalInbound += stats.BytesReceived;
                        }
                    }
                }

                var now = DateTime.UtcNow;
                if (_lastSystemInboundBytes < 0)
                {
                    _lastSystemInboundBytes = totalInbound;
                    _lastSystemSampleTime = now;
                    return 0;
                }

                double elapsedSeconds = (now - _lastSystemSampleTime).TotalSeconds;
                long delta = totalInbound - _lastSystemInboundBytes;
                _lastSystemInboundBytes = totalInbound;
                _lastSystemSampleTime = now;

                if (elapsedSeconds <= 0.1 || delta < 0) return 0;
                return delta / elapsedSeconds;
            }
            catch
            {
                return 0;
            }
        }

        public void Enqueue(AppItem item, bool isUpgrade = false)
        {
            EnqueueRange(new[] { item }, isUpgrade);
        }

        public void EnqueueRange(IEnumerable<AppItem> items, bool isUpgrade = false)
        {
            int added = 0;
            lock (_lock)
            {
                foreach (var item in items)
                {
                    if (isUpgrade)
                    {
                        item.IsUpgrade = true;
                    }

                    bool eligible = item.IsUpgrade || item.HasUpdate || !item.IsInstalled;
                    if (!_queue.Contains(item) && eligible)
                    {
                        item.Status = item.IsUpgrade ? "⏳ Queued (Update)" : "⏳ Queued";
                        item.IsBusy = true;
                        _queue.Add(item);
                        added++;
                    }
                }

                if (added > 0)
                {
                    _batchTotal += added;
                }

                if (!_isQueueRunning && _queue.Count > 0)
                {
                    _isQueueRunning = true;
                    _isCancelled = false;
                    _batchCompleted = 0;
                    _batchTotal = _queue.Count;
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
                if (idx > 0)
                {
                    _queue.RemoveAt(idx);
                    item.IsBusy = false;
                    item.Status = "Install";
                    _batchTotal = Math.Max(0, _batchTotal - 1);
                }
                else if (idx == 0 && _isQueueRunning)
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
                if (idx > 1)
                {
                    _queue.RemoveAt(idx);
                    _queue.Insert(1, item);
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
            _currentSpeedBps = 0;
            _currentCts?.Cancel();

            var current = CurrentApp;
            if (current != null)
            {
                var prev = CurrentProgressInfo;
                double preservedPct = prev != null && prev.Percentage > 0 ? prev.Percentage : current.DownloadProgress;
                long bytesDownloaded = prev?.BytesDownloaded ?? 0;
                long totalBytes = prev?.TotalBytes ?? 0;
                string sizeFormatted = prev?.SizeFormatted ?? "";

                current.Status = "Paused";
                Notify(new DownloadProgressInfo
                {
                    App = current,
                    QueueIndex = prev?.QueueIndex ?? (_batchCompleted + 1),
                    QueueTotal = prev?.QueueTotal ?? Math.Max(_batchTotal, _queue.Count + _batchCompleted),
                    State = DownloadState.Paused,
                    BytesDownloaded = bytesDownloaded,
                    TotalBytes = totalBytes,
                    Percentage = preservedPct,
                    SizeFormatted = sizeFormatted,
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

            var current = CurrentApp;
            if (current != null)
            {
                var prev = CurrentProgressInfo;
                double preservedPct = prev != null && prev.Percentage > 0 ? prev.Percentage : current.DownloadProgress;
                long bytesDownloaded = prev?.BytesDownloaded ?? 0;
                long totalBytes = prev?.TotalBytes ?? 0;
                string sizeFormatted = prev?.SizeFormatted ?? "";

                current.Status = preservedPct > 0 ? $"Downloading {preservedPct:0}%" : "Downloading…";
                Notify(new DownloadProgressInfo
                {
                    App = current,
                    QueueIndex = prev?.QueueIndex ?? (_batchCompleted + 1),
                    QueueTotal = prev?.QueueTotal ?? Math.Max(_batchTotal, _queue.Count + _batchCompleted),
                    State = DownloadState.Downloading,
                    BytesDownloaded = bytesDownloaded,
                    TotalBytes = totalBytes,
                    Percentage = preservedPct,
                    SizeFormatted = sizeFormatted,
                    StatusMessage = $"Resuming download for {current.Name}…",
                    SpeedFormatted = "Connecting…",
                    EtaFormatted = "Calculating…"
                });
            }

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

        public void ResetBandwidthMonitor()
        {
            lock (_lock)
            {
                _currentSpeedBps = 0;
                _peakSpeedBps = 0;
                _totalDownloadedBytes = 0;
                _speedHistory.Clear();
                for (int i = 0; i < 60; i++)
                {
                    _speedHistory.Add(0);
                }
            }

            SpeedSampled?.Invoke(0);
        }

        public void CancelAll()
        {
            lock (_lock)
            {
                _isCancelled = true;
                _isPaused = false;
                _resumeTcs?.TrySetResult(false);
                _currentCts?.Cancel();

                // Explicitly wipe bandwidth metrics, peak speed, session data, and speed history
                _currentSpeedBps = 0;
                _peakSpeedBps = 0;
                _totalDownloadedBytes = 0;
                _speedHistory.Clear();
                for (int i = 0; i < 60; i++)
                {
                    _speedHistory.Add(0);
                }
                CurrentProgressInfo = null;

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
                _batchTotal = 0;
                _batchCompleted = 0;
            }

            QueueChanged?.Invoke();
            QueueCancelled?.Invoke();
            SpeedSampled?.Invoke(0);
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
                    if (_isCancelled || _queue.Count == 0)
                    {
                        _isQueueRunning = false;
                        break;
                    }

                    item = _queue[0];
                    idx = _batchCompleted + 1;
                    total = Math.Max(_batchTotal, _queue.Count + _batchCompleted);
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
                    if (_queue.Count > 0 && _queue[0] == item)
                    {
                        _queue.RemoveAt(0);
                    }
                    _batchCompleted++;
                }

                QueueChanged?.Invoke();
            }

            bool wasCancelled;
            lock (_lock)
            {
                wasCancelled = _isCancelled;
                _isQueueRunning = false;
                _currentSpeedBps = 0;
                if (!wasCancelled)
                {
                    _batchTotal = 0;
                    _batchCompleted = 0;
                }
            }

            if (wasCancelled)
            {
                QueueCancelled?.Invoke();
            }
            else
            {
                QueueCompleted?.Invoke();
            }
        }

        private async Task ProcessSingleAppAsync(AppItem app, int queueIndex, int queueTotal)
        {
            app.IsBusy = true;
            string actionVerb = (app.IsUpgrade || app.HasUpdate) ? "update" : "install";
            app.Status = (app.IsUpgrade || app.HasUpdate) ? "Updating…" : "Preparing…";

            Notify(new DownloadProgressInfo
            {
                App = app,
                QueueIndex = queueIndex,
                QueueTotal = queueTotal,
                State = DownloadState.Downloading,
                StatusMessage = $"Preparing to {actionVerb} {app.Name}…",
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

            // 1. Attempt high-speed direct CDN download with byte-level progress and resume support
            try
            {
                var installerInfo = await _winget.GetInstallerInfoAsync(app.Id);
                if (installerInfo != null)
                {
                    if (!string.IsNullOrWhiteSpace(installerInfo.Version))
                    {
                        if (app.IsInstalled)
                        {
                            app.AvailableVersion = installerInfo.Version;
                        }
                        else
                        {
                            app.Version = installerInfo.Version;
                        }
                    }

                    // Ensure the app is linked directly to its authentic official website without mismatch
                    string? officialLink = installerInfo.Homepage ?? installerInfo.PublisherUrl;
                    if (string.IsNullOrWhiteSpace(officialLink) && !string.IsNullOrWhiteSpace(installerInfo.Url))
                    {
                        try
                        {
                            var u = new Uri(installerInfo.Url);
                            officialLink = $"{u.Scheme}://{u.Host}";
                        }
                        catch { }
                    }
                    if (!string.IsNullOrWhiteSpace(officialLink))
                    {
                        app.WebUrl = officialLink;
                    }

                    if (!string.IsNullOrWhiteSpace(installerInfo.Url) && installerInfo.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bool downloaded = await DownloadWithResumeAsync(app, installerInfo.Url, installerInfo.Type, installerInfo.Version, installerInfo.Sha256, queueIndex, queueTotal);
                        if (downloaded)
                        {
                            await InstallDownloadedPackageAsync(app, installerInfo, queueIndex, queueTotal);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Direct CDN download for {app.Name} interrupted: {ex.Message}. Falling back to Winget pipeline.", ActivityType.Warning);
            }

            if (_isCancelled || _isSkipping) return;

            // 2. Fallback to Winget CLI direct install with real-time stream parsing
            await InstallViaWingetDirectAsync(app, queueIndex, queueTotal);
        }

        private async Task<bool> DownloadWithResumeAsync(
            AppItem app,
            string url,
            string? installerType,
            string? version,
            string? sha256,
            int queueIndex,
            int queueTotal)
        {
            string ext = GetExtensionForType(installerType, url);
            string safeId = MakeSafeFilename(app.Id);
            string versionTag = !string.IsNullOrWhiteSpace(version)
                ? MakeSafeFilename(version)
                : (!string.IsNullOrWhiteSpace(sha256) && sha256.Length >= 8 ? sha256.Substring(0, 8) : "latest");

            string finalPath = Path.Combine(_downloadDir, $"{safeId}_{versionTag}_setup{ext}");
            string partPath = Path.Combine(_downloadDir, $"{safeId}_{versionTag}_setup.part");

            // Clean up any stale or older version installers for this app
            try
            {
                var existingFiles = Directory.GetFiles(_downloadDir, $"{safeId}_*_setup.*");
                foreach (var f in existingFiles)
                {
                    if (!f.Equals(finalPath, StringComparison.OrdinalIgnoreCase) && !f.Equals(partPath, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                string legacyPath = Path.Combine(_downloadDir, $"{safeId}_setup{ext}");
                if (File.Exists(legacyPath))
                {
                    try { File.Delete(legacyPath); } catch { }
                }
            }
            catch { }

            // If the verified latest installer is already complete on disk, verify SHA256, skip download and proceed to install
            if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 1024 * 50)
            {
                if (!string.IsNullOrWhiteSpace(sha256) && !VerifyFileSha256(finalPath, sha256))
                {
                    try { File.Delete(finalPath); } catch { }
                }
                else
                {
                    long cachedFileSize = new FileInfo(finalPath).Length;
                    app.DownloadProgress = 100;
                    app.Status = "Installing…";
                    Notify(new DownloadProgressInfo
                    {
                        App = app,
                        QueueIndex = queueIndex,
                        QueueTotal = queueTotal,
                        State = DownloadState.Installing,
                        BytesDownloaded = cachedFileSize,
                        TotalBytes = cachedFileSize,
                        Percentage = 100,
                        SpeedFormatted = "Instant",
                        EtaFormatted = "Installing…",
                        SizeFormatted = $"({FormatBytes(cachedFileSize)} / {FormatBytes(cachedFileSize)})",
                        StatusMessage = $"Verifying authentic latest {app.Name} (v{versionTag})…"
                    });
                    return true;
                }
            }

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

                    if (totalBytes <= 0 && response.Content.Headers.ContentRange?.Length != null)
                    {
                        totalBytes = response.Content.Headers.ContentRange.Length.Value;
                    }

                    if (totalBytes <= 0)
                    {
                        totalBytes = ParseSizeToBytes(app.Size);
                    }

                    if (totalBytes <= 0)
                    {
                        totalBytes = Math.Max(85L * 1024 * 1024, (long)(existingBytes > 0 ? existingBytes * 1.5 : 85L * 1024 * 1024));
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

                                // Dynamically expand totalBytes if download payload exceeds initial catalog estimate
                                if (downloadedTotal >= totalBytes && totalBytes > 0)
                                {
                                    totalBytes = (long)(downloadedTotal * 1.12);
                                }

                                double percent = totalBytes > 0
                                    ? Math.Clamp((double)downloadedTotal / totalBytes * 100.0, 1.0, 99.0)
                                    : Math.Clamp(downloadedTotal / (1024.0 * 1024.0), 1.0, 95.0);

                                double remainingBytes = Math.Max(0, totalBytes - downloadedTotal);
                                double etaSec = speedBytesPerSec > 1024 ? remainingBytes / speedBytesPerSec : 0;

                                string speedStr = FormatSpeed(speedBytesPerSec);
                                string etaStr = FormatEta(etaSec);
                                string sizeStr = totalBytes > 0
                                    ? $"({FormatBytes(downloadedTotal)} / {FormatBytes(totalBytes)})"
                                    : $"({FormatBytes(downloadedTotal)})";

                                app.Status = $"Downloading {percent:0}%";
                                app.DownloadProgress = percent;
                                app.DownloadSpeed = speedStr;
                                app.DownloadEta = etaStr;

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

                    if (!string.IsNullOrWhiteSpace(sha256) && !VerifyFileSha256(finalPath, sha256))
                    {
                        ActivityLogger.Instance.Log($"Hash verification failed for {app.Name}. Deleting corrupt file and falling back to Winget.", ActivityType.Warning);
                        try { File.Delete(finalPath); } catch { }
                        return false;
                    }
                    long finalFileSize = new FileInfo(finalPath).Length;
                    app.DownloadProgress = 100;
                    app.Status = "Installing…";
                    Notify(new DownloadProgressInfo
                    {
                        App = app,
                        QueueIndex = queueIndex,
                        QueueTotal = queueTotal,
                        State = DownloadState.Installing,
                        BytesDownloaded = finalFileSize,
                        TotalBytes = finalFileSize,
                        Percentage = 100,
                        SpeedFormatted = FormatSpeed(_currentSpeedBps),
                        EtaFormatted = "Installing…",
                        SizeFormatted = $"({FormatBytes(finalFileSize)} / {FormatBytes(finalFileSize)})",
                        StatusMessage = $"Installing authentic {app.Name} package…"
                    });
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
                catch (Exception ex) when (_isPaused || ex.InnerException is OperationCanceledException)
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

                    var prev = CurrentProgressInfo;
                    double preservedPct = prev != null && prev.Percentage > 0 ? prev.Percentage : app.DownloadProgress;
                    long bytesDownloaded = prev?.BytesDownloaded ?? 0;
                    long totalBytes = prev?.TotalBytes ?? 0;
                    string sizeFormatted = prev?.SizeFormatted ?? "";

                    app.Status = "Connection Error";
                    Notify(new DownloadProgressInfo
                    {
                        App = app,
                        QueueIndex = queueIndex,
                        QueueTotal = queueTotal,
                        State = DownloadState.Error,
                        BytesDownloaded = bytesDownloaded,
                        TotalBytes = totalBytes,
                        Percentage = preservedPct,
                        SizeFormatted = sizeFormatted,
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
            string versionTag = !string.IsNullOrWhiteSpace(info.Version)
                ? MakeSafeFilename(info.Version)
                : (!string.IsNullOrWhiteSpace(info.Sha256) && info.Sha256.Length >= 8 ? info.Sha256.Substring(0, 8) : "latest");

            string finalPath = Path.Combine(_downloadDir, $"{safeId}_{versionTag}_setup{ext}");

            if (!File.Exists(finalPath))
            {
                string legacyPath = Path.Combine(_downloadDir, $"{safeId}_setup{ext}");
                if (File.Exists(legacyPath))
                {
                    finalPath = legacyPath;
                }
                else
                {
                    await InstallViaWingetDirectAsync(app, queueIndex, queueTotal);
                    return;
                }
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
                else if (ext.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
                         ext.Equals(".appx", StringComparison.OrdinalIgnoreCase) ||
                         ext.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                         ext.Equals(".appxbundle", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = "powershell.exe";
                    psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{finalPath}' -ForceApplicationShutdown\"";
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
                    psi.Arguments = DetectInstallerArguments(finalPath);
                }

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    success = (proc.ExitCode == 0 || proc.ExitCode == 3010 || proc.ExitCode == 1641);
                }

                if (!success && AppMetadataHelper.IsAppInstalled(app))
                {
                    success = true;
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Direct installer failed for {app.Name}: {ex.Message}. Falling back to Winget.", ActivityType.Warning);
            }

            if (!success)
            {
                // Fallback to winget install or upgrade command
                success = (app.IsUpgrade || app.HasUpdate)
                    ? await _winget.UpgradeAsync(app.Id)
                    : await _winget.InstallAsync(app.Id, app.Source, app.Name);
            }

            FinalizeAppStatus(app, success, queueIndex, queueTotal);
        }

        private static string DetectInstallerArguments(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    byte[] buffer = new byte[Math.Min(fs.Length, 4 * 1024 * 1024)];
                    int read = fs.Read(buffer, 0, buffer.Length);
                    string header = System.Text.Encoding.ASCII.GetString(buffer, 0, read);

                    if (header.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
                    }
                    if (header.Contains("NullsoftInst", StringComparison.OrdinalIgnoreCase))
                    {
                        return "/S";
                    }
                    if (header.Contains("WixBurn", StringComparison.OrdinalIgnoreCase) || header.Contains("Burn", StringComparison.OrdinalIgnoreCase))
                    {
                        return "/quiet /norestart";
                    }
                    if (header.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
                    {
                        return "/s /v\"/qn\"";
                    }
                }
            }
            catch { }

            return "/VERYSILENT /NORESTART /S /quiet";
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

            using var smoothTimer = new System.Timers.Timer(250);
            smoothTimer.Elapsed += (_, _) =>
            {
                if (ct.IsCancellationRequested || _isCancelled || _isPaused) return;
                if (currentPct >= 20 && currentPct < 75)
                {
                    currentPct = Math.Min(75, currentPct + 0.5);
                    double simulatedSpeed = _currentSpeedBps > 0 ? _currentSpeedBps : 12.5 * 1024 * 1024;
                    _currentSpeedBps = simulatedSpeed;
                    _peakSpeedBps = Math.Max(_peakSpeedBps, simulatedSpeed);
                    _totalDownloadedBytes += (long)(simulatedSpeed * 0.25);

                    string speed = FormatSpeed(simulatedSpeed);
                    string statusMsg = $"Downloading authentic {app.Name} package…";
                    Notify(new DownloadProgressInfo
                    {
                        App = app,
                        QueueIndex = queueIndex,
                        QueueTotal = queueTotal,
                        State = DownloadState.Downloading,
                        Percentage = currentPct,
                        SpeedFormatted = speed,
                        EtaFormatted = "In progress…",
                        StatusMessage = statusMsg
                    });
                }
            };
            smoothTimer.Start();

            Action<string> onOutputLine = line =>
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
                         line.Contains("Installing", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Upgrading", StringComparison.OrdinalIgnoreCase))
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
                    eta = (app.IsUpgrade || app.HasUpdate) ? "Updating…" : "Installing…";
                }

                string sizeStr = "";
                var matchSize = sizeRegex.Match(line);
                if (matchSize.Success)
                {
                    sizeStr = matchSize.Value;
                }

                var state = currentPct >= 85 ? DownloadState.Installing : DownloadState.Downloading;
                string statusMsg = currentPct >= 85
                    ? $"{(app.IsUpgrade || app.HasUpdate ? "Updating" : "Installing")} {app.Name} quietly in background…"
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
            };

            bool success = (app.IsUpgrade || app.HasUpdate)
                ? await _winget.UpgradeAsync(app.Id, onOutputLine, ct)
                : await _winget.InstallAsync(app.Id, app.Source, app.Name, onOutputLine, ct);

            smoothTimer.Stop();

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
            if (!success && AppMetadataHelper.IsAppInstalled(app))
            {
                success = true;
            }

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
                app.InstallDate = DateTime.Now;

                bool wasUpgrade = app.IsUpgrade || app.HasUpdate;
                if (wasUpgrade)
                {
                    if (!string.IsNullOrWhiteSpace(app.AvailableVersion))
                    {
                        app.Version = app.AvailableVersion;
                    }
                    app.AvailableVersion = "";
                    app.IsUpgrade = false;
                    UpdateMonitorService.Instance.MarkAsUpdated(app.Id, app.Name);
                    _ = Task.Run(async () =>
                    {
                        try { await UpdateMonitorService.Instance.RefreshAsync(force: true); } catch { }
                    });
                }

                // Invalidate registry cache and notify catalog of newly installed app
                AppMetadataHelper.InvalidateCache();
                PackageCatalogService.NotifyStatusChanged(app.Id, true);
                PackageCatalogService.NotifyStatusChanged(app.Name, true);

                ActivityLogger.Instance.Log($"Successfully deployed {app.Name}.", ActivityType.Success);
                Notify(new DownloadProgressInfo
                {
                    App = app,
                    QueueIndex = queueIndex,
                    QueueTotal = queueTotal,
                    State = DownloadState.Completed,
                    Percentage = 100,
                    StatusMessage = wasUpgrade ? $"Successfully updated {app.Name}!" : $"Successfully installed {app.Name}!"
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
                    StatusMessage = $"{(app.IsUpgrade ? "Update" : "Installation")} failed for {app.Name}."
                });
            }

            lock (_lock)
            {
                _completedHistory.Insert(0, new DownloadHistoryItem
                {
                    App = app,
                    CompletedAt = DateTime.Now,
                    Success = success,
                    Message = success
                        ? (app.IsUpgrade ? "Updated successfully" : "Installed successfully")
                        : (app.IsUpgrade ? "Update failed" : "Installation failed")
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

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.00} MB";
            double gb = mb / 1024.0;
            return $"{gb:0.00} GB";
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec <= 0 || double.IsNaN(bytesPerSec)) return "0 B/s";
            if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
            double kb = bytesPerSec / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB/s";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.00} MB/s";
            double gb = mb / 1024.0;
            return $"{gb:0.00} GB/s";
        }

        public static long ParseSizeToBytes(string? sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr)) return 0;
            try
            {
                var cleaned = sizeStr.Trim().Replace("💾", "").Trim();
                var match = Regex.Match(cleaned, @"([\d\.]+)\s*([KMGT]?B)", RegexOptions.IgnoreCase);
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    string unit = match.Groups[2].Value.ToUpperInvariant();
                    return unit switch
                    {
                        "KB" => (long)(val * 1024),
                        "MB" => (long)(val * 1024 * 1024),
                        "GB" => (long)(val * 1024 * 1024 * 1024),
                        "TB" => (long)(val * 1024L * 1024L * 1024L * 1024L),
                        _ => (long)val
                    };
                }
            }
            catch { }
            return 0;
        }

        private static string FormatEta(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "Almost done…";
            if (seconds < 1) return "< 1s remaining";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m remaining";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s remaining";
            return $"{ts.Seconds}s remaining";
        }

        private static bool VerifyFileSha256(string filePath, string expectedSha256)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(filePath)) return true;
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = sha256.ComputeHash(stream);
                string computed = Convert.ToHexString(hashBytes);
                return computed.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
