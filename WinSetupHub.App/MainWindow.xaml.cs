using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SetupHub180Hz.Services;
using SetupHub180Hz.Views;

namespace SetupHub180Hz
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarPinned;
        private bool _isSidebarExpanded;
        private string _currentPageKey = "Dashboard";
        private readonly Dictionary<string, UserControl> _pageCache = new();

        public MainWindow()
        {
            InitializeComponent();

            StateChanged += (_, _) =>
            {
                bool isMax = WindowState == WindowState.Maximized;
                MaximizeIconPath.Data = Geometry.Parse(
                    isMax ? "M 2.5,0.5 H 9.5 V 7.5 H 2.5 Z M 0.5,2.5 H 7.5 V 9.5 H 0.5 Z"
                          : "M 0.5,0.5 H 9.5 V 9.5 H 0.5 Z");
                MaximizeButton.ToolTip = isMax ? "Restore Down" : "Maximize";
            };

            Activated += async (_, _) =>
            {
                if (!UpdateMonitorService.Instance.ShouldSkipDueToRecency())
                {
                    await UpdateMonitorService.Instance.RefreshAsync();
                }
            };

            ThemeService.ThemeChanged += OnThemeChanged;
            UpdateThemeButtonText();

            DownloadManagerService.Instance.QueueChanged += UpdateDownloadsBadge;
            DownloadManagerService.Instance.ProgressChanged += _ => UpdateDownloadsBadge();
            DownloadManagerService.Instance.QueueCompleted += UpdateDownloadsBadge;
            UpdateDownloadsBadge();

            // Initialize sidebar in collapsed compact mode
            CollapseSidebar(animate: false);

            NavigateTo("Dashboard");
        }

        private void OnThemeChanged(bool isDark)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateThemeButtonText();

                // Reapply active brushes to the shell window elements
                Background = (Brush)Application.Current.FindResource("BrushBackground");
                TitleBarGrid.Background = (Brush)Application.Current.FindResource("BrushSurface");
                SidebarBorder.Background = (Brush)Application.Current.FindResource("BrushBackground");
                SidebarBorder.BorderBrush = (Brush)Application.Current.FindResource("BrushBorder");

                // Clear page cache and re-navigate so views cleanly rehydrate under the new theme
                _pageCache.Clear();
                if (!string.IsNullOrWhiteSpace(_currentPageKey))
                {
                    NavigateTo(_currentPageKey);
                }
            });
        }

        // ================= Collapsible Animated Cyber Sidebar =================
        private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isSidebarPinned && !_isSidebarExpanded)
            {
                ExpandSidebar(animate: true);
            }
        }

        private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isSidebarPinned && _isSidebarExpanded)
            {
                CollapseSidebar(animate: true);
            }
        }

        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarPinned = !_isSidebarPinned;

            if (_isSidebarPinned)
            {
                SidebarPinIcon.Text = "🔒";
                BtnPinSidebar.ToolTip = "Unlock Sidebar (Auto-collapse)";
                ExpandSidebar(animate: true);
            }
            else
            {
                SidebarPinIcon.Text = "📌";
                BtnPinSidebar.ToolTip = "Lock Sidebar Expanded";
                CollapseSidebar(animate: true);
            }
        }

        private void ExpandSidebar(bool animate)
        {
            _isSidebarExpanded = true;
            double targetWidth = 220;

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                SidebarBorder.BeginAnimation(WidthProperty, anim);
            }
            else
            {
                SidebarBorder.BeginAnimation(WidthProperty, null);
                SidebarBorder.Width = targetWidth;
            }

            AnimateSidebarLabels(1.0, animate);
        }

        private void CollapseSidebar(bool animate)
        {
            _isSidebarExpanded = false;
            double targetWidth = 68;

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                SidebarBorder.BeginAnimation(WidthProperty, anim);
            }
            else
            {
                SidebarBorder.BeginAnimation(WidthProperty, null);
                SidebarBorder.Width = targetWidth;
            }

            AnimateSidebarLabels(0.0, animate);
        }

        private void AnimateSidebarLabels(double targetOpacity, bool animate)
        {
            var textElements = new FrameworkElement?[]
            {
                SidebarBrandText,
                BtnPinSidebar,
                NavDashboardLabel,
                NavSetupAppsLabel,
                NavDownloadsLabel,
                NavUpdateCenterLabel,
                NavUninstallerLabel,
                NavStartupLabel,
                NavCleanupLabel,
                NavActivityLabel,
                NavStorageLabel,
                NavSettingsLabel,
                SidebarThemeLabel
            };

            foreach (var elem in textElements)
            {
                if (elem == null) continue;

                if (animate)
                {
                    var anim = new DoubleAnimation
                    {
                        To = targetOpacity,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    elem.BeginAnimation(OpacityProperty, anim);
                }
                else
                {
                    elem.BeginAnimation(OpacityProperty, null);
                    elem.Opacity = targetOpacity;
                }
            }
        }

        private void UpdateDownloadsBadge()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var dm = DownloadManagerService.Instance;
                int activeAndQueued = dm.QueueRemaining + (dm.IsRunning ? 1 : 0);
                if (activeAndQueued > 0)
                {
                    DownloadsNavBadge.Visibility = Visibility.Visible;
                    DownloadsNavBadgeText.Text = activeAndQueued.ToString();
                }
                else
                {
                    DownloadsNavBadge.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void NavItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                NavigateTo(tag);
        }

        private void NavigateTo(string pageKey)
        {
            _currentPageKey = pageKey;

            if (!_pageCache.TryGetValue(pageKey, out var page))
            {
                page = pageKey switch
                {
                    "Dashboard" => new DashboardPage(this),
                    "SetupApps" => new SetupAppsPage(),
                    "Downloads" => new DownloadsPage(this),
                    "UpdateCenter" => new UpdateCenterPage(),
                    "Uninstaller" => new UninstallerPage(),
                    "Startup" => new StartupManagerPage(),
                    "Cleanup" => new CleanupPage(),
                    "Activity" => new ActivityPage(),
                    "Storage" => new StoragePage(),
                    "Settings" => new SettingsPage(),
                    _ => new DashboardPage(this),
                };
                _pageCache[pageKey] = page;
            }

            ContentHost.Content = page;
            PlayEnterAnimation(ContentHost);
        }

        public void SetStatus(string text)
        {
            // Status bar removed per user request; kept as safe no-op for backward compatibility
        }

        public void GoToPage(string pageKey)
        {
            RadioButton? target = pageKey switch
            {
                "Dashboard" => NavDashboard,
                "SetupApps" => NavSetupApps,
                "Downloads" => NavDownloads,
                "UpdateCenter" => NavUpdateCenter,
                "Uninstaller" => NavUninstaller,
                "Startup" => NavStartup,
                "Cleanup" => NavCleanup,
                "Activity" => NavActivity,
                "Storage" => NavStorage,
                "Settings" => NavSettings,
                _ => null,
            };
            if (target != null) target.IsChecked = true;
        }

        private void PlayEnterAnimation(FrameworkElement target)
        {
            if (TryFindResource("PageEnterAnimation") is not Storyboard template) return;
            var clone = template.Clone();
            Storyboard.SetTarget(clone, target);
            clone.Begin();
        }

        // ===== Custom chrome & theme controls =====
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            var currentDark = SettingsService.Instance.Current.DarkTheme;
            ThemeService.ApplyTheme(!currentDark);
        }

        private void UpdateThemeButtonText()
        {
            var isDark = SettingsService.Instance.Current.DarkTheme;

            // Sidebar Theme Button in bottom-left corner
            if (SidebarThemeIcon != null)
                SidebarThemeIcon.Text = isDark ? "☀️" : "🌙";
            if (SidebarThemeLabel != null)
                SidebarThemeLabel.Text = isDark ? "Light Theme" : "Dark Theme";
            if (SidebarThemeButton != null)
                SidebarThemeButton.ToolTip = isDark ? "Switch to Light Theme" : "Switch to Dark Theme";
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
