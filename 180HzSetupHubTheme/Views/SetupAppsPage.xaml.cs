using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class SetupAppsPage : UserControl
    {
        private readonly WingetService _winget = new();

        public SetupAppsPage()
        {
            InitializeComponent();
        }

        private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await RunSearchAsync();
        }

        private async Task RunSearchAsync()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

            ResultsList.Items.Clear();
            StatusText.Text = $"Searching for \"{query}\"…";
            StatusText.Visibility = Visibility.Visible;

            var results = await _winget.SearchAsync(query);

            if (results.Count == 0)
            {
                StatusText.Text = "No matching packages found.";
                return;
            }

            StatusText.Visibility = Visibility.Collapsed;
            foreach (var app in results)
                ResultsList.Items.Add(BuildRow(app));
        }

        private Border BuildRow(AppItem app)
        {
            var installButton = new Button
            {
                Content = "Install",
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
            var idText = new TextBlock
            {
                Text = $"{app.Id}  ·  {app.Version}",
                Style = (Style)Application.Current.Resources["TextBody"],
            };

            var textPanel = new StackPanel();
            textPanel.Children.Add(nameText);
            textPanel.Children.Add(idText);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textPanel, 0);
            Grid.SetColumn(installButton, 1);
            grid.Children.Add(textPanel);
            grid.Children.Add(installButton);

            var row = new Border { Style = (Style)Application.Current.Resources["ListRow"], Child = grid };

            installButton.Click += async (_, _) =>
            {
                installButton.IsEnabled = false;
                installButton.Content = "Installing…";

                var success = await _winget.InstallAsync(app.Id);

                installButton.Content = success ? "Installed" : "Failed";
                ActivityLogger.Instance.Log(
                    success ? $"Installed {app.Name}." : $"Failed to install {app.Name}.",
                    success ? ActivityType.Success : ActivityType.Error);

                if (!success) installButton.IsEnabled = true;
            };

            return row;
        }
    }
}
