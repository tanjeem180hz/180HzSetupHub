using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SetupHub180Hz.Models
{
    public enum StartupItemType
    {
        RegistryCurrentUser,
        RegistryLocalMachine,
        RegistryLocalMachineWow64,
        StartupFolderUser,
        StartupFolderCommon
    }

    public class StartupItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _name = "";
        private string _command = "";
        private string _source = "Registry (Current User)";
        private StartupItemType _startupType = StartupItemType.RegistryCurrentUser;
        private string? _targetFilePath;
        private string? _lastActionDescription;
        private DateTime? _lastActionTime;
        private ImageSource? _iconImageSource;
        private string? _localIconPath;
        private string? _iconUrl;
        private string _webUrl = "";

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Initial));
                }
            }
        }

        public string Command
        {
            get => _command;
            set
            {
                if (_command != value)
                {
                    _command = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Source
        {
            get => _source;
            set
            {
                if (_source != value)
                {
                    _source = value;
                    OnPropertyChanged();
                }
            }
        }

        public StartupItemType StartupType
        {
            get => _startupType;
            set
            {
                if (_startupType != value)
                {
                    _startupType = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? TargetFilePath
        {
            get => _targetFilePath;
            set
            {
                if (_targetFilePath != value)
                {
                    _targetFilePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? LastActionDescription
        {
            get => _lastActionDescription;
            set
            {
                if (_lastActionDescription != value)
                {
                    _lastActionDescription = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLastAction));
                }
            }
        }

        public DateTime? LastActionTime
        {
            get => _lastActionTime;
            set
            {
                if (_lastActionTime != value)
                {
                    _lastActionTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasLastAction => !string.IsNullOrWhiteSpace(LastActionDescription);

        public string Initial => !string.IsNullOrWhiteSpace(Name)
            ? Name.Trim().Substring(0, 1).ToUpperInvariant()
            : "⚡";

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

        public bool HasIconImage => _iconImageSource != null;

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

        public string? IconUrl
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

        public bool HasWebUrl => !string.IsNullOrWhiteSpace(WebUrl);

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusShortText));
                    OnPropertyChanged(nameof(StatusBadgeBackground));
                    OnPropertyChanged(nameof(StatusBadgeForeground));
                    OnPropertyChanged(nameof(StatusBadgeBorder));
                }
            }
        }

        public string StatusText => IsEnabled ? "Enabled (ON)" : "Disabled (OFF)";
        public string StatusShortText => IsEnabled ? "ON" : "OFF";
        public string StatusBadgeBackground => IsEnabled ? "#152E20" : "#20242C";
        public string StatusBadgeForeground => IsEnabled ? "#10B981" : "#94A3B8";
        public string StatusBadgeBorder => IsEnabled ? "#059669" : "#334155";

        private bool _isSelected;
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
