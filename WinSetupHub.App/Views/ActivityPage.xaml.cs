using System.Windows.Controls;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class ActivityPage : UserControl
    {
        public ActivityPage()
        {
            InitializeComponent();
            LogList.ItemsSource = ActivityLogger.Instance.Entries;
        }
    }
}
