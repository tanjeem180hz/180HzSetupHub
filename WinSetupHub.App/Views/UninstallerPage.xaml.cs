using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Models;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class UninstallerPage : UserControl
    {
        private readonly WingetService _winget = new();
        private List<AppItem> _allApps = new();

        public UninstallerPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            ResultsList.Items.Clear();
            StatusText.Text = "Loading installed apps…";
            StatusText.Visibility = Visibility.Visible;

            _allApps = await _winget.GetInstalledAppsAsync();
            Render(_allApps);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = FilterBox.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allApps
                : _allApps.Where(a => a.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToList();
            Render(filtered);
        }

        private void Render(List<AppItem> apps)
        {
            ResultsList.Items.Clear();

            if (apps.Count == 0)
            {
                StatusText.Text = "No apps found.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            StatusText.Visibility = Visibility.Collapsed;
            foreach (var app in apps)
                ResultsList.Items.Add(BuildRow(app));
        }

        private Border BuildRow(AppItem app)
        {
            var uninstallButton = new Button
            {
                Content = "Uninstall",
                Style = (Style)Application.Current.Resources["OutlineButton"],
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
            Grid.SetColumn(uninstallButton, 1);
            grid.Children.Add(textPanel);
            grid.Children.Add(uninstallButton);

            var row = new Border { Style = (Style)Application.Current.Resources["ListRow"], Child = grid };

            uninstallButton.Click += async (_, _) =>
            {
                var confirm = MessageBox.Show($"Uninstall \"{app.Name}\"?", "Confirm Uninstall",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                uninstallButton.IsEnabled = false;
                uninstallButton.Content = "Removing…";

                var success = await _winget.UninstallAsync(app.Id);

                ActivityLogger.Instance.Log(
                    success ? $"Uninstalled {app.Name}." : $"Failed to uninstall {app.Name}.",
                    success ? ActivityType.Success : ActivityType.Error);

                if (success)
                {
                    _allApps.Remove(app);
                    ResultsList.Items.Remove(row);
                }
                else
                {
                    uninstallButton.Content = "Failed";
                    uninstallButton.IsEnabled = true;
                }
            };

            return row;
        }
    }
}
