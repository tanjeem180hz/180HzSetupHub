using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WinSetupHub.App.Models;
using WinSetupHub.App.Services;

namespace WinSetupHub.App.ViewModels;

public sealed class PackageViewModel : INotifyPropertyChanged
{
    private readonly Brush _accentBrush;
    private readonly Brush _accentSoftBrush;
    private bool _isSelected;
    private PackageInstallState _state = PackageInstallState.Unknown;
    private string _statusText = "Not checked";
    private string _versionText = "Check for latest version";
    private bool _isBusy;
    private double _progressPercent;
    private bool _isProgressIndeterminate = true;
    private string _progressText = string.Empty;
    private string _transferText = string.Empty;

    public PackageViewModel(PackageDefinition package)
    {
        Package = package;
        IsSelected = package.Essential;
        _accentBrush = CreateBrush(package.AccentColor, Color.FromRgb(36, 107, 254));
        _accentSoftBrush = CreateBrush(package.AccentColor, Color.FromArgb(24, 36, 107, 254), alpha: 24);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PackageDefinition Package { get; }
    public string Id => Package.Id;
    public string Name => Package.Name;
    public string Category => Package.Category;
    public string Description => Package.Description;
    public bool Essential => Package.Essential;
    public string Initials => string.Join(string.Empty, Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
    public Uri? IconUri => Uri.TryCreate(Package.IconUrl, UriKind.Absolute, out var uri) ? uri : null;
    public Brush AccentBrush => _accentBrush;
    public Brush AccentSoftBrush => _accentSoftBrush;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionText));
            }
        }
    }

    public string SelectionText => IsSelected ? "Selected" : "Not selected";

    public PackageInstallState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(IsActionEnabled));
                OnPropertyChanged(nameof(CanRunPrimaryAction));
                OnPropertyChanged(nameof(CanCheck));
                OnPropertyChanged(nameof(StatusBackground));
                OnPropertyChanged(nameof(StatusForeground));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string VersionText
    {
        get => _versionText;
        private set => SetField(ref _versionText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsActionEnabled));
                OnPropertyChanged(nameof(CanRunPrimaryAction));
                OnPropertyChanged(nameof(CanCheck));
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetField(ref _isProgressIndeterminate, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    public string TransferText
    {
        get => _transferText;
        private set => SetField(ref _transferText, value);
    }

    public bool CanRunPrimaryAction => !IsBusy && State is PackageInstallState.Unknown or PackageInstallState.NotInstalled or PackageInstallState.UpdateAvailable or PackageInstallState.Failed;
    public bool IsActionEnabled => CanRunPrimaryAction;
    public bool CanCheck => !IsBusy;

    public string PrimaryActionText => State switch
    {
        PackageInstallState.Installed => "Installed",
        PackageInstallState.UpdateAvailable => "Update",
        PackageInstallState.Busy => "Working",
        PackageInstallState.Failed => "Retry",
        PackageInstallState.Unknown => "Install",
        _ => "Install"
    };

    public Brush StatusBackground => State switch
    {
        PackageInstallState.Installed => new SolidColorBrush(Color.FromRgb(232, 247, 239)),
        PackageInstallState.UpdateAvailable => new SolidColorBrush(Color.FromRgb(255, 243, 216)),
        PackageInstallState.NotInstalled => new SolidColorBrush(Color.FromRgb(238, 241, 245)),
        PackageInstallState.Busy => new SolidColorBrush(Color.FromRgb(234, 242, 255)),
        PackageInstallState.Failed => new SolidColorBrush(Color.FromRgb(253, 235, 236)),
        _ => new SolidColorBrush(Color.FromRgb(255, 243, 216))
    };

    public Brush StatusForeground => State switch
    {
        PackageInstallState.Installed => new SolidColorBrush(Color.FromRgb(11, 107, 67)),
        PackageInstallState.UpdateAvailable => new SolidColorBrush(Color.FromRgb(138, 87, 0)),
        PackageInstallState.NotInstalled => new SolidColorBrush(Color.FromRgb(77, 92, 112)),
        PackageInstallState.Busy => new SolidColorBrush(Color.FromRgb(23, 78, 196)),
        PackageInstallState.Failed => new SolidColorBrush(Color.FromRgb(163, 54, 60)),
        _ => new SolidColorBrush(Color.FromRgb(138, 87, 0))
    };

    public void MarkBusy(string status)
    {
        IsBusy = true;
        State = PackageInstallState.Busy;
        StatusText = status;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressText = "Starting...";
        TransferText = string.Empty;
    }

    public void UpdateProgress(double? percent, string text, string? transferText = null)
    {
        if (percent is not null)
        {
            ProgressPercent = Math.Clamp(percent.Value, 0, 100);
            IsProgressIndeterminate = false;
        }

        if (transferText is not null)
        {
            TransferText = transferText;
        }

        ProgressText = text;
    }

    public void SetStatus(PackageStatus status)
    {
        SetStatus(status.State, installedVersion: status.InstalledVersion, availableVersion: status.AvailableVersion);
    }

    public void SetStatus(
        PackageInstallState state,
        string? text = null,
        string? installedVersion = null,
        string? availableVersion = null)
    {
        IsBusy = false;
        State = state;
        StatusText = text ?? state switch
        {
            PackageInstallState.Installed => "Up to date",
            PackageInstallState.UpdateAvailable => "Update available",
            PackageInstallState.NotInstalled => "Not installed",
            PackageInstallState.Failed => "Retry ready",
            PackageInstallState.Busy => "Working",
            _ => "Not checked"
        };

        VersionText = BuildVersionText(state, installedVersion, availableVersion);
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressText = string.Empty;
        TransferText = string.Empty;
    }

    private static string BuildVersionText(PackageInstallState state, string? installedVersion, string? availableVersion)
    {
        if (state == PackageInstallState.UpdateAvailable)
        {
            return !string.IsNullOrWhiteSpace(installedVersion) && !string.IsNullOrWhiteSpace(availableVersion)
                ? $"Current: {installedVersion} - New: {availableVersion}"
                : !string.IsNullOrWhiteSpace(availableVersion)
                    ? $"New: {availableVersion}"
                    : "New version available";
        }

        if (state == PackageInstallState.Installed)
        {
            if (!string.IsNullOrWhiteSpace(installedVersion) && !string.IsNullOrWhiteSpace(availableVersion))
            {
                var comparison = CompareVersions(installedVersion, availableVersion);
                if (comparison > 0)
                {
                    return $"Current: {installedVersion} (newer than catalog)";
                }

                return $"Current: {installedVersion} - Latest: {availableVersion}";
            }

            return !string.IsNullOrWhiteSpace(installedVersion)
                ? $"Current: {installedVersion}"
                : string.Empty;
        }

        if (state == PackageInstallState.NotInstalled)
        {
            return !string.IsNullOrWhiteSpace(availableVersion)
                ? $"Latest: {availableVersion}"
                : string.Empty;
        }

        if (state == PackageInstallState.Failed)
        {
            return !string.IsNullOrWhiteSpace(installedVersion) && !string.IsNullOrWhiteSpace(availableVersion)
                ? $"Current: {installedVersion} - Latest: {availableVersion}"
                : string.Empty;
        }

        return string.Empty;
    }

    private static int? CompareVersions(string installedVersion, string availableVersion)
    {
        var installedParts = ReadNumericVersionParts(installedVersion);
        var availableParts = ReadNumericVersionParts(availableVersion);
        if (installedParts.Count == 0 || availableParts.Count == 0)
        {
            return null;
        }

        var length = Math.Max(installedParts.Count, availableParts.Count);
        for (var index = 0; index < length; index++)
        {
            var installed = index < installedParts.Count ? installedParts[index] : 0;
            var available = index < availableParts.Count ? availableParts[index] : 0;
            var comparison = installed.CompareTo(available);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static IReadOnlyList<long> ReadNumericVersionParts(string version)
    {
        var normalized = version
            .Split(['-', '+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var value) ? value : (long?)null)
            .TakeWhile(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    }

    private static Brush CreateBrush(string hex, Color fallback, byte? alpha = null)
    {
        try
        {
            var normalized = hex.Trim().TrimStart('#');
            if (normalized.Length != 6)
            {
                return new SolidColorBrush(fallback);
            }

            var color = Color.FromArgb(
                alpha ?? (byte)255,
                byte.Parse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

            return new SolidColorBrush(color);
        }
        catch
        {
            return new SolidColorBrush(fallback);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
