using System.Text.Json;

namespace WinSetupHub.App.Services;

public sealed record ApplicationSettings
{
    public UpdateSettings Update { get; init; } = new();
    public UiSettings Ui { get; init; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static ApplicationSettings Load(StoragePaths paths)
    {
        paths.EnsureSeedFile("appsettings.default.json", paths.AppSettingsPath);

        if (!File.Exists(paths.AppSettingsPath))
        {
            return new ApplicationSettings();
        }

        using var stream = File.OpenRead(paths.AppSettingsPath);
        var settings = JsonSerializer.Deserialize<ApplicationSettings>(stream, JsonOptions);

        return settings ?? new ApplicationSettings();
    }

    public static async Task<ApplicationSettings> LoadAsync(StoragePaths paths, CancellationToken cancellationToken = default)
    {
        paths.EnsureSeedFile("appsettings.default.json", paths.AppSettingsPath);

        if (!File.Exists(paths.AppSettingsPath))
        {
            return new ApplicationSettings();
        }

        await using var stream = File.OpenRead(paths.AppSettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
            stream,
            JsonOptions,
            cancellationToken);

        return settings ?? new ApplicationSettings();
    }

    public async Task SaveAsync(StoragePaths paths, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AppSettingsPath)!);

        await using var stream = File.Create(paths.AppSettingsPath);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
    }
}

public sealed record UpdateSettings
{
    public string CurrentVersion { get; init; } = "0.1.0";
    public string ReleasesApiUrl { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
}

public sealed record UiSettings
{
    public string Theme { get; init; } = "Dark";
}
