namespace WinSetupHub.App.Models;

public enum PackageInstallState
{
    Unknown,
    Installed,
    UpdateAvailable,
    NotInstalled,
    Busy,
    Failed
}
