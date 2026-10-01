using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class OptimizationDialog : Window
    {
        private readonly List<TweakItem> _tweaks;
        private readonly bool _isUndo;
        private readonly TweakService _tweakService;

        public OptimizationDialog(
            string title,
            string subtitle,
            List<TweakItem> tweaks,
            bool isUndo,
            TweakService tweakService)
        {
            InitializeComponent();

            _tweaks = tweaks ?? new List<TweakItem>();
            _isUndo = isUndo;
            _tweakService = tweakService;

            TxtDialogTitle.Text = title;
            TxtDialogSubtitle.Text = subtitle;
            TxtHeaderIcon.Text = isUndo ? "↩" : "⚡";

            BtnProceed.Content = isUndo ? "RESTORE NOW" : "APPLY NOW";
            TxtConfirmPrompt.Text = isUndo ? "Restore standard Windows defaults?" : "Apply these optimizations now?";

            // Bind tweak list
            TweakItemsList.ItemsSource = _tweaks;

            // Show caution banner if any tweak is advanced
            if (!isUndo && _tweaks.Any(t => t.IsAdvanced))
            {
                CautionWarningBox.Visibility = Visibility.Visible;
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void BtnProceed_Click(object sender, RoutedEventArgs e)
        {
            // Transition to Circular Progress View
            ConfirmPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            BtnDialogClose.IsEnabled = false;

            TxtActionTitle.Text = _isUndo ? "Restoring Windows Defaults..." : "Applying Optimizations...";

            int total = _tweaks.Count;
            int done = 0;

            UpdateProgress(0, 0, total, "Initializing...");

            foreach (var tweak in _tweaks)
            {
                double currentPercent = total > 0 ? ((double)done / total) * 100.0 : 0.0;
                UpdateProgress(currentPercent, done + 1, total, tweak.Name);
                await Task.Delay(40); // yield for smooth animation

                try
                {
                    if (_isUndo)
                        await _tweakService.UndoAsync(tweak);
                    else
                        await _tweakService.ApplyAsync(tweak);
                }
                catch (Exception ex)
                {
                    UpdateProgress(currentPercent, done + 1, total, $"Error: {ex.Message}");
                    await Task.Delay(600);
                }

                done++;
                double newPercent = total > 0 ? ((double)done / total) * 100.0 : 100.0;
                UpdateProgress(newPercent, done, total, tweak.Name);
            }

            // 100% Completion State
            UpdateProgress(100, total, total, "All operations completed successfully.");
            TxtActionTitle.Text = _isUndo ? "✓ Restore Complete!" : "✓ Optimization Complete!";
            TxtPercent.Text = "100%";
            TxtCurrentTweakName.Foreground = (Brush)FindResource("BrushAccent");

            // Auto-close automatically after reaching 100%
            await Task.Delay(800);
            DialogResult = true;
            Close();
        }

        private void UpdateProgress(double percent, int current, int total, string currentName)
        {
            percent = Math.Clamp(percent, 0, 100);
            TxtPercent.Text = $"{(int)percent}%";
            TxtStepCounter.Text = $"{current} / {total}";
            TxtCurrentTweakName.Text = currentName;

            if (percent <= 0)
            {
                ProgressArc.Data = null;
                return;
            }

            double cx = 65;
            double cy = 65;
            double radius = 55;

            double angle = (percent / 100.0) * 359.999;
            double rad = (angle - 90) * (Math.PI / 180.0);

            double endX = cx + radius * Math.Cos(rad);
            double endY = cy + radius * Math.Sin(rad);

            var figure = new PathFigure
            {
                StartPoint = new Point(cx, cy - radius),
                IsClosed = false
            };

            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(radius, radius),
                IsLargeArc = angle > 180.0,
                SweepDirection = SweepDirection.Clockwise
            });

            var geom = new PathGeometry();
            geom.Figures.Add(figure);
            ProgressArc.Data = geom;
        }
    }
}
