using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SetupHub180Hz.Models
{
    public class AppBundle : INotifyPropertyChanged
    {
        private string _progressText = "";
        private bool _isInstalling;
        private string _buttonContent = "⚡ Install All";

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconEmoji { get; set; } = "📦";
        public List<string> WingetIds { get; set; } = new();

        public string ProgressText
        {
            get => _progressText;
            set
            {
                if (_progressText != value)
                {
                    _progressText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasProgress));
                }
            }
        }

        public bool HasProgress => !string.IsNullOrWhiteSpace(ProgressText);

        public bool IsInstalling
        {
            get => _isInstalling;
            set
            {
                if (_isInstalling != value)
                {
                    _isInstalling = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotInstalling));
                }
            }
        }

        public bool IsNotInstalling => !_isInstalling;

        public string ButtonContent
        {
            get => _buttonContent;
            set
            {
                if (_buttonContent != value)
                {
                    _buttonContent = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
