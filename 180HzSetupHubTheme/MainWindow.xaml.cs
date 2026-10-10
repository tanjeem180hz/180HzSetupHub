using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using SetupHub180Hz.Services;
using SetupHub180Hz.Views;

namespace SetupHub180Hz
{
    public partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        private bool _isSidebarPinned;
        private bool _isSidebarExpanded;
        private string _currentPageKey = "";
        private readonly Dictionary<string, UserControl> _pageCache = new();
        private readonly Stack<string> _backStack = new();
        private readonly Stack<string> _forwardStack = new();
        private bool _isNavigatingHistory = false;

        public bool CanGoBack => _backStack.Count > 0;
        public bool CanGoForward => _forwardStack.Count > 0;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            StateChanged += (_, _) =>
            {
                bool isMax = WindowState == WindowState.Maximized;
                MaximizeIconPath.Data = Geometry.Parse(
                    isMax ? "M 2.5,0.5 H 9.5 V 7.5 H 2.5 Z M 0.5,2.5 H 7.5 V 9.5 H 0.5 Z"
                          : "M 0.5,0.5 H 9.5 V 9.5 H 0.5 Z");
                MaximizeButton.ToolTip = isMax ? "Restore Down" : "Maximize";
                if (RootGrid != null)
                {
                    RootGrid.Margin = new Thickness(0);
                }
                if (_pageCache.TryGetValue(_currentPageKey, out var cur) && cur != null)
                {
                    PlayEnterAnimation(cur);
                }
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
            DownloadManagerService.Instance.QueueCancelled += UpdateDownloadsBadge;
            UpdateDownloadsBadge();

            // Initialize sidebar in collapsed compact mode
            CollapseSidebar(animate: false);

            // Automatically start cleanup background scan immediately when app opens
            CleanupService.Instance.StartBackgroundScan();

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
                PagesHost.Children.Clear();
                _pageCache.Clear();
                if (!string.IsNullOrWhiteSpace(_currentPageKey))
                {
                    string current = _currentPageKey;
                    _currentPageKey = "";
                    NavigateTo(current);
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
                    Duration = TimeSpan.FromMilliseconds(260),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
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
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
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
                NavOptimizationLabel,
                NavSettingsLabel,
                SidebarThemeLabel
            };

            bool isExpanding = targetOpacity > 0.5;

            foreach (var elem in textElements)
            {
                if (elem == null) continue;

                if (elem.RenderTransform is not TranslateTransform tt)
                {
                    tt = new TranslateTransform();
                    elem.RenderTransform = tt;
                }

                if (animate)
                {
                    var opacAnim = new DoubleAnimation
                    {
                        To = targetOpacity,
                        Duration = TimeSpan.FromMilliseconds(isExpanding ? 240 : 160),
                        EasingFunction = new QuarticEase { EasingMode = isExpanding ? EasingMode.EaseOut : EasingMode.EaseIn }
                    };
                    elem.BeginAnimation(OpacityProperty, opacAnim);

                    var transAnim = new DoubleAnimation
                    {
                        To = isExpanding ? 0 : -8,
                        Duration = TimeSpan.FromMilliseconds(isExpanding ? 260 : 180),
                        EasingFunction = new QuarticEase { EasingMode = isExpanding ? EasingMode.EaseOut : EasingMode.EaseIn }
                    };
                    tt.BeginAnimation(TranslateTransform.XProperty, transAnim);
                }
                else
                {
                    elem.BeginAnimation(OpacityProperty, null);
                    elem.Opacity = targetOpacity;
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                    tt.X = isExpanding ? 0 : -8;
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

        private DispatcherTimer? _memoryTrimDebounceTimer;

        public void GoBack()
        {
            if (!CanGoBack) return;
            _isNavigatingHistory = true;
            try
            {
                var target = _backStack.Pop();
                _forwardStack.Push(_currentPageKey);
                GoToPage(target);
            }
            finally
            {
                _isNavigatingHistory = false;
                UpdateNavButtonsState();
            }
        }

        public void GoForward()
        {
            if (!CanGoForward) return;
            _isNavigatingHistory = true;
            try
            {
                var target = _forwardStack.Pop();
                _backStack.Push(_currentPageKey);
                GoToPage(target);
            }
            finally
            {
                _isNavigatingHistory = false;
                UpdateNavButtonsState();
            }
        }

        private void NavBack_Click(object sender, RoutedEventArgs e) => GoBack();
        private void NavForward_Click(object sender, RoutedEventArgs e) => GoForward();

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1)
            {
                if (CanGoBack)
                {
                    GoBack();
                    e.Handled = true;
                }
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                if (CanGoForward)
                {
                    GoForward();
                    e.Handled = true;
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (e.SystemKey == Key.Left || e.Key == Key.Left)
                {
                    if (CanGoBack)
                    {
                        GoBack();
                        e.Handled = true;
                    }
                }
                else if (e.SystemKey == Key.Right || e.Key == Key.Right)
                {
                    if (CanGoForward)
                    {
                        GoForward();
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.Back && !(Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox))
            {
                if (CanGoBack)
                {
                    GoBack();
                    e.Handled = true;
                }
            }
        }

        private void UpdateNavButtonsState()
        {
            if (BtnNavBack != null)
            {
                BtnNavBack.IsEnabled = CanGoBack;
                BtnNavBack.ToolTip = CanGoBack
                    ? $"Go Back to {GetPageFriendlyTitle(_backStack.Peek())} (Alt+Left, Mouse 4)"
                    : "Go Back (Alt+Left, Mouse 4)";
            }

            if (BtnNavForward != null)
            {
                BtnNavForward.IsEnabled = CanGoForward;
                BtnNavForward.ToolTip = CanGoForward
                    ? $"Go Forward to {GetPageFriendlyTitle(_forwardStack.Peek())} (Alt+Right, Mouse 5)"
                    : "Go Forward (Alt+Right, Mouse 5)";
            }
        }

        private static string GetPageFriendlyTitle(string? key) => key switch
        {
            "Dashboard" => "Dashboard",
            "SetupApps" => "Setup Apps",
            "Downloads" => "Downloads",
            "UpdateCenter" => "Update Center",
            "Uninstaller" => "Uninstaller",
            "Startup" => "Startup Apps",
            "Cleanup" => "System Cleaner",
            "Activity" => "Activity Log",
            "Storage" => "Storage Analysis",
            "Optimization" => "PC Optimization",
            "Settings" => "Settings",
            _ => string.IsNullOrEmpty(key) ? "Dashboard" : key
        };

        private void NavigateTo(string pageKey)
        {
            if (!string.IsNullOrEmpty(_currentPageKey) && _currentPageKey == pageKey)
            {
                UpdateNavButtonsState();
                return;
            }

            if (!_isNavigatingHistory && !string.IsNullOrEmpty(_currentPageKey) && _currentPageKey != pageKey)
            {
                _backStack.Push(_currentPageKey);
                _forwardStack.Clear();
            }

            _currentPageKey = pageKey;

            // Keep sidebar active selection synchronized
            RadioButton? targetRb = pageKey switch
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
                "Optimization" => NavOptimization,
                _ => null,
            };
            if (targetRb != null && targetRb.IsChecked != true)
            {
                targetRb.IsChecked = true;
            }

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
                    "Optimization" => new OptimizationPage(),
                    _ => new DashboardPage(this),
                };
                _pageCache[pageKey] = page;
                PagesHost.Children.Add(page);
            }

            // Zero-latency instant visibility toggle across all cached pages (0ms visual tree rebuild)
            foreach (UIElement child in PagesHost.Children)
            {
                if (ReferenceEquals(child, page))
                {
                    if (child.Visibility != Visibility.Visible)
                    {
                        child.Visibility = Visibility.Visible;
                        PlayEnterAnimation((FrameworkElement)child);

                        if (child is IRealtimeRefreshable refreshable)
                        {
                            refreshable.RefreshRealtime();
                        }
                    }
                }
                else
                {
                    if (child.Visibility != Visibility.Collapsed)
                    {
                        child.Visibility = Visibility.Collapsed;
                    }
                }
            }

            UpdateNavButtonsState();
            ScheduleBackgroundMemoryTrim();
        }

        private void ScheduleBackgroundMemoryTrim()
        {
            if (_memoryTrimDebounceTimer == null)
            {
                _memoryTrimDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2.5)
                };
                _memoryTrimDebounceTimer.Tick += (_, _) =>
                {
                    _memoryTrimDebounceTimer.Stop();
                    Task.Run(() => MemoryCleaner.TrimCurrentProcessMemory());
                };
            }
            _memoryTrimDebounceTimer.Stop();
            _memoryTrimDebounceTimer.Start();
        }

        public void TriggerRealtimeRefresh()
        {
            try
            {
                if (_pageCache.TryGetValue(_currentPageKey, out var activePage) &&
                    activePage is IRealtimeRefreshable refreshable &&
                    activePage.Visibility == Visibility.Visible)
                {
                    refreshable.RefreshRealtime();
                }
                UpdateDownloadsBadge();
            }
            catch { }
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
                "Optimization" => NavOptimization,
                _ => null,
            };

            if (target != null)
            {
                if (target.IsChecked == true)
                {
                    NavigateTo(pageKey);
                }
                else
                {
                    target.IsChecked = true;
                }
            }
            else
            {
                NavigateTo(pageKey);
            }
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_APPCOMMAND)
            {
                int cmd = HIWORD(lParam) & ~FAPPCOMMAND_MASK;
                if (cmd == APPCOMMAND_BROWSER_BACKWARD)
                {
                    if (CanGoBack) { GoBack(); handled = true; return new IntPtr(1); }
                }
                else if (cmd == APPCOMMAND_BROWSER_FORWARD)
                {
                    if (CanGoForward) { GoForward(); handled = true; return new IntPtr(1); }
                }
            }
            else if (msg == WM_XBUTTONDOWN)
            {
                int button = HIWORD(wParam);
                if (button == XBUTTON1)
                {
                    if (CanGoBack) { GoBack(); handled = true; return new IntPtr(1); }
                }
                else if (button == XBUTTON2)
                {
                    if (CanGoForward) { GoForward(); handled = true; return new IntPtr(1); }
                }
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    var rcWork = mi.rcWork;
                    var rcMonitor = mi.rcMonitor;

                    mmi.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left);
                    mmi.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top);
                    mmi.ptMaxSize.x = Math.Abs(rcWork.right - rcWork.left);
                    mmi.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top);
                    mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
                    mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        #region Native Win32 MinMax & Navigation Interop
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_APPCOMMAND = 0x0319;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int APPCOMMAND_BROWSER_BACKWARD = 1;
        private const int APPCOMMAND_BROWSER_FORWARD = 2;
        private const int FAPPCOMMAND_MASK = 0xF000;
        private const int XBUTTON1 = 0x0001;
        private const int XBUTTON2 = 0x0002;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private static int HIWORD(IntPtr ptr)
        {
            unchecked
            {
                long val = ptr.ToInt64();
                return (int)((val >> 16) & 0xFFFF);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }
        #endregion
    }
}
