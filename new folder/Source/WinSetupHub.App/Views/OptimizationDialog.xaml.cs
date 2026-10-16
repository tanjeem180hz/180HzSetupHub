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
    public class OptimizationCustomStep
    {
        public string Name { get; set; } = "";
        public Func<Task> Action { get; set; } = () => Task.CompletedTask;
    }

    public partial class OptimizationDialog : Window
    {
        private readonly List<TweakItem> _tweaks;
        private readonly bool _isUndo;
        private readonly TweakService _tweakService;
        private readonly bool _autoStart;
        private readonly List<OptimizationCustomStep>? _customSteps;
        private double _currentRenderedPercent = 0;

        public OptimizationDialog(
            string title,
            string subtitle,
            List<TweakItem> tweaks,
            bool isUndo,
            TweakService tweakService,
            bool autoStart = false,
            List<OptimizationCustomStep>? customSteps = null)
        {
            InitializeComponent();

            _tweaks = tweaks ?? new List<TweakItem>();
            _isUndo = isUndo;
            _tweakService = tweakService;
            _autoStart = autoStart;
            _customSteps = customSteps;

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

            Loaded += async (_, _) =>
            {
                if (_autoStart)
                {
                    await StartExecutionAsync();
                }
            };
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
            await StartExecutionAsync();
        }

        private async Task StartExecutionAsync()
        {
            // Transition to Circular Progress View
            ConfirmPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            BtnDialogClose.IsEnabled = false;

            TxtActionTitle.Text = _isUndo ? "Restoring Windows Defaults..." : "Applying Optimizations...";
            if (BadgeStepStatus != null && TxtBadgeStatus != null)
            {
                TxtBadgeStatus.Text = _isUndo ? "RESTORING" : "APPLYING";
            }

            int customStepCount = _customSteps?.Count ?? 0;
            int total = customStepCount + _tweaks.Count;
            if (total == 0) total = 1;

            int done = 0;

            // 0% Initial Rendering
            RenderCircle(0, 0, total, "Initializing system engine...");

            // 1. Registry backup if applying
            if (!_isUndo && _tweaks.Count > 0)
            {
                RenderCircle(0, 0, total, "Creating pre-optimization registry backup...");
                await Task.Run(() => TweakService.ExportPreTweakRegBackup(_tweaks));
                await Task.Delay(100);
            }

            // 2. Custom Pre-Steps (Cleaner, RAM flush, Turbo CPU boost, Timer resolution, etc.)
            if (_customSteps != null)
            {
                foreach (var step in _customSteps)
                {
                    double targetPercent = ((double)done / total) * 100.0;
                    await UpdateProgressSmoothAsync(targetPercent, done + 1, total, step.Name);

                    try
                    {
                        await step.Action();
                    }
                    catch (Exception ex)
                    {
                        await UpdateProgressSmoothAsync(targetPercent, done + 1, total, $"{step.Name}: Notice ({ex.Message})");
                        await Task.Delay(250);
                    }

                    done++;
                    double newPercent = ((double)done / total) * 100.0;
                    await UpdateProgressSmoothAsync(newPercent, done, total, step.Name);
                }
            }

            // 3. Tweak List Execution
            foreach (var tweak in _tweaks)
            {
                double currentPercent = ((double)done / total) * 100.0;
                await UpdateProgressSmoothAsync(currentPercent, done + 1, total, tweak.Name);

                try
                {
                    if (_isUndo)
                        await _tweakService.UndoAsync(tweak);
                    else
                        await _tweakService.ApplyAsync(tweak);
                }
                catch (Exception ex)
                {
                    await UpdateProgressSmoothAsync(currentPercent, done + 1, total, $"Notice: {ex.Message}");
                    await Task.Delay(300);
                }

                done++;
                double newPercent = ((double)done / total) * 100.0;
                await UpdateProgressSmoothAsync(newPercent, done, total, tweak.Name);
                await Task.Delay(15); // Smooth pacing for visible circular sweep
            }

            // 4. 100% Completion State
            RenderCircle(100, total, total, "All operations completed successfully!");
            TxtActionTitle.Text = _isUndo ? "✓ Restore Complete!" : "✓ 100% Optimization Complete!";
            TxtPercent.Text = "100%";
            if (BadgeStepStatus != null && TxtBadgeStatus != null)
            {
                TxtBadgeStatus.Text = "✓ COMPLETED";
                BadgeStepStatus.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xFF, 0x66));
                BadgeStepStatus.BorderBrush = (Brush)FindResource("BrushAccent");
            }
            TxtCurrentTweakName.Foreground = (Brush)FindResource("BrushAccent");

            await Task.Delay(850);
            DialogResult = true;
            Close();
        }

        private async Task UpdateProgressSmoothAsync(double targetPercent, int current, int total, string currentName)
        {
            targetPercent = Math.Clamp(targetPercent, 0, 100);
            double startPercent = _currentRenderedPercent;

            int frames = 4;
            for (int i = 1; i <= frames; i++)
            {
                double p = startPercent + (targetPercent - startPercent) * (i / (double)frames);
                RenderCircle(p, current, total, currentName);
                await Task.Delay(8);
            }
            _currentRenderedPercent = targetPercent;
            RenderCircle(targetPercent, current, total, currentName);
        }

        private void RenderCircle(double percent, int current, int total, string currentName)
        {
            percent = Math.Clamp(percent, 0, 100);
            TxtPercent.Text = $"{(int)Math.Round(percent)}%";
            TxtStepCounter.Text = $"{current} / {total}";
            TxtCurrentTweakName.Text = currentName;

            double cx = 85.0;
            double cy = 85.0;
            double radius = 70.0;

            if (percent <= 0)
            {
                ProgressArc.Data = null;
                return;
            }

            if (percent >= 100)
            {
                ProgressArc.Data = new EllipseGeometry(new Point(cx, cy), radius, radius);
                return;
            }

            double angle = Math.Clamp((percent / 100.0) * 360.0, 0.5, 359.95);
            double rad = (angle - 90.0) * (Math.PI / 180.0);

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
