using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinSetupHub.App.Models;
using WinSetupHub.App.Services;

namespace WinSetupHub.App.ViewModels;

public sealed class CleanupTargetViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isBusy;
    private long _sizeBytes;
    private int _fileCount;
    private int _skippedCount;
    private string _statusText = "Not scanned";

    public CleanupTargetViewModel(CleanupTarget target)
    {
        Target = target;
        _isSelected = target.IsSelectedByDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CleanupTarget Target { get; }
    public string Name => Target.Name;
    public string Description => Target.Description;
    public string RootPath => Target.RootPath;
    public long SizeBytes => _sizeBytes;
    public string SizeText => SizeFormatter.Format(_sizeBytes);
    public string FileText => $"{_fileCount} file(s), {_skippedCount} skipped";
    public bool CanRun => !IsBusy;

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
                OnPropertyChanged(nameof(CanRun));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public void MarkBusy(string status)
    {
        IsBusy = true;
        StatusText = status;
    }

    public void SetScanResult(CleanupScanResult result)
    {
        IsBusy = false;
        _sizeBytes = result.SizeBytes;
        _fileCount = result.FileCount;
        _skippedCount = result.SkippedCount;
        StatusText = _fileCount == 0 ? "Clean" : "Ready to clean";
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(FileText));
    }

    public void SetRunResult(CleanupRunResult result)
    {
        IsBusy = false;
        _sizeBytes = Math.Max(0, _sizeBytes - result.FreedBytes);
        _fileCount = Math.Max(0, _fileCount - result.DeletedCount);
        _skippedCount = result.SkippedCount;
        StatusText = $"Freed {SizeFormatter.Format(result.FreedBytes)}";
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(FileText));
    }

    public void SetStatus(string status)
    {
        IsBusy = false;
        StatusText = status;
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
