using WinSetupHub.App.Models;

namespace WinSetupHub.App.Services;

public sealed record PackageStatus(
    PackageInstallState State,
    string? InstalledVersion = null,
    string? AvailableVersion = null);
