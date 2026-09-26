using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UpdateCenterPage : UserControl
    {
        private readonly WingetService _winget = new();

        public UpdateCenterPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            ResultsList.Items.Clear();
            SummaryText.Text = "Checking for updates…";
            UpgradeAllButton.IsEnabled = false;

            var upgradable = await _winget.GetUpgradableAppsAsync();

            if (upgradable.Count == 0)
            {
                SummaryText.Text = "Everything is up to date.";
                return;
            }

            SummaryText.Text = $"{upgradable.Count} app(s) can be updated.";
            UpgradeAllButton.IsEnabled = true;

            foreach (var app in upgradable)
                ResultsList.Items.Add(BuildRow(app));
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            UpgradeAllButton.IsEnabled = false;
            UpgradeAllButton.Content = "Upgrading…";

            var success = await _winget.UpgradeAllAsync();

            ActivityLogger.Instance.Log(
                success ? "Upgraded all apps." : "Upgrade-all finished with some errors.",
                success ? ActivityType.Success : ActivityType.Warning);

            UpgradeAllButton.Content = "Upgrade All";
            await LoadAsync();
        }

        private Border BuildRow(AppItem app)
        {
            var upgradeButton = new Button
            {
                Content = "Upgrade",
                Style = (Style)Application.Current.Resources["AccentButton"],
                Padding = new Thickness(14, 6, 14, 6),
            };

            var nameText = new TextBlock
            {
                Text = app.Name,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["BrushTextPrimary"],
                FontWeight = FontWeights.SemiBold,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFont"],
            };
            var versionText = new TextBlock
            {
                Text = $"{app.Version}  →  {app.AvailableVersion}",
                Style = (Style)Application.Current.Resources["TextBody"],
            };

            var textPanel = new StackPanel();
            textPanel.Children.Add(nameText);
            textPanel.Children.Add(versionText);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textPanel, 0);
            Grid.SetColumn(upgradeButton, 1);
            grid.Children.Add(textPanel);
            grid.Children.Add(upgradeButton);

            var row = new Border { Style = (Style)Application.Current.Resources["ListRow"], Child = grid };

            upgradeButton.Click += async (_, _) =>
            {
                upgradeButton.IsEnabled = false;
                upgradeButton.Content = "Upgrading…";

                var success = await _winget.UpgradeAsync(app.Id);

                upgradeButton.Content = success ? "Done" : "Failed";
                ActivityLogger.Instance.Log(
                    success ? $"Upgraded {app.Name}." : $"Failed to upgrade {app.Name}.",
                    success ? ActivityType.Success : ActivityType.Error);

                if (!success) upgradeButton.IsEnabled = true;
            };

            return row;
        }
    }
}
