using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class ActivityPage : UserControl, IRealtimeRefreshable
    {
        public void RefreshRealtime() => UpdateCount();

        public ActivityPage()
        {
            InitializeComponent();
            LogList.ItemsSource = ActivityLogger.Instance.Entries;
            ActivityLogger.Instance.Entries.CollectionChanged += (_, _) => UpdateCount();
            Loaded += (_, _) => UpdateCount();
        }

        private void UpdateCount()
        {
            int count = ActivityLogger.Instance.Entries.Count;
            LogCountText.Text = count == 1 ? "1 logged event" : $"{count} logged events";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            UpdateCount();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ActivityLogger.Instance.Clear();
            UpdateCount();
        }
    }
}
