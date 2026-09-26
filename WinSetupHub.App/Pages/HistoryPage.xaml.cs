using System.Windows;
using System.Windows.Controls;

namespace WindowsSetupHub.Pages
{
    public partial class HistoryPage : Page
    {
        public HistoryPage()
        {
            InitializeComponent();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all history?",
                "Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                MessageBox.Show("History cleared successfully!",
                    "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
