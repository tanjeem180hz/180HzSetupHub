using System.Windows;
using System.Windows.Controls;

namespace WindowsSetupHub.Pages
{
    public partial class UpdatePage : Page
    {
        public UpdatePage()
        {
            InitializeComponent();
        }

        private void UpdateOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                MessageBox.Show($"Updating {btn.Tag}...\nThis would run winget update.",
                    "Updating", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateAll_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Updating all apps...\nThis would run: winget upgrade --all",
                "Update All", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
