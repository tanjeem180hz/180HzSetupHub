using System.Text.Json;
using WinSetupHub.App.Models;
using WinSetupHub.App.Security;

namespace WinSetupHub.App.Services;

public sealed class PackageCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly StoragePaths _paths;

    public PackageCatalog(StoragePaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<PackageDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureSeedFile("packages.default.json", _paths.PackagesConfigPath);
        var defaultPackages = await LoadFromFileAsync(
            Path.Combine(AppContext.BaseDirectory, "Configuration", "packages.default.json"),
            cancellationToken);

        if (!File.Exists(_paths.PackagesConfigPath))
        {
            return defaultPackages;
        }

        var packages = await LoadFromFileAsync(_paths.PackagesConfigPath, cancellationToken);
        var defaultsById = defaultPackages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
        var mergedPackages = defaultPackages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            mergedPackages[package.Id] = EnrichPackage(package, defaultsById);
        }

        return CleanPackages(mergedPackages.Values.ToList());
    }

    private static async Task<List<PackageDefinition>> LoadFromFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new List<PackageDefinition>();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<PackageDefinition>>(stream, Options, cancellationToken)
            ?? new List<PackageDefinition>();
    }

    private static IReadOnlyList<PackageDefinition> CleanPackages(IReadOnlyList<PackageDefinition> packages)
    {
        var cleanedPackages = packages?
            .Where(package =>
                !string.IsNullOrWhiteSpace(package.Id)
                && !string.IsNullOrWhiteSpace(package.Name)
                && PackageIdValidator.IsSafe(package.Id)
                && IsKnownSource(package.Source))
            .OrderByDescending(package => package.Essential)
            .ThenBy(package => package.Category)
            .ThenBy(package => package.Name)
            .ToList();

        if (cleanedPackages is not null)
        {
            return cleanedPackages;
        }

        return Array.Empty<PackageDefinition>();
    }

    private static PackageDefinition EnrichPackage(
        PackageDefinition package,
        IReadOnlyDictionary<string, PackageDefinition> defaultsById)
    {
        if (!defaultsById.TryGetValue(package.Id, out var defaults))
        {
            return package;
        }

        return package with
        {
            IconUrl = string.IsNullOrWhiteSpace(package.IconUrl) ? defaults.IconUrl : package.IconUrl,
            AccentColor = string.IsNullOrWhiteSpace(package.AccentColor) ? defaults.AccentColor : package.AccentColor,
            Source = string.IsNullOrWhiteSpace(package.Source) ? defaults.Source : package.Source,
            DetectionNames = package.DetectionNames.Count == 0 ? defaults.DetectionNames : package.DetectionNames
        };
    }

    private static bool IsKnownSource(string? source)
    {
        return string.IsNullOrWhiteSpace(source)
            || source.Equals("winget", StringComparison.OrdinalIgnoreCase)
            || source.Equals("msstore", StringComparison.OrdinalIgnoreCase);
    }
}
