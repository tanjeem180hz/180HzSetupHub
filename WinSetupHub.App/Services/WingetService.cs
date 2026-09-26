using System.Net.Http;
using System.Security.Cryptography;
using WinSetupHub.App.Models;
using WinSetupHub.App.Security;
using WinSetupHub.App.Services.Winget;
using Microsoft.Win32;

namespace WinSetupHub.App.Services;

public sealed class WingetService
{
    private static readonly string[] SourceAgreementArgs = ["--accept-source-agreements", "--disable-interactivity"];
    private static readonly string[] PackageAgreementArgs = ["--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"];
    private readonly ProcessRunner _runner;
    private readonly StoragePaths _paths;

    public WingetService(ProcessRunner runner, StoragePaths paths)
    {
        _runner = runner;
        _paths = paths;
    }

    public async Task<bool> IsAvailableAsync(Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var result = await RunWingetAsync(["--version"], log, cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task<PackageStatus> GetStatusAsync(
        PackageDefinition package,
        CancellationToken cancellationToken = default)
    {
        PackageIdValidator.ThrowIfUnsafe(package.Id);
        ValidateSource(package.Source);
        var latestVersion = await GetLatestVersionAsync(package, cancellationToken);
        var installedPackage = await FindInstalledPackageAsync(package, cancellationToken)
            ?? FindInstalledPackageInRegistry(package);

        if (installedPackage is null)
        {
            return new PackageStatus(PackageInstallState.NotInstalled, AvailableVersion: latestVersion);
        }

        var installedVersion = installedPackage.InstalledVersion;
        var availableVersion = installedPackage.AvailableVersion ?? latestVersion;
        var state = IsUpdateAvailable(installedVersion, availableVersion)
            ? PackageInstallState.UpdateAvailable
            : PackageInstallState.Installed;

        return new PackageStatus(state, installedVersion, availableVersion);
    }

    public async Task<ProcessRunResult> InstallAsync(
        PackageDefinition package,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        PackageIdValidator.ThrowIfUnsafe(package.Id);
        ValidateSource(package.Source);
        return await RunPackageCommandPlanAsync(
            "install",
            package,
            BuildInstallTargets(package),
            GetSource(package),
            log,
            cancellationToken);
    }

    public async Task<ProcessRunResult> UpgradeAsync(
        PackageDefinition package,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        PackageIdValidator.ThrowIfUnsafe(package.Id);
        ValidateSource(package.Source);
        return await RunPackageCommandPlanAsync(
            "upgrade",
            package,
            BuildUpgradeTargets(package),
            GetSource(package),
            log,
            cancellationToken);
    }

    public Task<ProcessRunResult> RefreshSourcesAsync(Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        return RunWingetAsync(["source", "update"], log, cancellationToken);
    }

    public async Task<IReadOnlyList<AvailablePackageUpdate>> GetAvailableUpdatesAsync(
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunWingetAsync(["upgrade", .. SourceAgreementArgs], log, cancellationToken);

        if (result.ExitCode != 0 && !ContainsResultText(result, "No installed package found matching input criteria"))
        {
            log?.Invoke("Update scan failed. Repairing winget sources and retrying scan.");
            await RepairSourcesAsync(log, cancellationToken);
            result = await RunWingetAsync(["upgrade", .. SourceAgreementArgs], log, cancellationToken);

            if (result.ExitCode != 0 && !ContainsResultText(result, "No installed package found matching input criteria"))
            {
                return [];
            }
        }

        return WingetOutputParser.ReadAvailableUpdates(result.Output);
    }

    private Task<ProcessRunResult> RunWingetAsync(
        IReadOnlyList<string> arguments,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        return _runner.RunAsync(ResolveWingetExecutable(), arguments, log, cancellationToken);
    }

    private async Task<ProcessRunResult> RunPackageCommandPlanAsync(
        string command,
        PackageDefinition package,
        IReadOnlyList<WingetPackageTarget> targets,
        string source,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        ProcessRunResult? lastResult = null;
        var sourceRepairAttempted = false;

        foreach (var target in targets)
        {
            foreach (var scope in BuildScopeOptions(command, source))
            {
                var args = BuildPackageCommandArgs(command, target, source, scope);
                log?.Invoke(BuildAttemptMessage(command, package.Name, target, scope));

                var result = await RunWingetWithHashRecoveryAsync(args, log, cancellationToken);
                if (IsSuccessfulPackageResult(result))
                {
                    return result with { ExitCode = 0 };
                }

                lastResult = result;

                if (!sourceRepairAttempted && ShouldRepairSources(result))
                {
                    sourceRepairAttempted = true;
                    await RepairSourcesAsync(log, cancellationToken);
                    log?.Invoke("Retrying after winget source repair.");

                    result = await RunWingetWithHashRecoveryAsync(args, log, cancellationToken);
                    if (IsSuccessfulPackageResult(result))
                    {
                        return result with { ExitCode = 0 };
                    }

                    lastResult = result;
                }
            }
        }

        if (source.Equals("winget", StringComparison.OrdinalIgnoreCase))
        {
            var directResult = await TryDirectInstallerFallbackAsync(package, log, cancellationToken);
            if (IsSuccessfulPackageResult(directResult))
            {
                return directResult with { ExitCode = 0 };
            }

            lastResult = directResult;
        }

        return lastResult ?? new ProcessRunResult(-1, string.Empty, $"{package.Name} did not start.");
    }

    private async Task<ProcessRunResult> TryDirectInstallerFallbackAsync(
        PackageDefinition package,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        try
        {
            log?.Invoke($"Winget command plan did not finish {package.Name}. Trying direct installer fallback.");
            var info = await GetInstallerInfoAsync(package, cancellationToken);
            if (info is null)
            {
                return new ProcessRunResult(-1, string.Empty, "No direct installer metadata was available.");
            }

            if (!IsSupportedDirectInstaller(info))
            {
                return new ProcessRunResult(-1, string.Empty, $"Direct installer fallback is not supported for {info.InstallerType} packages.");
            }

            var installerPath = await DownloadInstallerAsync(package, info, log, cancellationToken);
            if (!await VerifyInstallerHashAsync(installerPath, info, log, cancellationToken))
            {
                return new ProcessRunResult(-1, string.Empty, "Direct installer hash verification failed.");
            }

            var command = BuildDirectInstallerCommand(installerPath, info);
            log?.Invoke($"Running direct installer fallback for {package.Name} ({info.InstallerType}).");
            return await _runner.RunAsync(command.FileName, command.Arguments, log, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(-1, string.Empty, $"Direct installer fallback failed: {ex.Message}");
        }
    }

    private async Task<WingetInstallerInfo?> GetInstallerInfoAsync(
        PackageDefinition package,
        CancellationToken cancellationToken)
    {
        var result = await RunWingetAsync(
            ["show", "--id", package.Id, "--exact", "--source", "winget", .. SourceAgreementArgs],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0
            ? WingetOutputParser.ReadInstallerInfo(result.Output)
            : null;
    }

    private async Task<string> DownloadInstallerAsync(
        PackageDefinition package,
        WingetInstallerInfo info,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Downloads);
        var extension = Path.GetExtension(new Uri(info.InstallerUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = info.InstallerType.Equals("msi", StringComparison.OrdinalIgnoreCase)
                || info.InstallerType.Equals("wix", StringComparison.OrdinalIgnoreCase)
                    ? ".msi"
                    : ".exe";
        }

        var installerPath = Path.Combine(
            _paths.Downloads,
            $"{SanitizeFileName(package.Id)}-{DateTime.Now:yyyyMMddHHmmss}{extension}");

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(installerPath);

        var buffer = new byte[256 * 1024];
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

            if (DateTime.Now - lastReport >= TimeSpan.FromMilliseconds(750))
            {
                lastReport = DateTime.Now;
                log?.Invoke(totalBytes is > 0
                    ? $"{SizeFormatter.Format(downloaded)} / {SizeFormatter.Format(totalBytes.Value)}"
                    : $"Downloaded {SizeFormatter.Format(downloaded)}");
            }
        }

        log?.Invoke(totalBytes is > 0
            ? $"{SizeFormatter.Format(downloaded)} / {SizeFormatter.Format(totalBytes.Value)}"
            : $"Downloaded {SizeFormatter.Format(downloaded)}");
        return installerPath;
    }

    private static async Task<bool> VerifyInstallerHashAsync(
        string installerPath,
        WingetInstallerInfo info,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(info.InstallerSha256))
        {
            log?.Invoke("Direct installer did not publish a SHA256 hash. Continuing with winget metadata URL.");
            return true;
        }

        await using var stream = File.OpenRead(installerPath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var hash = Convert.ToHexString(hashBytes);
        var ok = hash.Equals(info.InstallerSha256, StringComparison.OrdinalIgnoreCase);
        log?.Invoke(ok ? "Direct installer hash verified." : "Direct installer hash mismatch.");
        return ok;
    }

    private static (string FileName, IReadOnlyList<string> Arguments) BuildDirectInstallerCommand(
        string installerPath,
        WingetInstallerInfo info)
    {
        var extension = Path.GetExtension(installerPath);
        var type = info.InstallerType.Trim();

        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
            || type.Equals("msi", StringComparison.OrdinalIgnoreCase)
            || type.Equals("wix", StringComparison.OrdinalIgnoreCase))
        {
            return ("msiexec.exe", ["/i", installerPath, "/qn", "/norestart"]);
        }

        if (type.Equals("nullsoft", StringComparison.OrdinalIgnoreCase))
        {
            return (installerPath, ["/S"]);
        }

        if (type.Equals("inno", StringComparison.OrdinalIgnoreCase))
        {
            return (installerPath, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-"]);
        }

        if (type.Equals("burn", StringComparison.OrdinalIgnoreCase)
            || type.Equals("exe", StringComparison.OrdinalIgnoreCase))
        {
            return (installerPath, ["/quiet", "/norestart"]);
        }

        return (installerPath, ["/S"]);
    }

    private static bool IsSupportedDirectInstaller(WingetInstallerInfo info)
    {
        if (!Uri.TryCreate(info.InstallerUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msix", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appxbundle", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
            || info.InstallerType.Equals("exe", StringComparison.OrdinalIgnoreCase)
            || info.InstallerType.Equals("msi", StringComparison.OrdinalIgnoreCase)
            || info.InstallerType.Equals("wix", StringComparison.OrdinalIgnoreCase)
            || info.InstallerType.Equals("nullsoft", StringComparison.OrdinalIgnoreCase)
            || info.InstallerType.Equals("inno", StringComparison.OrdinalIgnoreCase)
            || info.InstallerType.Equals("burn", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "installer" : sanitized;
    }

    private async Task<ProcessRunResult> RunWingetWithHashRecoveryAsync(
        IReadOnlyList<string> arguments,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var result = await RunWingetAsync(arguments, log, cancellationToken);
        if (!IsInstallerHashMismatch(result))
        {
            return result;
        }

        log?.Invoke("Installer hash mismatch detected. Refreshing winget sources and retrying once.");
        await RefreshSourcesAsync(log, cancellationToken);
        return await RunWingetAsync(arguments, log, cancellationToken);
    }

    private async Task RepairSourcesAsync(Action<string>? log, CancellationToken cancellationToken)
    {
        log?.Invoke("Refreshing winget sources.");
        var updateResult = await RefreshSourcesAsync(log, cancellationToken);
        if (updateResult.ExitCode == 0)
        {
            return;
        }

        log?.Invoke("Winget source update failed. Resetting default sources and retrying.");
        await RunWingetAsync(["source", "reset", "--force"], log, cancellationToken);
        await RefreshSourcesAsync(log, cancellationToken);
    }

    private static bool IsAlreadyInstalledOrCurrentResult(ProcessRunResult result)
    {
        return ContainsResultText(result, "No available upgrade found")
            || ContainsResultText(result, "No newer package versions are available")
            || (result.ExitCode == 0 && ContainsResultText(result, "Found an existing package already installed"));
    }

    private static bool IsSuccessfulPackageResult(ProcessRunResult result)
    {
        return result.ExitCode == 0 || IsAlreadyInstalledOrCurrentResult(result);
    }

    private static bool ShouldRepairSources(ProcessRunResult result)
    {
        return result.ExitCode != 0
            && (IsNoPackageFoundResult(result)
                || IsNoInstalledPackageResult(result)
                || ContainsResultText(result, "Failed in attempting to update the source")
                || ContainsResultText(result, "source requires")
                || ContainsResultText(result, "source agreements")
                || ContainsResultText(result, "0x8a15000f")
                || ContainsResultText(result, "Data required by the source is missing"));
    }

    private static IReadOnlyList<string?> BuildScopeOptions(string command, string source)
    {
        if (!source.Equals("winget", StringComparison.OrdinalIgnoreCase))
        {
            return [null];
        }

        return command.Equals("install", StringComparison.OrdinalIgnoreCase)
            ? ["user", null]
            : [null, "user"];
    }

    private static IReadOnlyList<string> BuildPackageCommandArgs(
        string command,
        WingetPackageTarget target,
        string source,
        string? scope)
    {
        var args = new List<string>
        {
            command,
            target.Option,
            target.Value,
            "--exact",
            "--source",
            source
        };

        if (source.Equals("winget", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--silent");
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            args.Add("--scope");
            args.Add(scope);
        }

        args.AddRange(PackageAgreementArgs);
        return args;
    }

    private static string BuildAttemptMessage(
        string command,
        string packageName,
        WingetPackageTarget target,
        string? scope)
    {
        var action = command.Equals("upgrade", StringComparison.OrdinalIgnoreCase) ? "update" : "install";
        var scopeText = string.IsNullOrWhiteSpace(scope) ? "default scope" : $"{scope} scope";
        var targetText = target.Option == "--id" ? $"id {target.Value}" : $"name {target.Value}";
        return $"Trying {packageName} {action} with {targetText} ({scopeText}).";
    }

    private static IReadOnlyList<WingetPackageTarget> BuildInstallTargets(PackageDefinition package)
    {
        var targets = new List<WingetPackageTarget> { new("--id", package.Id) };

        foreach (var name in new[] { package.Name }.Concat(package.DetectionNames))
        {
            AddTarget(targets, name);
        }

        return targets;
    }

    private static IReadOnlyList<WingetPackageTarget> BuildUpgradeTargets(PackageDefinition package)
    {
        var targets = new List<WingetPackageTarget> { new("--id", package.Id) };

        foreach (var name in BuildDetectionNames(package, includeIdAliases: true))
        {
            AddTarget(targets, name);
        }

        return targets;
    }

    private static void AddTarget(List<WingetPackageTarget> targets, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        value = value.Trim();
        var option = PackageIdValidator.IsSafe(value) && value.Contains('.', StringComparison.Ordinal)
            ? "--id"
            : "--name";

        if (targets.Any(target => target.Option == option && target.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        targets.Add(new WingetPackageTarget(option, value));
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildUpgradeFallbackArgs(PackageDefinition package)
    {
        var args = new List<IReadOnlyList<string>>();
        var source = GetSource(package);
        foreach (var detectionName in BuildDetectionNames(package, includeIdAliases: true))
        {
            if (PackageIdValidator.IsSafe(detectionName) && detectionName.Contains('.', StringComparison.Ordinal))
            {
                args.Add(["upgrade", "--id", detectionName, "--exact", "--source", source, .. PackageAgreementArgs]);
                continue;
            }

            args.Add(["upgrade", "--name", detectionName, "--exact", "--source", source, .. PackageAgreementArgs]);
        }

        return args;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildInstallFallbackArgs(PackageDefinition package)
    {
        var source = GetSource(package);
        var fallbackNames = new List<string> { package.Name };
        fallbackNames.AddRange(package.DetectionNames);

        return fallbackNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !string.Equals(name, package.Id, StringComparison.OrdinalIgnoreCase))
            .Select<string, IReadOnlyList<string>>(name =>
                PackageIdValidator.IsSafe(name) && name.Contains('.', StringComparison.Ordinal)
                    ? ["install", "--id", name, "--exact", "--source", source, .. PackageAgreementArgs]
                    : ["install", "--name", name, "--exact", "--source", source, .. PackageAgreementArgs])
            .ToList();
    }

    private static bool IsInstallerHashMismatch(ProcessRunResult result)
    {
        return ContainsResultText(result, "Installer hash does not match");
    }

    private static bool IsNoInstalledPackageResult(ProcessRunResult result)
    {
        return result.ExitCode != 0
            && ContainsResultText(result, "No installed package found matching input criteria");
    }

    private static bool IsNoPackageFoundResult(ProcessRunResult result)
    {
        return result.ExitCode != 0
            && ContainsResultText(result, "No package found matching input criteria");
    }

    private static bool ContainsResultText(ProcessRunResult result, string text)
    {
        return result.Output.Contains(text, StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSource(PackageDefinition package)
    {
        return string.IsNullOrWhiteSpace(package.Source)
            ? "winget"
            : package.Source.Trim().ToLowerInvariant();
    }

    private static void ValidateSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)
            || source.Equals("winget", StringComparison.OrdinalIgnoreCase)
            || source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Package source is not supported: {source}");
    }

    private static string? NormalizeVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            || version.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? null
            : version.Trim();
    }

    private async Task<string?> GetLatestVersionAsync(PackageDefinition package, CancellationToken cancellationToken)
    {
        PackageIdValidator.ThrowIfUnsafe(package.Id);
        var result = await RunWingetAsync(
            ["show", "--id", package.Id, "--exact", "--source", GetSource(package), .. SourceAgreementArgs],
            cancellationToken: cancellationToken);

        return NormalizeVersion(WingetOutputParser.ReadField(result.Output, "Version"));
    }

    private async Task<InstalledPackageInfo?> FindInstalledPackageAsync(
        PackageDefinition package,
        CancellationToken cancellationToken)
    {
        var detectionNames = BuildDetectionNames(package, includeIdAliases: true);
        var source = GetSource(package);
        var listByIdResult = await RunWingetAsync(
            ["list", "--id", package.Id, "--exact", "--source", source, .. SourceAgreementArgs],
            cancellationToken: cancellationToken);

        if (listByIdResult.Output.Contains(package.Id, StringComparison.OrdinalIgnoreCase))
        {
            var versions = WingetOutputParser.ReadVersions(listByIdResult.Output, package.Id);
            var installedPackage = CreateInstalledPackage(versions.InstalledVersion, versions.AvailableVersion);
            if (installedPackage is not null)
            {
                return installedPackage;
            }
        }

        foreach (var detectionName in detectionNames)
        {
            var exactNameResult = await RunWingetAsync(
                ["list", "--name", detectionName, "--exact", "--source", source, .. SourceAgreementArgs],
                cancellationToken: cancellationToken);

            if (exactNameResult.Output.Contains(detectionName, StringComparison.OrdinalIgnoreCase))
            {
                var versions = WingetOutputParser.ReadVersionsByName(exactNameResult.Output, detectionName);
                var installedPackage = CreateInstalledPackage(versions.InstalledVersion, versions.AvailableVersion);
                if (installedPackage is not null)
                {
                    return installedPackage;
                }
            }
        }

        return null;
    }

    private static InstalledPackageInfo? FindInstalledPackageInRegistry(PackageDefinition package)
    {
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var registryHive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                using var baseKey = RegistryKey.OpenBaseKey(registryHive, registryView);
                using var uninstallKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var appKey = uninstallKey.OpenSubKey(subKeyName);
                    var displayName = appKey?.GetValue("DisplayName") as string;
                    if (!IsDisplayNameMatch(package, displayName))
                    {
                        continue;
                    }

                    var installedVersion = appKey?.GetValue("DisplayVersion") as string;
                    return new InstalledPackageInfo(installedVersion, null);
                }
            }
        }

        return null;
    }

    private static bool IsDisplayNameMatch(PackageDefinition package, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return BuildDetectionNames(package, includeIdAliases: false).Any(candidate => IsNameMatch(displayName, candidate));
    }

    private static IReadOnlyList<string> BuildDetectionNames(PackageDefinition package, bool includeIdAliases)
    {
        var names = new List<string> { package.Name };
        names.AddRange(package.DetectionNames);

        if (includeIdAliases)
        {
            var idSegments = package.Id.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (idSegments.Length > 1)
            {
                names.Add(string.Join(' ', idSegments.Skip(1)));
                names.Add(idSegments[^1]);
            }
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static InstalledPackageInfo? CreateInstalledPackage(string? installedVersion, string? availableVersion)
    {
        installedVersion = NormalizeVersion(installedVersion);
        availableVersion = NormalizeVersion(availableVersion);

        return string.IsNullOrWhiteSpace(installedVersion)
            ? null
            : new InstalledPackageInfo(installedVersion, availableVersion);
    }

    private static bool IsNameMatch(string source, string candidate)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (string.Equals(source, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedSource = NormalizeForMatch(source);
        var normalizedCandidate = NormalizeForMatch(candidate);

        if (normalizedSource.Length == 0 || normalizedCandidate.Length == 0)
        {
            return false;
        }

        if (normalizedSource == normalizedCandidate)
        {
            return true;
        }

        var sourceTokens = TokenizeForMatch(source);
        var candidateTokens = TokenizeForMatch(candidate);

        if (candidateTokens.Count == 0 || sourceTokens.Count < candidateTokens.Count)
        {
            return false;
        }

        if (!candidateTokens.SequenceEqual(sourceTokens.Take(candidateTokens.Count), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return sourceTokens.Count == candidateTokens.Count
            || IsAcceptableDisplayNameSuffix(sourceTokens[candidateTokens.Count]);
    }

    private static string NormalizeForMatch(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static IReadOnlyList<string> TokenizeForMatch(string value)
    {
        return value
            .Split([' ', '.', '-', '_', '+', '(', ')', '[', ']', '{', '}', ',', ':', ';', '/', '\\', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeForMatch)
            .Where(token => token.Length > 0)
            .ToList();
    }

    private static bool IsAcceptableDisplayNameSuffix(string token)
    {
        return token.Length > 0
            && (char.IsDigit(token[0])
                || token.Equals("x64", StringComparison.OrdinalIgnoreCase)
                || token.Equals("x86", StringComparison.OrdinalIgnoreCase)
                || token.Equals("arm64", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUpdateAvailable(string? installedVersion, string? availableVersion)
    {
        if (string.IsNullOrWhiteSpace(availableVersion) || string.IsNullOrWhiteSpace(installedVersion))
        {
            return false;
        }

        var comparison = CompareVersions(installedVersion, availableVersion);
        return comparison.HasValue
            ? comparison.Value < 0
            : !string.Equals(installedVersion, availableVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static int? CompareVersions(string installedVersion, string availableVersion)
    {
        var installedParts = ReadNumericVersionParts(installedVersion);
        var availableParts = ReadNumericVersionParts(availableVersion);
        if (installedParts.Count == 0 || availableParts.Count == 0)
        {
            return null;
        }

        var length = Math.Max(installedParts.Count, availableParts.Count);
        for (var index = 0; index < length; index++)
        {
            var installed = index < installedParts.Count ? installedParts[index] : 0;
            var available = index < availableParts.Count ? availableParts[index] : 0;
            var comparison = installed.CompareTo(available);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static IReadOnlyList<long> ReadNumericVersionParts(string version)
    {
        var normalized = version
            .Split(['-', '+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var value) ? value : (long?)null)
            .TakeWhile(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    }

    private static string ResolveWingetExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var windowsAppsWinget = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(windowsAppsWinget))
            {
                return windowsAppsWinget;
            }
        }

        return "winget";
    }

    private sealed record WingetPackageTarget(string Option, string Value);

    private sealed record InstalledPackageInfo(string? InstalledVersion, string? AvailableVersion);
}
