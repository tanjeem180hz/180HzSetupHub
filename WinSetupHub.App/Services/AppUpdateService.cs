using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace WinSetupHub.App.Services;

public sealed class AppUpdateService
{
    private readonly StoragePaths _paths;

    public AppUpdateService(StoragePaths paths)
    {
        _paths = paths;
    }

    public async Task<string> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ApplicationSettings.LoadAsync(_paths, cancellationToken);
        var apiUrl = settings.Update.ReleasesApiUrl;

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            return $"Updater is ready. Add a GitHub Releases API URL in {_paths.AppSettingsPath}.";
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("180HzSetupHub/0.1");

        using var response = await client.GetAsync(apiUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Could not check updates. Server returned {(int)response.StatusCode}.";
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var latestTag = document.RootElement.TryGetProperty("tag_name", out var tag)
            ? tag.GetString()
            : null;

        var releaseUrl = document.RootElement.TryGetProperty("html_url", out var url)
            ? url.GetString()
            : settings.Update.ProjectUrl;

        var currentVersion = !string.IsNullOrWhiteSpace(settings.Update.CurrentVersion)
            ? settings.Update.CurrentVersion
            : Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return "Update source responded, but no release tag was found.";
        }

        if (string.Equals(latestTag.TrimStart('v'), currentVersion.TrimStart('v'), StringComparison.OrdinalIgnoreCase))
        {
            return $"180Hz Setup Hub is up to date ({currentVersion}).";
        }

        return $"Update available: {latestTag}. Release page: {releaseUrl}";
    }
}
