namespace WinSetupHub.App.Services;

public sealed record AvailablePackageUpdate(
    string Name,
    string Id,
    string InstalledVersion,
    string AvailableVersion,
    string Source);
