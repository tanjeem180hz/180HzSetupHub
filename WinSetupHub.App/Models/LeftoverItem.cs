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

    public record LeftoverItem(
        LeftoverType Type,
        string Path,
        long? SizeBytes,
        string MatchedOn,
        bool IsHighConfidence = true);

    public class LeftoverViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public LeftoverItem Item { get; }
        public LeftoverType Type => Item.Type;
        public string Path => Item.Path;
        public string MatchedOn => Item.MatchedOn;
        public long? SizeBytes => Item.SizeBytes;
        public bool IsHighConfidence => Item.IsHighConfidence;
        public string ConfidenceBadge => IsHighConfidence ? "Verified Safe" : "Review Recommended";

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

        private bool _isProcessing;
        private bool _isDeleted;
        private bool _isFailed;

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (_isProcessing != value)
                {
                    _isProcessing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadgeVisibility));
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(BadgeBackground));
                    OnPropertyChanged(nameof(BadgeForeground));
                }
            }
        }

        public bool IsDeleted
        {
            get => _isDeleted;
            set
            {
                if (_isDeleted != value)
                {
                    _isDeleted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadgeVisibility));
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(BadgeBackground));
                    OnPropertyChanged(nameof(BadgeForeground));
                    OnPropertyChanged(nameof(ItemOpacity));
                }
            }
        }

        public bool IsFailed
        {
            get => _isFailed;
            set
            {
                if (_isFailed != value)
                {
                    _isFailed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadgeVisibility));
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(BadgeBackground));
                    OnPropertyChanged(nameof(BadgeForeground));
                }
            }
        }

        public System.Windows.Visibility StatusBadgeVisibility =>
            (IsProcessing || IsDeleted || IsFailed) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility CheckBoxVisibility =>
            (IsProcessing || IsDeleted || IsFailed) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public string StatusBadgeText =>
            IsProcessing ? "⚡ PURGING" :
            IsDeleted ? "✓ REMOVED" :
            IsFailed ? "⚠️ SKIPPED" : string.Empty;

        public string BadgeBackground =>
            IsProcessing ? "#262FB6FF" :
            IsDeleted ? "#263FCB7E" :
            IsFailed ? "#26F5B84C" : "Transparent";

        public string BadgeForeground =>
            IsProcessing ? "#2FB6FF" :
            IsDeleted ? "#3FCB7E" :
            IsFailed ? "#F5B84C" : "#A0AEC0";

        public double ItemOpacity => IsDeleted ? 0.45 : 1.0;

        public LeftoverViewModel(LeftoverItem item, string fullAppName)
        {
            Item = item;
            // Pre-select verified safe leftover items by default
            _isSelected = item.IsHighConfidence;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
