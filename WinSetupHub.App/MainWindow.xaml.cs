using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using SetupHub180Hz.Services;
using SetupHub180Hz.Views;

namespace SetupHub180Hz
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            Activated += async (_, _) =>
            {
                if (!UpdateMonitorService.Instance.ShouldSkipDueToRecency())
                {
                    await UpdateMonitorService.Instance.RefreshAsync();
                }
            };
            UpdateThemeButtonText();
            NavigateTo("Dashboard");
        }

        private void NavItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                NavigateTo(tag);
        }

        private readonly System.Collections.Generic.Dictionary<string, UserControl> _pageCache = new();

        private void NavigateTo(string pageKey)
        {
            if (!_pageCache.TryGetValue(pageKey, out var page))
            {
                page = pageKey switch
                {
                    "Dashboard" => new DashboardPage(this),
                    "SetupApps" => new SetupAppsPage(),
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

        public void SetStatus(string text) => StatusText.Text = text;

        /// <summary>Lets a child page (e.g. Dashboard's module tiles) trigger navigation
        /// through the same RadioButton-checked flow the sidebar uses.</summary>
        public void GoToPage(string pageKey)
        {
            RadioButton? target = pageKey switch
            {
                "Dashboard" => NavDashboard,
                "SetupApps" => NavSetupApps,
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

        // ===== Custom chrome window controls =====
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            var currentDark = SetupHub180Hz.Services.SettingsService.Instance.Current.DarkTheme;
            SetupHub180Hz.Services.ThemeService.ApplyTheme(!currentDark);
            UpdateThemeButtonText();
        }

        private void UpdateThemeButtonText()
        {
            var isDark = SetupHub180Hz.Services.SettingsService.Instance.Current.DarkTheme;
            ThemeToggleButton.Content = isDark ? "☀️ Light" : "🌙 Dark";
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
