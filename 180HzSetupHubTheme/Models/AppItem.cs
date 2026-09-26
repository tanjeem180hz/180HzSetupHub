using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SetupHub180Hz.Models
{
    public class AppItem : INotifyPropertyChanged
    {
        private string _status = "Install";
        private bool _isBusy;
        private bool _isInstalled;

        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Category { get; set; } = "General";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "Latest";
        public string AvailableVersion { get; set; } = "";
        public string Size { get; set; } = "~50 MB";
        public string IconUrl { get; set; } = "";
        public string WebUrl { get; set; } = "";
        public string AccentColor { get; set; } = "#2FB6FF";
        public string Source { get; set; } = "winget";
        public bool Essential { get; set; }

        public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion);

        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled != value)
                {
                    _isInstalled = value;
                    OnPropertyChanged();
                    if (_isInstalled && _status != "Installing…")
                    {
                        Status = "Installed";
                    }
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
