using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SetupHub180Hz.Models
{
    public class StartupItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _name = "";
        private string _command = "";
        private string _source = "Registry (Current User)";
        private ImageSource? _iconImageSource;
        private string? _localIconPath;
        private string? _iconUrl;

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

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
