using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinSetupHub.App.Models;
using WinSetupHub.App.Services;

namespace WinSetupHub.App.ViewModels;

public sealed class InstalledAppViewModel : INotifyPropertyChanged
{
    private readonly Brush _accentBrush;
    private readonly Brush _accentSoftBrush;
    private bool _isSelected;
    private bool _isBusy;
    private string _statusText;
    private double _progressPercent;
    private bool _isProgressIndeterminate = true;
    private string _progressText = string.Empty;
    private IReadOnlyList<LeftoverItem> _leftovers = [];

    public InstalledAppViewModel(InstalledApplication app)
    {
        App = app;
        _statusText = app.IsProtected ? app.ProtectionReason : "Ready";
        _accentBrush = CreateAccentBrush(app.Name, alpha: 255);
        _accentSoftBrush = CreateAccentBrush(app.Name, alpha: 36);
        IconSource = LoadIcon(app.IconPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public InstalledApplication App { get; }
    public string Name => App.Name;
    public string Version => string.IsNullOrWhiteSpace(App.Version) ? "Unknown" : App.Version;
    public string Publisher => string.IsNullOrWhiteSpace(App.Publisher) ? "Unknown publisher" : App.Publisher;
    public string KindText => App.Kind == InstalledApplicationKind.Store ? "Store app" : "Desktop app";
    public string InstallLocation => string.IsNullOrWhiteSpace(App.InstallLocation) ? "No install folder found" : App.InstallLocation;
    public string Initials => string.Join(string.Empty, Name.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
    public ImageSource? IconSource { get; }
    public Brush AccentBrush => _accentBrush;
    public Brush AccentSoftBrush => _accentSoftBrush;
    public bool IsProtected => App.IsProtected;
    public bool CanUninstall => !IsBusy && !App.IsProtected;
    public bool CanScanLeftovers => !IsBusy;
    public bool CanDeleteLeftovers => !IsBusy && Leftovers.Count > 0 && StatusText.Contains("uninstall", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<LeftoverItem> Leftovers => _leftovers;
    public string LeftoverText => Leftovers.Count == 0 ? "No leftover scan yet" : $"{Leftovers.Count} item(s), {SizeFormatter.Format(Leftovers.Sum(item => item.SizeBytes))}";
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

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(CanScanLeftovers));
                OnPropertyChanged(nameof(CanDeleteLeftovers));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(CanDeleteLeftovers));
            }
        }
    }

    public void MarkBusy(string status)
    {
        IsBusy = true;
        StatusText = status;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressText = "Starting...";
    }

    public void UpdateProgress(double? percent, string text)
    {
        if (percent is not null)
        {
            ProgressPercent = Math.Clamp(percent.Value, 0, 100);
            IsProgressIndeterminate = false;
        }

        ProgressText = text;
    }

    public void SetStatus(string status)
    {
        IsBusy = false;
        StatusText = status;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressText = string.Empty;
    }

    public void SetLeftovers(IReadOnlyList<LeftoverItem> leftovers)
    {
        _leftovers = leftovers;
        OnPropertyChanged(nameof(Leftovers));
        OnPropertyChanged(nameof(LeftoverText));
        OnPropertyChanged(nameof(CanDeleteLeftovers));
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

    private static ImageSource? LoadIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            var extension = Path.GetExtension(iconPath);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }

            var info = new ShFileInfo();
            var result = SHGetFileInfo(
                iconPath,
                0,
                ref info,
                (uint)Marshal.SizeOf<ShFileInfo>(),
                ShgfiIcon | ShgfiLargeIcon);

            if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(
                    info.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(36, 36));
                image.Freeze();
                return image;
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static Brush CreateAccentBrush(string seed, byte alpha)
    {
        var hash = Math.Abs(seed.GetHashCode(StringComparison.OrdinalIgnoreCase));
        var hue = hash % 360;
        var color = HslToRgb(hue / 360.0, 0.56, 0.52, alpha);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color HslToRgb(double hue, double saturation, double lightness, byte alpha)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue * 6 % 2 - 1));
        var m = lightness - chroma / 2;

        var (red, green, blue) = hue switch
        {
            < 1.0 / 6.0 => (chroma, x, 0d),
            < 2.0 / 6.0 => (x, chroma, 0d),
            < 3.0 / 6.0 => (0d, chroma, x),
            < 4.0 / 6.0 => (0d, x, chroma),
            < 5.0 / 6.0 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromArgb(
            alpha,
            (byte)Math.Round((red + m) * 255, MidpointRounding.AwayFromZero),
            (byte)Math.Round((green + m) * 255, MidpointRounding.AwayFromZero),
            (byte)Math.Round((blue + m) * 255, MidpointRounding.AwayFromZero));
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
