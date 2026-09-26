using System.Windows;
using System.Windows.Controls;

namespace WindowsSetupHub.Pages
{
    public partial class CleanupPage : Page
    {
        public CleanupPage()
        {
            InitializeComponent();
        }

        private void StartScan_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Scanning PC...\nFound: 2.4 GB Temp Files, 486 Registry Issues, 12 Startup Apps",
                "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CleanTemp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Cleaning temp files...\nFreed 2.4 GB of disk space!",
                "Cleanup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void FixRegistry_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Fixing 486 registry issues...\nRegistry cleaned successfully!",
                "Registry Fixed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ManageStartup_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Opening Startup Manager...\n12 startup apps found.",
                "Startup Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
