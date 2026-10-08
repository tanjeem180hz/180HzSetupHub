using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SetupHub180Hz.Views;

public partial class SplashScreenWindow : Window
{
    private double _currentValue = 0;

    public SplashScreenWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Smoothly updates the minimalist progress bar.
    /// </summary>
    public void UpdateProgress(double targetPercent, string? statusText = null)
    {
        Dispatcher.Invoke(() =>
        {
            var anim = new DoubleAnimation
            {
                From = _currentValue,
                To = targetPercent,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            _currentValue = targetPercent;
            StartupProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
        });
    }

    /// <summary>
    /// Smoothly animates splash screen opacity to zero and closes the window.
    /// </summary>
    public Task FadeOutAndCloseAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Invoke(() =>
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOut.Completed += (_, _) =>
            {
                try
                {
                    Close();
                }
                catch { }
                tcs.TrySetResult(true);
            };

            BeginAnimation(OpacityProperty, fadeOut);
        });

        return tcs.Task;
    }
}
