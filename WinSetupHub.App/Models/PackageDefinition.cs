namespace WinSetupHub.App.Models;

public sealed record PackageDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Source { get; init; } = "winget";
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconUrl { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "#246BFE";
    public IReadOnlyList<string> DetectionNames { get; init; } = [];
    public bool Essential { get; init; }
}
