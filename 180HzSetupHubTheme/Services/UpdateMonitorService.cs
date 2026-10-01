using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class UpdateMonitorService : IDisposable
    {
        private static readonly Lazy<UpdateMonitorService> _instance =
            new(() => new UpdateMonitorService());

        public static UpdateMonitorService Instance => _instance.Value;

        private readonly WingetService _winget = new();
        private readonly PackageCatalogService _catalogService = new();
        private Timer? _timer;
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private bool _isStarted;
        private List<AppItem>? _catalogCache;

        public ObservableCollection<AppItem> UpgradableApps { get; } = new();

        public bool ShouldSkipDueToRecency() =>
            (DateTime.UtcNow - _lastRefresh).TotalMinutes < 5.0;

        public void Start()
        {
            if (_isStarted) return;
            _isStarted = true;

            var hours = SettingsService.Instance.Current.UpdateCheckFrequencyHours;
            if (hours <= 0)
            {
                ActivityLogger.Instance.Log("Background update monitor disabled by settings (Never).", ActivityType.Info);
                return;
            }

            var intervalMs = (long)TimeSpan.FromHours(hours).TotalMilliseconds;

            // Fast initial check after 10 seconds, then recurring on configured period
            _timer = new Timer(async _ =>
            {
                try
                {
                    await RefreshAsync(force: false);
                }
                catch (Exception ex)
                {
                    ActivityLogger.Instance.Log($"Background update check error: {ex.Message}", ActivityType.Warning);
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(intervalMs));

            ActivityLogger.Instance.Log($"Background update monitor initialized (Interval: {hours}h).", ActivityType.Info);
        }

        public void UpdateInterval()
        {
            if (_timer == null) return;

            var hours = SettingsService.Instance.Current.UpdateCheckFrequencyHours;
            if (hours <= 0)
            {
                ActivityLogger.Instance.Log("Background update monitor disabled by settings (Never).", ActivityType.Info);
                return;
            }

            var interval = TimeSpan.FromHours(hours);
            _timer.Change(interval, interval);
            ActivityLogger.Instance.Log($"Background update monitor interval set to {hours}h.", ActivityType.Info);
        }

        private readonly HashSet<string> _recentlyUpdated = new(StringComparer.OrdinalIgnoreCase);

        public void MarkAsUpdated(string appId, string? appName = null)
        {
            if (!string.IsNullOrWhiteSpace(appId)) _recentlyUpdated.Add(appId);
            if (!string.IsNullOrWhiteSpace(appName)) _recentlyUpdated.Add(appName);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.InvokeAsync(() =>
            {
                var matches = UpgradableApps.Where(a =>
                    string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(appName) && string.Equals(a.Name, appName, StringComparison.OrdinalIgnoreCase))).ToList();

                foreach (var match in matches)
                {
                    UpgradableApps.Remove(match);
                }
            });
        }

        public void UnmarkUpdated(string appIdOrName)
        {
            if (!string.IsNullOrWhiteSpace(appIdOrName))
            {
                _recentlyUpdated.Remove(appIdOrName);
            }
        }

        public async Task RefreshAsync(bool force = false)
        {
            if (!force && ShouldSkipDueToRecency())
            {
                return;
            }

            if (!await _refreshLock.WaitAsync(0))
            {
                // Already checking in background
                return;
            }

            try
            {
                if (!await _winget.IsAvailableAsync()) return;

                var rawUpgradable = await _winget.GetUpgradableAppsAsync();
                _catalogCache ??= await _catalogService.GetAllAsync();

                // Filter out any packages where current version already matches available version,
                // or where the app was already updated in the current session
                var latestUpgradable = rawUpgradable
                    .Where(a => !string.IsNullOrWhiteSpace(a.AvailableVersion) &&
                                !string.Equals(a.Version?.Trim(), a.AvailableVersion?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                !_recentlyUpdated.Contains(a.Id) &&
                                !_recentlyUpdated.Contains(a.Name))
                    .ToList();

                foreach (var app in latestUpgradable)
                {
                    AppMetadataHelper.EnrichAppItem(app, _catalogCache);
                }

                // Diff by Id against current collection
                var currentIds = new HashSet<string>(UpgradableApps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
                var newlyDetected = latestUpgradable
                    .Where(a => !currentIds.Contains(a.Id))
                    .ToList();

                // Fire notifications for newly detected updates
                if (newlyDetected.Count > 0)
                {
                    if (newlyDetected.Count <= 3)
                    {
                        foreach (var app in newlyDetected)
                        {
                            var versionDisplay = string.IsNullOrWhiteSpace(app.AvailableVersion)
                                ? app.Version
                                : $"{app.Version} → {app.AvailableVersion}";
                            NotificationService.Notify("Update available", $"{app.Name} {versionDisplay}");
                        }
                    }
                    else
                    {
                        NotificationService.Notify("Updates available", $"{newlyDetected.Count} software updates are ready to install.");
                    }

                    ActivityLogger.Instance.Log($"Update monitor detected {newlyDetected.Count} new update(s).", ActivityType.Info);
                }

                // Update shared ObservableCollection on UI thread
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        UpgradableApps.Clear();
                        foreach (var app in latestUpgradable)
                        {
                            UpgradableApps.Add(app);
                        }
                    });
                }

                _lastRefresh = DateTime.UtcNow;

                // Asynchronously fetch missing icons and websites in parallel
                _ = Task.Run(async () =>
                {
                    await Parallel.ForEachAsync(latestUpgradable, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (app, ct) =>
                    {
                        if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(app.Id))
                        {
                            var meta = await _winget.GetMetadataAsync(app.Id);
                            if (meta?.BestLink != null)
                            {
                                app.WebUrl = meta.BestLink;
                                if (string.IsNullOrWhiteSpace(app.IconUrl))
                                {
                                    app.IconUrl = IconCacheService.DeriveFaviconUrl(app.WebUrl) ?? "";
                                }
                            }
                        }

                        if (app.IconImageSource == null && (!string.IsNullOrWhiteSpace(app.IconUrl) || !string.IsNullOrWhiteSpace(app.LocalIconPath)))
                        {
                            var img = await IconCacheService.GetImageAsync(app.IconUrl, app.LocalIconPath);
                            if (img != null && dispatcher != null)
                            {
                                await dispatcher.InvokeAsync(() => app.IconImageSource = img);
                            }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                ActivityLogger.Instance.Log($"Failed to refresh updates: {ex.Message}", ActivityType.Warning);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
            _refreshLock.Dispose();
        }
    }
}
