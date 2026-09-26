namespace SetupHub180Hz.Models
{
    public class AppSettings
    {
        public bool AutoStartWithWindows { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
        public int UpdateCheckFrequencyHours { get; set; } = 6;
        public bool RequireAdminForActions { get; set; } = true;
        public bool DarkTheme { get; set; } = true;
    }
}
