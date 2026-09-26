using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsSetupHub.Pages
{
    public partial class InstallPage : Page
    {
        private DispatcherTimer? _progressTimer;
        private int _progressValue = 0;
        private string _currentApp = "";

        public InstallPage()
        {
            InitializeComponent();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Search logic here
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            // Filter logic here
        }

        private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border)
                border.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x38));
        }

        private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border)
                border.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
        }

        private void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _currentApp = btn.Tag?.ToString() ?? "App";
                StartInstall();
            }
        }

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _currentApp = btn.Tag?.ToString() ?? "App";
                ProgressLabel.Text = $"Updating {_currentApp}...";
                StartInstall();
            }
        }

        private void StartInstall()
        {
            ProgressSection.Visibility = Visibility.Visible;
            ProgressLabel.Text = $"Installing {_currentApp}...";
            _progressValue = 0;
            InstallProgressBar.Value = 0;

            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(80);
            _progressTimer.Tick += ProgressTick;
            _progressTimer.Start();
        }

        private void ProgressTick(object? sender, EventArgs e)
        {
            _progressValue += new Random().Next(1, 4);
            if (_progressValue >= 100)
            {
                _progressValue = 100;
                _progressTimer?.Stop();
                InstallProgressBar.Value = 100;
                ProgressPct.Text = "100% — Completed!";
                ProgressLabel.Text = $"{_currentApp} installed successfully!";

                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += (s, ev) =>
                {
                    ProgressSection.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
                return;
            }

            InstallProgressBar.Value = _progressValue;
            ProgressPct.Text = $"{_progressValue}% • Downloading...";
        }

        private void CancelInstall_Click(object sender, RoutedEventArgs e)
        {
            _progressTimer?.Stop();
            ProgressSection.Visibility = Visibility.Collapsed;
            _progressValue = 0;
        }
    }
}
