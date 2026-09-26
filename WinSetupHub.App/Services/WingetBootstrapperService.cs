using System.Net.Http;

namespace WinSetupHub.App.Services;

public sealed class WingetBootstrapperService
{
    private const string AppInstallerBundleUrl = "https://aka.ms/getwinget";
    private readonly StoragePaths _paths;
    private readonly ProcessRunner _runner;

    public WingetBootstrapperService(StoragePaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public async Task<bool> EnsureWingetAsync(Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        if (await IsWingetAvailableAsync(log, cancellationToken))
        {
            return true;
        }

        log?.Invoke("Windows Package Manager was not available. Requesting App Installer registration.");
        await RegisterBuiltInAppInstallerAsync(log, cancellationToken);
        if (await IsWingetAvailableAsync(log, cancellationToken))
        {
            return true;
        }

        log?.Invoke("Downloading Microsoft App Installer for Windows Package Manager.");
        var bundlePath = await DownloadAppInstallerBundleAsync(log, cancellationToken);
        log?.Invoke("Installing Microsoft App Installer package.");
        await InstallAppInstallerBundleAsync(bundlePath, log, cancellationToken);

        return await IsWingetAvailableAsync(log, cancellationToken);
    }

    private async Task<bool> IsWingetAvailableAsync(Action<string>? log, CancellationToken cancellationToken)
    {
        foreach (var executable in ResolveWingetCandidates())
        {
            var result = await _runner.RunAsync(executable, ["--version"], log, cancellationToken);
            if (result.ExitCode == 0)
            {
                log?.Invoke($"Windows Package Manager is ready: {result.Output.Trim()}");
                return true;
            }
        }

        return false;
    }

    private async Task RegisterBuiltInAppInstallerAsync(Action<string>? log, CancellationToken cancellationToken)
    {
        await _runner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe"
            ],
            log,
            cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private async Task<string> DownloadAppInstallerBundleAsync(Action<string>? log, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Downloads);
        var bundlePath = Path.Combine(_paths.Downloads, "Microsoft.DesktopAppInstaller.msixbundle");

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(AppInstallerBundleUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(bundlePath);
        var buffer = new byte[128 * 1024];
        long downloaded = 0;
        var lastReport = DateTime.MinValue;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            if (DateTime.Now - lastReport > TimeSpan.FromSeconds(2))
            {
                lastReport = DateTime.Now;
                log?.Invoke($"Downloaded App Installer: {SizeFormatter.Format(downloaded)}");
            }
        }

        log?.Invoke($"Downloaded App Installer: {SizeFormatter.Format(downloaded)}");
        return bundlePath;
    }

    private async Task InstallAppInstallerBundleAsync(string bundlePath, Action<string>? log, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                $"Add-AppxPackage -Path {QuotePowerShell(bundlePath)}"
            ],
            log,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            log?.Invoke("App Installer package install did not complete. The machine may need Microsoft Store/Appx support enabled.");
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private static IReadOnlyList<string> ResolveWingetCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string> { "winget.exe", "winget" };

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Insert(0, Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe"));
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string QuotePowerShell(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
