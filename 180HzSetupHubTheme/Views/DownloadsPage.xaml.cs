using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class DownloadsPage : UserControl
    {
        private readonly MainWindow? _mainWindow;

        public DownloadsPage() : this(null)
        {
        }

        public DownloadsPage(MainWindow? mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            Loaded += DownloadsPage_Loaded;
            Unloaded += DownloadsPage_Unloaded;
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue)
                {
                    RefreshAllUI();
                }
            };
        }

        private void DownloadsPage_Loaded(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.SpeedSampled += OnSpeedSampled;
            DownloadManagerService.Instance.ProgressChanged += OnProgressChanged;
            DownloadManagerService.Instance.QueueChanged += OnQueueChanged;
            DownloadManagerService.Instance.QueueCompleted += OnQueueCompleted;
            DownloadManagerService.Instance.QueueCancelled += OnQueueCompleted;

            RefreshAllUI();
        }

        private void DownloadsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.SpeedSampled -= OnSpeedSampled;
            DownloadManagerService.Instance.ProgressChanged -= OnProgressChanged;
            DownloadManagerService.Instance.QueueChanged -= OnQueueChanged;
            DownloadManagerService.Instance.QueueCompleted -= OnQueueCompleted;
            DownloadManagerService.Instance.QueueCancelled -= OnQueueCompleted;
        }

        private void OnSpeedSampled(double bps)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RenderSpeedGraph();
                UpdateTelemetry();
            });
        }

        private void OnProgressChanged(DownloadProgressInfo info)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateActiveHeroCard(info);
                UpdateTelemetry();
            });
        }

        private void OnQueueChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshQueueAndHistory();
                UpdateTelemetry();
            });
        }

        private void OnQueueCompleted()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshAllUI();
            });
        }

        private void RefreshAllUI()
        {
            UpdateTelemetry();
            RefreshQueueAndHistory();
            RenderSpeedGraph();

            var current = DownloadManagerService.Instance.CurrentApp;
            if (DownloadManagerService.Instance.IsRunning && current != null)
            {
                UpdateActiveHeroCard(DownloadManagerService.Instance.CurrentProgressInfo ?? new DownloadProgressInfo
                {
                    App = current,
                    State = DownloadManagerService.Instance.IsPaused ? DownloadState.Paused : DownloadState.Downloading,
                    Percentage = current.DownloadProgress,
                    SpeedFormatted = current.DownloadSpeed,
                    EtaFormatted = current.DownloadEta,
                    StatusMessage = current.Status
                });
            }
            else
            {
                ActiveContentGrid.Visibility = Visibility.Collapsed;
                IdlePlaceholderGrid.Visibility = Visibility.Visible;
                ActiveDownloadCard.BorderBrush = (SolidColorBrush)FindResource("BrushBorder");
                SetStatusBadge("IDLE", "BrushTextSecondary");
                LottieHelper.SetButtonIcon(BtnPauseAll, "pause.json", 15);
                BtnPauseAll.Tag = "Pause All";
                BtnPauseAll.IsEnabled = false;
            }
        }

        private void UpdateTelemetry()
        {
            var dm = DownloadManagerService.Instance;

            MetricCurrentSpeed.Text = dm.CurrentSpeedFormatted;
            MetricPeakSpeed.Text = dm.PeakSpeedFormatted;
            MetricTotalDownloaded.Text = dm.TotalDownloadedFormatted;

            int remaining = dm.QueueRemaining;
            int total = dm.QueueTotal;
            int completedCount = dm.CompletedHistory.Count;

            MetricQueueRemaining.Text = dm.IsRunning ? $"{remaining} Waiting" : $"{total} Total";
            MetricQueueSubtext.Text = $"{completedCount} deployed this session";

            if (dm.IsRunning)
            {
                BtnPauseAll.IsEnabled = true;
                if (dm.IsPaused)
                {
                    SetStatusBadge("PAUSED", "BrushWarning");
                    LottieHelper.SetButtonIcon(BtnPauseAll, "resume.json", 15);
                    BtnPauseAll.Tag = "Resume All";
                }
                else
                {
                    SetStatusBadge("DOWNLOADING", "BrushSuccess");
                    LottieHelper.SetButtonIcon(BtnPauseAll, "pause.json", 15);
                    BtnPauseAll.Tag = "Pause All";
                }
            }
            else
            {
                SetStatusBadge("IDLE", "BrushTextSecondary");
                LottieHelper.SetButtonIcon(BtnPauseAll, "pause.json", 15);
                BtnPauseAll.Tag = "Pause All";
                BtnPauseAll.IsEnabled = false;
            }
        }

        private void SetStatusBadge(string text, string brushKey)
        {
            LiveStatusText.Text = text;
            if (TryFindResource(brushKey) is Brush b)
            {
                LiveStatusText.Foreground = b;
                LiveStatusDot.Fill = b;
            }
            if (TryFindResource("BrushSurfaceHover") is Brush bg)
            {
                LiveStatusBadge.Background = bg;
            }
        }

        private void UpdateActiveHeroCard(DownloadProgressInfo info)
        {
            if (info?.App == null) return;

            ActiveContentGrid.Visibility = Visibility.Visible;
            IdlePlaceholderGrid.Visibility = Visibility.Collapsed;
            ActiveDownloadCard.BorderBrush = (SolidColorBrush)FindResource("BrushAccent");

            ActiveAppName.Text = info.App.Name;
            ActiveAppIcon.Source = info.App.IconImageSource;
            ActiveCategoryText.Text = string.IsNullOrWhiteSpace(info.App.Category) ? "Application" : info.App.Category;
            ActiveVersionText.Text = info.App.FormattedVersion;
            ActiveStatusDetail.Text = string.IsNullOrWhiteSpace(info.StatusMessage) ? "Processing installation payload…" : info.StatusMessage;

            // Link authentic official site and enable instant visit
            string? webUrl = info.App.WebUrl;
            if (string.IsNullOrWhiteSpace(webUrl))
            {
                webUrl = AppMetadataHelper.ResolveOfficialUrl(info.App);
                if (!string.IsNullOrWhiteSpace(webUrl))
                {
                    info.App.WebUrl = webUrl;
                }
            }
            if (!string.IsNullOrWhiteSpace(webUrl))
            {
                BtnActiveWebsite.ToolTip = $"Visit Official Site: {webUrl}";
                ActiveAppName.ToolTip = $"Visit Official Site: {webUrl} (Click to open)";
                ActiveAppName.Cursor = System.Windows.Input.Cursors.Hand;
            }
            else
            {
                BtnActiveWebsite.ToolTip = "Open Official Website";
            }

            double displayPct = info.Percentage > 0 ? info.Percentage : (info.App?.DownloadProgress ?? 0);
            double targetPct = Math.Clamp(displayPct, 0, 100);
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = targetPct,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            ActiveProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
            ActiveProgressPercent.Text = $"{displayPct:0}%";
            ActiveSpeedText.Text = info.SpeedFormatted;
            string eta = info.EtaFormatted ?? "Calculating…";
            if (eta.StartsWith("ETA:", StringComparison.OrdinalIgnoreCase))
            {
                eta = eta.Substring(4).TrimStart();
            }
            ActiveEtaText.Text = eta;
            string size = !string.IsNullOrWhiteSpace(info.SizeFormatted) ? info.SizeFormatted : (DownloadManagerService.Instance.CurrentProgressInfo?.SizeFormatted ?? "");
            ActiveTransferredText.Text = size;
            ActiveQueueCountText.Text = $"App {info.QueueIndex} of {info.QueueTotal}";

            switch (info.State)
            {
                case DownloadState.Downloading:
                    ActiveStateText.Text = "DOWNLOADING";
                    ActiveStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushAccent");
                    LottieHelper.SetButtonIcon(BtnActivePauseResume, "pause.json", 15);
                    BtnActivePauseResume.Tag = "Pause";
                    BtnActivePauseResume.IsEnabled = true;
                    break;

                case DownloadState.Paused:
                    ActiveStateText.Text = "PAUSED";
                    ActiveStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    LottieHelper.SetButtonIcon(BtnActivePauseResume, "resume.json", 15);
                    BtnActivePauseResume.Tag = "Resume";
                    BtnActivePauseResume.IsEnabled = true;
                    break;

                case DownloadState.Installing:
                    ActiveStateText.Text = "INSTALLING";
                    ActiveStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                    ActiveStateText.Foreground = (Brush)FindResource("BrushAccent");
                    BtnActivePauseResume.IsEnabled = false;
                    break;

                case DownloadState.Error:
                    ActiveStateText.Text = "CONNECTION ERROR";
                    ActiveStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushError");
                    LottieHelper.SetButtonIcon(BtnActivePauseResume, "resume.json", 15);
                    BtnActivePauseResume.Tag = "Retry";
                    BtnActivePauseResume.IsEnabled = true;
                    break;

                case DownloadState.Completed:
                    ActiveStateText.Text = "INSTALLED";
                    ActiveStateBadge.Background = (Brush)FindResource("BrushSurfaceHover");
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
                    break;
            }
        }

        private void RefreshQueueAndHistory()
        {
            var dm = DownloadManagerService.Instance;
            var remaining = dm.RemainingQueue;
            QueueItemsList.ItemsSource = remaining;
            QueueCountBadge.Text = remaining.Count.ToString();
            EmptyQueueBorder.Visibility = remaining.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var history = dm.CompletedHistory;
            HistoryItemsList.ItemsSource = history;
            HistoryCountBadge.Text = history.Count.ToString();
            EmptyHistoryBorder.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static double CalculateDynamicScale(double maxBps)
        {
            double[] niceSteps = new double[]
            {
                25 * 1024.0,       // 25 KB/s
                50 * 1024.0,       // 50 KB/s
                100 * 1024.0,      // 100 KB/s
                250 * 1024.0,      // 250 KB/s
                500 * 1024.0,      // 500 KB/s
                1024 * 1024.0,     // 1 MB/s
                2 * 1024 * 1024.0, // 2 MB/s
                5 * 1024 * 1024.0, // 5 MB/s
                10 * 1024 * 1024.0,// 10 MB/s
                20 * 1024 * 1024.0,// 20 MB/s
                30 * 1024 * 1024.0,// 30 MB/s
                50 * 1024 * 1024.0,// 50 MB/s
                100 * 1024 * 1024.0// 100 MB/s
            };

            double target = Math.Max(niceSteps[0], maxBps * 1.15);

            foreach (var step in niceSteps)
            {
                if (step >= target)
                    return step;
            }

            double unit = 25 * 1024 * 1024.0;
            return Math.Ceiling(target / unit) * unit;
        }

        private void RenderSpeedGraph()
        {
            double w = GraphCanvas.ActualWidth;
            double h = GraphCanvas.ActualHeight;

            if (w <= 10 || h <= 10) return;

            var history = DownloadManagerService.Instance.SpeedHistory;
            if (history.Count == 0) return;

            double currentBps = history.LastOrDefault();
            double windowMaxBps = history.Max();
            double scaleBps = CalculateDynamicScale(windowMaxBps);

            // Update axis labels dynamically with dynamic units (B/s, KB/s, MB/s, GB/s)
            YAxisMaxLabel.Text = DownloadManagerService.FormatSpeed(scaleBps);
            YAxisHalfLabel.Text = DownloadManagerService.FormatSpeed(scaleBps / 2.0);
            GraphPeakLabel.Text = $"Scale: {DownloadManagerService.FormatSpeed(scaleBps)}";
            GraphCurrentLabel.Text = $"Now: {DownloadManagerService.FormatSpeed(currentBps)}";

            // Reposition gridlines dynamically
            GridLine75.Y1 = GridLine75.Y2 = h * 0.25;
            GridLine50.Y1 = GridLine50.Y2 = h * 0.50;
            GridLine25.Y1 = GridLine25.Y2 = h * 0.75;
            GridLine75.X2 = GridLine50.X2 = GridLine25.X2 = w;

            int count = history.Count;
            PointCollection linePoints = new PointCollection();
            PointCollection areaPoints = new PointCollection();

            areaPoints.Add(new Point(0, h));

            double lastX = 0;
            double lastY = h;

            for (int i = 0; i < count; i++)
            {
                double x = (i / (double)(count - 1)) * w;
                double speed = history[i];
                double ratio = Math.Clamp(speed / scaleBps, 0.0, 1.0);
                double y = speed <= 0 ? h : (h - (ratio * (h - 14)) - 7);

                Point pt = new Point(x, y);
                linePoints.Add(pt);
                areaPoints.Add(pt);

                if (i == count - 1)
                {
                    lastX = x;
                    lastY = y;
                }
            }

            areaPoints.Add(new Point(w, h));

            GraphLine.Points = linePoints;
            GraphAreaPolygon.Points = areaPoints;

            if (Application.Current?.TryFindResource("BrushAccent") is SolidColorBrush accentBrush)
            {
                var accentCol = accentBrush.Color;
                GraphAreaPolygon.Fill = new LinearGradientBrush(
                    Color.FromArgb(0x40, accentCol.R, accentCol.G, accentCol.B),
                    Color.FromArgb(0x00, accentCol.R, accentCol.G, accentCol.B),
                    new Point(0, 0), new Point(0, 1));
            }

            if (currentBps > 1024)
            {
                GraphDot.Visibility = Visibility.Visible;
                Canvas.SetLeft(GraphDot, lastX - 4);
                Canvas.SetTop(GraphDot, lastY - 4);
            }
            else
            {
                GraphDot.Visibility = Visibility.Collapsed;
            }
        }

        private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderSpeedGraph();
        }

        // ================= Action Controls =================
        private void BtnPauseAll_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadManagerService.Instance.IsPaused)
            {
                DownloadManagerService.Instance.Resume();
            }
            else
            {
                DownloadManagerService.Instance.Pause();
            }
            UpdateTelemetry();
        }

        private void BtnClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.ClearCompletedHistory();
        }

        private void BtnCancelAll_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.CancelAll();
            DownloadManagerService.Instance.ResetBandwidthMonitor();
            RefreshAllUI();
        }

        private void BtnResetBandwidth_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.ResetBandwidthMonitor();
            RefreshAllUI();
        }

        private void BtnBrowseApps_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.GoToPage("SetupApps");
        }

        private void BtnActivePauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadManagerService.Instance.IsPaused)
            {
                DownloadManagerService.Instance.Resume();
            }
            else
            {
                DownloadManagerService.Instance.Pause();
            }
        }

        private void BtnActiveSkip_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.SkipCurrent();
        }

        private void BtnActiveCancel_Click(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.CancelAll();
            DownloadManagerService.Instance.ResetBandwidthMonitor();
            RefreshAllUI();
        }

        private void BtnActiveWebsite_Click(object sender, RoutedEventArgs e)
        {
            var app = DownloadManagerService.Instance.CurrentApp;
            if (app != null)
            {
                string? url = app.WebUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    url = AppMetadataHelper.ResolveOfficialUrl(app);
                }
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(app.Id))
                {
                    url = $"https://winget.run/pkg/{app.Id}";
                }
                if (!string.IsNullOrWhiteSpace(url))
                {
                    OpenBrowserUrl(url);
                }
            }
        }

        private void ActiveAppName_Click(object sender, MouseButtonEventArgs e)
        {
            BtnActiveWebsite_Click(sender, e);
        }

        private void QueueItemMoveToTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                DownloadManagerService.Instance.MoveToTop(app);
            }
        }

        private void QueueItemRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppItem app)
            {
                DownloadManagerService.Instance.RemoveFromQueue(app);
            }
        }

        private void HistoryItemWebsite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DownloadHistoryItem item)
            {
                if (!string.IsNullOrWhiteSpace(item.App.WebUrl))
                {
                    OpenBrowserUrl(item.App.WebUrl);
                }
            }
        }

        private void HistoryItemRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DownloadHistoryItem item)
            {
                DownloadManagerService.Instance.RemoveFromHistory(item);
            }
        }

        private static void OpenBrowserUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
