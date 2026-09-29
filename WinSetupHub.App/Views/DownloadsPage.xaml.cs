using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        }

        private void DownloadsPage_Loaded(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.SpeedSampled += OnSpeedSampled;
            DownloadManagerService.Instance.ProgressChanged += OnProgressChanged;
            DownloadManagerService.Instance.QueueChanged += OnQueueChanged;
            DownloadManagerService.Instance.QueueCompleted += OnQueueCompleted;

            RefreshAllUI();
        }

        private void DownloadsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DownloadManagerService.Instance.SpeedSampled -= OnSpeedSampled;
            DownloadManagerService.Instance.ProgressChanged -= OnProgressChanged;
            DownloadManagerService.Instance.QueueChanged -= OnQueueChanged;
            DownloadManagerService.Instance.QueueCompleted -= OnQueueCompleted;
        }

        private void OnSpeedSampled(double mbps)
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
                SetStatusBadge("IDLE", "#8B95A8", "#1C2538");
                BtnPauseAll.Content = "⏸ Pause All";
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
                    SetStatusBadge("PAUSED", "#F5B84C", "#2E2413");
                    BtnPauseAll.Content = "▶ Resume All";
                }
                else
                {
                    SetStatusBadge("DOWNLOADING", "#3FCB7E", "#132E22");
                    BtnPauseAll.Content = "⏸ Pause All";
                }
            }
            else
            {
                SetStatusBadge("IDLE", "#8B95A8", "#1C2538");
                BtnPauseAll.Content = "⏸ Pause All";
                BtnPauseAll.IsEnabled = false;
            }
        }

        private void SetStatusBadge(string text, string colorHex, string bgHex)
        {
            LiveStatusText.Text = text;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            var bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
            LiveStatusText.Foreground = brush;
            LiveStatusDot.Fill = brush;
            LiveStatusBadge.Background = bgBrush;
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

            double targetPct = Math.Clamp(info.Percentage, 0, 100);
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = targetPct,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            ActiveProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
            ActiveProgressPercent.Text = $"{info.Percentage:0}%";
            ActiveSpeedText.Text = info.SpeedFormatted;
            ActiveEtaText.Text = info.EtaFormatted;
            ActiveTransferredText.Text = string.IsNullOrWhiteSpace(info.SizeFormatted) ? "" : info.SizeFormatted;
            ActiveQueueCountText.Text = $"App {info.QueueIndex} of {info.QueueTotal}";

            switch (info.State)
            {
                case DownloadState.Downloading:
                    ActiveStateText.Text = "DOWNLOADING";
                    ActiveStateBadge.Background = new SolidColorBrush(Color.FromRgb(18, 45, 66));
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushAccent");
                    BtnActivePauseResume.Content = "⏸ Pause";
                    BtnActivePauseResume.IsEnabled = true;
                    break;

                case DownloadState.Paused:
                    ActiveStateText.Text = "PAUSED";
                    ActiveStateBadge.Background = new SolidColorBrush(Color.FromRgb(55, 40, 10));
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushWarning");
                    BtnActivePauseResume.Content = "▶ Resume";
                    BtnActivePauseResume.IsEnabled = true;
                    break;

                case DownloadState.Installing:
                    ActiveStateText.Text = "INSTALLING";
                    ActiveStateBadge.Background = new SolidColorBrush(Color.FromRgb(35, 25, 60));
                    ActiveStateText.Foreground = new SolidColorBrush(Color.FromRgb(180, 140, 255));
                    BtnActivePauseResume.IsEnabled = false;
                    break;

                case DownloadState.Error:
                    ActiveStateText.Text = "CONNECTION ERROR";
                    ActiveStateBadge.Background = new SolidColorBrush(Color.FromRgb(65, 18, 25));
                    ActiveStateText.Foreground = (SolidColorBrush)FindResource("BrushError");
                    BtnActivePauseResume.Content = "🔄 Retry";
                    BtnActivePauseResume.IsEnabled = true;
                    break;

                case DownloadState.Completed:
                    ActiveStateText.Text = "INSTALLED";
                    ActiveStateBadge.Background = new SolidColorBrush(Color.FromRgb(18, 55, 25));
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

        private void RenderSpeedGraph()
        {
            double w = GraphCanvas.ActualWidth;
            double h = GraphCanvas.ActualHeight;

            if (w <= 10 || h <= 10) return;

            var history = DownloadManagerService.Instance.SpeedHistory;
            if (history.Count == 0) return;

            double currentSpeed = history.LastOrDefault();
            double peakSession = DownloadManagerService.Instance.PeakSpeedBps / (1024.0 * 1024.0);
            double maxSpeed = Math.Max(5.0, Math.Max(peakSession, history.Max()));

            // Update axis labels
            YAxisMaxLabel.Text = $"{maxSpeed:0.0} MB/s";
            YAxisHalfLabel.Text = $"{(maxSpeed / 2.0):0.0} MB/s";
            GraphPeakLabel.Text = $"Scale: {maxSpeed:0.0} MB/s";
            GraphCurrentLabel.Text = $"Now: {currentSpeed:0.1} MB/s";

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
                double ratio = Math.Clamp(speed / maxSpeed, 0.0, 1.0);
                double y = h - (ratio * (h - 14)) - 7;

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

            if (currentSpeed > 0.01)
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
            var res = MessageBox.Show(
                "Are you sure you want to cancel all pending and active downloads in the queue?",
                "Cancel Queue",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                DownloadManagerService.Instance.CancelAll();
            }
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
        }

        private void BtnActiveWebsite_Click(object sender, RoutedEventArgs e)
        {
            var app = DownloadManagerService.Instance.CurrentApp;
            if (app != null && !string.IsNullOrWhiteSpace(app.WebUrl))
            {
                OpenBrowserUrl(app.WebUrl);
            }
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
