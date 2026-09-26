using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WindowsSetupHub.Pages;

namespace WindowsSetupHub
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer? _sysTimer;

        public MainWindow()
        {
            InitializeComponent();
            if (ContentFrame != null && ContentFrame.Content == null)
            {
                ContentFrame.Navigate(new InstallPage());
            }
            StartSystemMonitor();
        }

        // Window drag
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // Window buttons
        private void Btn_Minimize(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Btn_Maximize(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void Btn_Close(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();

        private void Btn_Notify(object sender, RoutedEventArgs e)
            => MessageBox.Show("No new notifications.", "Notifications",
               MessageBoxButton.OK, MessageBoxImage.Information);

        private void Btn_Settings(object sender, RoutedEventArgs e)
            => MessageBox.Show("Settings coming soon!", "Settings",
               MessageBoxButton.OK, MessageBoxImage.Information);

        // Navigation
        private void NavInstall_Checked(object sender, RoutedEventArgs e)
        {
            if (PageTitle != null) PageTitle.Text = "Install Applications";
            if (PageSubTitle != null) PageSubTitle.Text = "Browse and install apps with one click";
            if (ContentFrame != null) ContentFrame.Navigate(new InstallPage());
        }

        private void NavUninstall_Checked(object sender, RoutedEventArgs e)
        {
            if (PageTitle != null) PageTitle.Text = "Uninstall Applications";
            if (PageSubTitle != null) PageSubTitle.Text = "Remove unwanted applications from your PC";
            if (ContentFrame != null) ContentFrame.Navigate(new UninstallPage());
        }

        private void NavUpdate_Checked(object sender, RoutedEventArgs e)
        {
            if (PageTitle != null) PageTitle.Text = "App Updates";
            if (PageSubTitle != null) PageSubTitle.Text = "Keep your applications secure and up to date";
            if (ContentFrame != null) ContentFrame.Navigate(new UpdatePage());
        }

        private void NavCleanup_Checked(object sender, RoutedEventArgs e)
        {
            if (PageTitle != null) PageTitle.Text = "PC Cleanup";
            if (PageSubTitle != null) PageSubTitle.Text = "Free up space and boost performance";
            if (ContentFrame != null) ContentFrame.Navigate(new CleanupPage());
        }

        private void NavHistory_Checked(object sender, RoutedEventArgs e)
        {
            if (PageTitle != null) PageTitle.Text = "Activity History";
            if (PageSubTitle != null) PageSubTitle.Text = "View all past operations and activity logs";
            if (ContentFrame != null) ContentFrame.Navigate(new HistoryPage());
        }

        // CPU / RAM monitor
        private void StartSystemMonitor()
        {
            UpdateSystemInfo(this, EventArgs.Empty);
            _sysTimer = new DispatcherTimer();
            _sysTimer.Interval = TimeSpan.FromSeconds(3);
            _sysTimer.Tick += UpdateSystemInfo;
            _sysTimer.Start();
        }

        private void UpdateSystemInfo(object? sender, EventArgs e)
        {
            var rnd = new Random();
            int cpu = rnd.Next(10, 55);
            int ram = rnd.Next(40, 70);
            if (CpuBar != null) CpuBar.Value = cpu;
            if (CpuText != null) CpuText.Text = cpu + "%";
            if (RamBar != null) RamBar.Value = ram;
            if (RamText != null) RamText.Text = (ram * 0.08).ToString("F1") + " GB";
        }
    }
}
