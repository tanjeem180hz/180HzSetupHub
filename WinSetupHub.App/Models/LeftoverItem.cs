namespace WinSetupHub.App.Models;

public sealed record LeftoverItem
{
    public string Path { get; init; } = string.Empty;
    public string Kind { get; init; } = "Folder";
    public long SizeBytes { get; init; }
    public string Reason { get; init; } = string.Empty;
}
