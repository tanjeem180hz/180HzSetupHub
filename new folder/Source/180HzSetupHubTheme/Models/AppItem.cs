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
        private string? _installLocation;
        private bool _isSelected;

        public string? InstallLocation
        {
            get => _installLocation;
            set
            {
                if (_installLocation != value)
                {
                    _installLocation = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string? Publisher { get; set; }
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
                    OnPropertyChanged(nameof(FormattedVersion));
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
        public string FormattedVersion
        {
            get
            {
                if (HasUpdate && !string.IsNullOrWhiteSpace(AvailableVersion))
                {
                    string current = string.IsNullOrWhiteSpace(Version) ? "Installed" : Version;
                    return $"🏷️ {current} ➔ {AvailableVersion}";
                }
                return string.IsNullOrWhiteSpace(Version) ? "🏷️ Latest" : $"🏷️ {Version}";
            }
        }
        public string FormattedSize => string.IsNullOrWhiteSpace(Size) ? "-" : $"💾 {Size}";
        public string Initial => !string.IsNullOrWhiteSpace(Name) ? Name[0].ToString().ToUpperInvariant() : "•";

        private bool _isUpgrade;
        public bool IsUpgrade
        {
            get => _isUpgrade;
            set
            {
                if (_isUpgrade != value)
                {
                    _isUpgrade = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTime? _installDate;
        public DateTime? InstallDate
        {
            get => _installDate;
            set
            {
                if (_installDate != value)
                {
                    _installDate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedInstallDate));
                }
            }
        }

        public string FormattedInstallDate
        {
            get
            {
                if (!_installDate.HasValue) return "";
                var date = _installDate.Value;
                var diff = DateTime.Now.Date - date.Date;
                if (diff.TotalDays == 0) return "🕒 Installed: Today";
                if (diff.TotalDays == 1) return "🕒 Installed: Yesterday";
                if (diff.TotalDays < 7) return $"🕒 Installed: {(int)diff.TotalDays}d ago";
                if (diff.TotalDays < 30) return $"🕒 Installed: {(int)(diff.TotalDays / 7)}w ago";
                return $"🕒 {date:MMM dd, yyyy}";
            }
        }

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
                    else if (!_isInstalled && _status == "Installed")
                    {
                        Status = "Install";
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

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                if (Math.Abs(_downloadProgress - value) > 0.01)
                {
                    _downloadProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _downloadSpeed = "";
        public string DownloadSpeed
        {
            get => _downloadSpeed;
            set
            {
                if (_downloadSpeed != value)
                {
                    _downloadSpeed = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _downloadEta = "";
        public string DownloadEta
        {
            get => _downloadEta;
            set
            {
                if (_downloadEta != value)
                {
                    _downloadEta = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
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
