using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SetupHub180Hz.Models
{
    public class StartupItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Source { get; set; } = "Registry (Current User)";

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
