using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SetupHub180Hz.Models
{
    public class TweakItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public int CategoryOrder { get; set; }
        public bool IsAdvanced { get; set; }
        public bool IsRecommended { get; set; }
        public bool RequiresReboot { get; set; }
        public string Link { get; set; } = "";
        public string Section { get; set; } = "System"; // "System" or "Registry"

        public string FormattedRegistryTarget
        {
            get
            {
                if (Registry == null || Registry.Count == 0) return string.Empty;
                var r = Registry[0];
                string shortPath = r.Path.Replace(":\\", @"\").Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
                string extra = Registry.Count > 1 ? $" (+{Registry.Count - 1} more)" : "";
                return $"{shortPath} ➔ {r.Name} = {r.Value}{extra}";
            }
        }

        public bool HasRegistryTarget => Registry != null && Registry.Count > 0;

        public List<TweakRegistryEntry> Registry { get; set; } = new();
        public List<TweakServiceEntry> Service { get; set; } = new();

        [JsonPropertyName("invokeScript")]
        public List<string> InvokeScript { get; set; } = new();

        [JsonPropertyName("undoScript")]
        public List<string> UndoScript { get; set; } = new();

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
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TweakRegistryEntry
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Type { get; set; } = "DWord";
        public string OriginalValue { get; set; } = "";
    }

    public class TweakServiceEntry
    {
        public string Name { get; set; } = "";
        public string StartupType { get; set; } = "";
        public string OriginalType { get; set; } = "";
    }
}
