using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SetupHub180Hz.Models
{
    public class AppItem : INotifyPropertyChanged
    {
        private string _status = "Install";
        private bool _isBusy;
        private bool _isInstalled;
        private string _version = "Latest";
        private string _availableVersion = "";
        private string _size = "";
        private string _iconUrl = "";
        private ImageSource? _iconImageSource;
        private string? _localIconPath;
        private string? _uninstallString;

        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Category { get; set; } = "General";
        public string Description { get; set; } = "";
        public List<string>? DetectionNames { get; set; }

        public string Version
        {
            get => _version;
            set
            {
                if (_version != value)
                {
                    _version = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedVersion));
                }
            }
        }

        public string AvailableVersion
        {
            get => _availableVersion;
            set
            {
                if (_availableVersion != value)
                {
                    _availableVersion = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasUpdate));
                }
            }
        }

        public string Size
        {
            get => _size;
            set
            {
                if (_size != value)
                {
                    _size = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedSize));
                }
            }
        }

        public string IconUrl
        {
            get => _iconUrl;
            set
            {
                if (_iconUrl != value)
                {
                    _iconUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public ImageSource? IconImageSource
        {
            get => _iconImageSource;
            set
            {
                if (_iconImageSource != value)
                {
                    _iconImageSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasIconImage));
                }
            }
        }

        public string? LocalIconPath
        {
            get => _localIconPath;
            set
            {
                if (_localIconPath != value)
                {
                    _localIconPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? UninstallString
        {
            get => _uninstallString;
            set
            {
                if (_uninstallString != value)
                {
                    _uninstallString = value;
                    OnPropertyChanged();
                }
            }
        }
        private string _webUrl = "";
        public string WebUrl
        {
            get => _webUrl;
            set
            {
                if (_webUrl != value)
                {
                    _webUrl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasWebUrl));
                }
            }
        }
        public string AccentColor { get; set; } = "#2FB6FF";
        public string Source { get; set; } = "winget";
        public bool Essential { get; set; }

        public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion);
        public bool HasIconImage => IconImageSource != null;
        public bool HasWebUrl => !string.IsNullOrWhiteSpace(WebUrl);
        public bool HasSource => !string.IsNullOrWhiteSpace(Source);
        public string SourceTag => Source?.ToUpperInvariant() ?? "WINGET";
        public string FormattedVersion => string.IsNullOrWhiteSpace(Version) ? "-" : $"🏷️ {Version}";
        public string FormattedSize => string.IsNullOrWhiteSpace(Size) ? "-" : $"💾 {Size}";
        public string Initial => !string.IsNullOrWhiteSpace(Name) ? Name[0].ToString().ToUpperInvariant() : "•";

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
