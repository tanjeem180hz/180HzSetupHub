using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SetupHub180Hz.Models
{
    public enum LeftoverType
    {
        RegistryKey,
        Folder,
        File
    }

    public record LeftoverItem(LeftoverType Type, string Path, long? SizeBytes, string MatchedOn);

    public class LeftoverViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public LeftoverItem Item { get; }
        public LeftoverType Type => Item.Type;
        public string Path => Item.Path;
        public string MatchedOn => Item.MatchedOn;
        public long? SizeBytes => Item.SizeBytes;

        public string TypeIcon => Type switch
        {
            LeftoverType.RegistryKey => "🔑",
            LeftoverType.Folder => "📁",
            _ => "📄"
        };

        public string TypeName => Type switch
        {
            LeftoverType.RegistryKey => "Registry Key",
            LeftoverType.Folder => "Folder",
            _ => "File"
        };

        public string SizeDisplay
        {
            get
            {
                if (!SizeBytes.HasValue || SizeBytes.Value <= 0) return string.Empty;
                var bytes = SizeBytes.Value;
                if (bytes >= 1024 * 1024 * 1024)
                    return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
                if (bytes >= 1024 * 1024)
                    return $"{bytes / (1024.0 * 1024):0.0} MB";
                if (bytes >= 1024)
                    return $"{bytes / 1024.0:0.0} KB";
                return $"{bytes} B";
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

        public LeftoverViewModel(LeftoverItem item, string fullAppName)
        {
            Item = item;
            // High-confidence matches (full app name) start checked; partial/shorter token matches start unchecked
            _isSelected = string.Equals(item.MatchedOn, fullAppName, StringComparison.OrdinalIgnoreCase);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
