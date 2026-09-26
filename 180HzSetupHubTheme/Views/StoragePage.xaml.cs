using System.Windows;
using System.Windows.Controls;
using SetupHub180Hz.Services;

namespace SetupHub180Hz.Views
{
    public partial class StoragePage : UserControl
    {
        private readonly StorageService _storage = new();

        public StoragePage()
        {
            InitializeComponent();
            PathText.Text = StorageService.AppDataFolder;
            Loaded += async (_, _) => await RefreshSizeAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSizeAsync();

        private void OpenStorage_Click(object sender, RoutedEventArgs e) => _storage.OpenInExplorer();

        private async System.Threading.Tasks.Task RefreshSizeAsync()
        {
            SizeText.Text = "Calculating…";
            long bytes = await System.Threading.Tasks.Task.Run(() => _storage.GetFolderSizeBytes());
            double mb = bytes / 1024.0 / 1024.0;
            SizeText.Text = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
        }
    }
}
