using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsSetupHub.Pages
{
    public partial class UninstallPage : Page
    {
        public UninstallPage()
        {
            InitializeComponent();
        }

        private void AppCheck_Changed(object sender, RoutedEventArgs e)
        {
            int count = 0;
            foreach (var item in UninstallList.Children)
            {
                if (item is Border border)
                {
                    var grid = border.Child as Grid;
                    if (grid?.Children[0] is CheckBox cb && cb.IsChecked == true)
                        count++;
                }
            }
            SelectCountText.Text = count > 0
                ? $"🗑️  {count} app(s) selected for removal"
                : "🗑️  Select apps to uninstall";
        }

        private void UninstallOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to uninstall {btn.Tag}?",
                    "Confirm Uninstall",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var parent = (btn.Parent as Grid)?.Parent as Border;
                    if (parent != null)
                        UninstallList.Children.Remove(parent);
                }
            }
        }

        private void UninstallSelected_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Uninstalling selected apps...", "Uninstall",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UninstallItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x38));
        }

        private void UninstallItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
                b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
        }
    }
}
