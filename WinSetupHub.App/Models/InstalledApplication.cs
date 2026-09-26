namespace WinSetupHub.App.Models;

public enum InstalledApplicationKind
{
    Win32,
    Store
}

public sealed record InstalledApplication
{
    public string Id { get; init; } = string.Empty;
    public InstalledApplicationKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string InstallLocation { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string UninstallString { get; init; } = string.Empty;
    public string QuietUninstallString { get; init; } = string.Empty;
    public string RegistryPath { get; init; } = string.Empty;
    public string PackageFullName { get; init; } = string.Empty;
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = string.Empty;
}
