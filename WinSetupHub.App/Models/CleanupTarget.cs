namespace WinSetupHub.App.Models;

public sealed record CleanupTarget
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public IReadOnlyList<string> FilePatterns { get; init; } = ["*"];
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromMinutes(30);
    public bool IsSelectedByDefault { get; init; } = true;
}

public sealed record CleanupScanResult(
    string TargetId,
    long SizeBytes,
    int FileCount,
    int SkippedCount);

public sealed record CleanupRunResult(
    string TargetId,
    long FreedBytes,
    int DeletedCount,
    int SkippedCount);
