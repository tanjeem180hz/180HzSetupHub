using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class LeftoverCleanupDialog : Window
    {
        private readonly AppItem _app;
        private readonly DeepUninstallService _deepUninstall = new();
        private readonly ObservableCollection<LeftoverViewModel> _viewModels = new();
        private bool _isDeleting;

        public LeftoverCleanupDialog(AppItem app, List<LeftoverItem> leftovers)
        {
            InitializeComponent();
            _app = app;

            DialogTitleText.Text = $"Residual Traces: {app.Name}";
            DialogSubtitleText.Text = $"Detected {leftovers.Count} registry and filesystem leftover(s) for {app.Name}.";

            foreach (var item in leftovers)
            {
                var vm = new LeftoverViewModel(item, app.Name);
                vm.PropertyChanged += (_, _) => UpdateCountDisplay();
                _viewModels.Add(vm);
            }

            LeftoversListBox.ItemsSource = _viewModels;
            UpdateCountDisplay();

            MouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isDeleting)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        private void UpdateCountDisplay()
        {
            if (_isDeleting) return;

            int total = _viewModels.Count;
            int selected = _viewModels.Count(v => v.IsSelected);
            CountSummaryText.Text = $"{selected} of {total} items selected for deletion";
            DeleteButton.Content = selected > 0 ? $"🗑️ Delete Selected ({selected})" : "🗑️ Delete Selected";
            DeleteButton.IsEnabled = selected > 0;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            foreach (var vm in _viewModels)
            {
                vm.IsSelected = true;
            }
            UpdateCountDisplay();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeleting) return;
            foreach (var vm in _viewModels)
            {
                vm.IsSelected = false;
            }
            UpdateCountDisplay();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isDeleting)
            {
                Close();
            }
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedVms = _viewModels.Where(v => v.IsSelected).ToList();
            if (selectedVms.Count == 0 || _isDeleting) return;

            _isDeleting = true;

            // Lock header and toolbar buttons
            DialogCloseButton.IsEnabled = false;
            SelectAllBtn.IsEnabled = false;
            DeselectAllBtn.IsEnabled = false;

            // Transition from action buttons to inline progress panel (No popups!)
            FooterActionsPanel.Visibility = Visibility.Collapsed;
            FooterProgressPanel.Visibility = Visibility.Visible;

            int total = selectedVms.Count;
            PurgeProgressBar.Minimum = 0;
            PurgeProgressBar.Maximum = 100;
            PurgeProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            ProgressStatusText.Text = $"Purging residual traces (0 of {total})…";
            CurrentTracePathText.Text = "Initializing deep clean engine…";

            var progress = new Progress<LeftoverDeleteProgress>(p =>
            {
                // Find matching viewmodel and mark state
                var vm = selectedVms.FirstOrDefault(v => string.Equals(v.Path, p.Item.Path, StringComparison.OrdinalIgnoreCase));
                if (vm != null)
                {
                    vm.IsProcessing = false;
                    vm.IsDeleted = p.Success;
                    vm.IsFailed = !p.Success;
                    try
                    {
                        LeftoversListBox.ScrollIntoView(vm);
                    }
                    catch { }
                }

                // Smoothly animate progress bar to target percentage (0% -> 100%)
                double targetPercent = ((double)p.Current / p.Total) * 100.0;
                var anim = new DoubleAnimation
                {
                    To = targetPercent,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                PurgeProgressBar.BeginAnimation(RangeBase.ValueProperty, anim);

                ProgressPercentText.Text = $"{(int)targetPercent}%";
                ProgressStatusText.Text = $"Purging residual trace {p.Current} of {p.Total}…";
                CurrentTracePathText.Text = p.Item.Path;
            });

            // Mark first item as processing
            if (selectedVms.Count > 0)
            {
                selectedVms[0].IsProcessing = true;
            }

            var (deleted, failed) = await _deepUninstall.DeleteAsync(selectedVms.Select(v => v.Item), progress);

            // Final 100% animation and success state
            var finalAnim = new DoubleAnimation
            {
                To = 100,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            PurgeProgressBar.BeginAnimation(RangeBase.ValueProperty, finalAnim);

            ProgressPercentText.Text = "100%";
            ProgressIconText.Text = "✓";

            var successBrush = (Brush)FindResource("BrushSuccess");
            ProgressIconText.Foreground = successBrush;
            ProgressPercentText.Foreground = successBrush;
            PurgeProgressBar.Foreground = successBrush;

            ProgressStatusText.Text = failed == 0
                ? $"Deep Clean Completed! {deleted} residual trace(s) eradicated."
                : $"Deep Clean Finished! Removed {deleted} item(s), skipped {failed}.";

            CurrentTracePathText.Text = "Residual traces successfully purged from Windows. Auto-closing…";

            // Non-intrusive notification (No MessageBox popup!)
            NotificationService.Notify(
                "Leftovers Cleaned",
                $"Permanently eradicated {deleted} residual trace(s) for {_app.Name}.");

            // Smooth fade-out and auto-close
            await Task.Delay(900);

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                _isDeleting = false;
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
