using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinSetupHub.App.Models;

namespace WinSetupHub.App.Services;

public sealed partial class UninstallService
{
    private static readonly string[] RegistrySubKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private static readonly string[] IgnoredTokens =
    [
        "app", "apps", "application", "setup", "installer", "install", "update", "updates",
        "windows", "microsoft", "corporation", "corp", "company", "software", "technologies",
        "technology", "limited", "ltd", "inc", "llc", "the", "and", "for", "x64", "x86"
    ];

    private readonly ProcessRunner _runner;

    public UninstallService(ProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var apps = await Task.Run(ReadRegistryApps, cancellationToken);
        var storeApps = await ReadStoreAppsAsync(cancellationToken);

        return apps
            .Concat(storeApps)
            .GroupBy(app => BuildDedupKey(app), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProcessRunResult> UninstallAsync(
        InstalledApplication app,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        if (app.IsProtected)
        {
            return new ProcessRunResult(-1, string.Empty, app.ProtectionReason);
        }

        if (app.Kind == InstalledApplicationKind.Store)
        {
            if (string.IsNullOrWhiteSpace(app.PackageFullName))
            {
                return new ProcessRunResult(-1, string.Empty, "Store package identity was not found.");
            }

            return await _runner.RunAsync(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"Remove-AppxPackage -Package {QuotePowerShell(app.PackageFullName)}"
                ],
                onOutput,
                cancellationToken);
        }

        var command = BuildUninstallCommand(app);
        if (command is null)
        {
            return new ProcessRunResult(-1, string.Empty, "No uninstall command was found.");
        }

        return await _runner.RunAsync(command.Value.FileName, command.Value.Arguments, onOutput, cancellationToken);
    }

    public async Task<IReadOnlyList<LeftoverItem>> ScanLeftoversAsync(
        InstalledApplication app,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<LeftoverItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appTokens = BuildTokens(app.Name);

            AddInstallLocation(app, appTokens, results, seen);

            foreach (var root in BuildSafeScanRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanRoot(root, appTokens, results, seen, cancellationToken);
            }

            return results
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Take(300)
                .ToList();
        }, cancellationToken);
    }

    public async Task<(long DeletedBytes, int DeletedCount, int SkippedCount)> DeleteLeftoversAsync(
        InstalledApplication app,
        IReadOnlyCollection<LeftoverItem> leftovers,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            long deletedBytes = 0;
            var deletedCount = 0;
            var skippedCount = 0;

            foreach (var item in leftovers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsSafeDeletePath(item.Path) || !IsSafeLeftoverForApp(app, item.Path))
                {
                    skippedCount++;
                    onOutput?.Invoke($"Skipped unsafe path: {item.Path}");
                    continue;
                }

                try
                {
                    if (Directory.Exists(item.Path))
                    {
                        Directory.Delete(item.Path, recursive: true);
                    }
                    else if (File.Exists(item.Path))
                    {
                        File.Delete(item.Path);
                    }
                    else
                    {
                        skippedCount++;
                        continue;
                    }

                    deletedBytes += item.SizeBytes;
                    deletedCount++;
                    onOutput?.Invoke($"Deleted leftover: {item.Path}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    skippedCount++;
                    onOutput?.Invoke($"Skipped locked leftover: {item.Path}");
                }
            }

            return (deletedBytes, deletedCount, skippedCount);
        }, cancellationToken);
    }

    private static IReadOnlyList<InstalledApplication> ReadRegistryApps()
    {
        var apps = new List<InstalledApplication>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);

                foreach (var subKeyPath in RegistrySubKeys)
                {
                    using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                    if (uninstallKey is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using var appKey = uninstallKey.OpenSubKey(subKeyName);
                        if (appKey is null)
                        {
                            continue;
                        }

                        var app = ReadRegistryApp(appKey, $"{hive}\\{view}\\{subKeyPath}\\{subKeyName}");
                        if (app is not null)
                        {
                            apps.Add(app);
                        }
                    }
                }
            }
        }

        return apps;
    }

    private static InstalledApplication? ReadRegistryApp(RegistryKey appKey, string registryPath)
    {
        var name = ReadString(appKey, "DisplayName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var releaseType = ReadString(appKey, "ReleaseType");
        if (IsHiddenUpdate(name, releaseType))
        {
            return null;
        }

        var uninstallString = ReadString(appKey, "UninstallString");
        var quietUninstallString = ReadString(appKey, "QuietUninstallString");
        var systemComponent = ReadInt(appKey, "SystemComponent") == 1;
        if (string.IsNullOrWhiteSpace(uninstallString) || systemComponent || IsCoreWindowsComponent(name, releaseType))
        {
            return null;
        }

        return new InstalledApplication
        {
            Id = registryPath,
            Kind = InstalledApplicationKind.Win32,
            Name = name.Trim(),
            Version = ReadString(appKey, "DisplayVersion"),
            Publisher = ReadString(appKey, "Publisher"),
            InstallLocation = ExpandPath(ReadString(appKey, "InstallLocation")),
            IconPath = ResolveIconPath(ReadString(appKey, "DisplayIcon"), ExpandPath(ReadString(appKey, "InstallLocation"))),
            UninstallString = uninstallString,
            QuietUninstallString = quietUninstallString,
            RegistryPath = registryPath,
            IsProtected = false,
            ProtectionReason = string.Empty
        };
    }

    private async Task<IReadOnlyList<InstalledApplication>> ReadStoreAppsAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                "Get-AppxPackage | Where-Object { -not $_.IsFramework } | Select-Object Name,PackageFullName,Version,Publisher,NonRemovable,InstallLocation | ConvertTo-Json -Compress"
            ],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            return [];
        }

        try
        {
            var apps = new List<InstalledApplication>();
            using var document = JsonDocument.Parse(result.Output);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    AddStoreApp(apps, element);
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddStoreApp(apps, document.RootElement);
            }

            return apps;
        }
        catch
        {
            return [];
        }
    }

    private static void AddStoreApp(List<InstalledApplication> apps, JsonElement element)
    {
        var name = ReadJsonString(element, "Name");
        var packageFullName = ReadJsonString(element, "PackageFullName");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(packageFullName))
        {
            return;
        }

        var nonRemovable = ReadJsonBool(element, "NonRemovable");
        if (nonRemovable)
        {
            return;
        }

        apps.Add(new InstalledApplication
        {
            Id = packageFullName,
            Kind = InstalledApplicationKind.Store,
            Name = name,
            Version = ReadJsonString(element, "Version"),
            Publisher = ReadJsonString(element, "Publisher"),
            InstallLocation = ReadJsonString(element, "InstallLocation"),
            IconPath = FindStoreLogoPath(ReadJsonString(element, "InstallLocation")),
            PackageFullName = packageFullName,
            IsProtected = false,
            ProtectionReason = string.Empty
        });
    }

    private static (string FileName, IReadOnlyList<string> Arguments)? BuildUninstallCommand(InstalledApplication app)
    {
        var commandLine = string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.UninstallString
            : app.QuietUninstallString;

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        commandLine = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        var msiCode = MsiProductCodeRegex().Match(commandLine);
        if (msiCode.Success && commandLine.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            return ("msiexec.exe", ["/x", msiCode.Value, "/qn", "/norestart"]);
        }

        var parts = SplitCommandLine(commandLine);
        if (parts.Count == 0)
        {
            return null;
        }

        var fileName = ExpandPath(parts[0]);
        var arguments = parts.Skip(1).ToList();
        if (!IsRunnableCommand(fileName) && TrySplitUnquotedExecutable(commandLine, out var repairedFileName, out var repairedArguments))
        {
            fileName = repairedFileName;
            arguments = repairedArguments;
        }

        if (Path.GetFileName(fileName).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            arguments = NormalizeMsiArguments(arguments);
        }

        return (fileName, arguments);
    }

    private static bool IsRunnableCommand(string fileName)
    {
        return File.Exists(fileName)
            || !Path.IsPathFullyQualified(fileName)
            || fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySplitUnquotedExecutable(
        string commandLine,
        out string fileName,
        out List<string> arguments)
    {
        fileName = string.Empty;
        arguments = [];

        foreach (var extension in new[] { ".exe", ".cmd", ".bat", ".msi" })
        {
            var index = commandLine.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var candidate = ExpandPath(commandLine[..(index + extension.Length)].Trim().Trim('"'));
            if (!File.Exists(candidate))
            {
                continue;
            }

            var remainder = commandLine[(index + extension.Length)..].Trim();
            fileName = candidate;
            arguments = string.IsNullOrWhiteSpace(remainder)
                ? []
                : SplitCommandLine(remainder).ToList();
            return true;
        }

        return false;
    }

    private static List<string> NormalizeMsiArguments(List<string> arguments)
    {
        var normalized = new List<string>();
        var addedQuiet = false;

        foreach (var argument in arguments)
        {
            if (argument.Equals("/i", StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add("/x");
                continue;
            }

            if (argument.StartsWith("/I{", StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add("/x");
                normalized.Add(argument[2..]);
                continue;
            }

            if (argument.Equals("/qn", StringComparison.OrdinalIgnoreCase) || argument.Equals("/quiet", StringComparison.OrdinalIgnoreCase))
            {
                addedQuiet = true;
            }

            normalized.Add(argument);
        }

        if (!normalized.Any(argument => argument.Equals("/x", StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Insert(0, "/x");
        }

        if (!addedQuiet)
        {
            normalized.Add("/qn");
        }

        if (!normalized.Any(argument => argument.Equals("/norestart", StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Add("/norestart");
        }

        return normalized;
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var result = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                var argument = Marshal.PtrToStringUni(argumentPointer);
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    result.Add(argument);
                }
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    private static void AddInstallLocation(
        InstalledApplication app,
        IReadOnlyCollection<string> appTokens,
        List<LeftoverItem> results,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(app.InstallLocation)
            || !Directory.Exists(app.InstallLocation)
            || !IsSafeDeletePath(app.InstallLocation))
        {
            return;
        }

        var name = Path.GetFileName(app.InstallLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!MatchesCandidate(name, appTokens))
        {
            return;
        }

        AddLeftover(app.InstallLocation, "Install location", "Original install folder", results, seen);
    }

    private static void ScanRoot(
        string root,
        IReadOnlyCollection<string> appTokens,
        List<LeftoverItem> results,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in SafeEnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            var parentMatches = MatchesCandidate(name, appTokens);

            if (parentMatches)
            {
                AddLeftover(directory, "Folder", "Folder name matched app", results, seen);
            }

            if (results.Count >= 300)
            {
                return;
            }

            foreach (var child in SafeEnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childName = Path.GetFileName(child);
                if (MatchesCandidate(childName, appTokens))
                {
                    AddLeftover(child, "Folder", "Nested app data match", results, seen);
                }

                if (results.Count >= 300)
                {
                    return;
                }
            }
        }
    }

    private static void AddLeftover(
        string path,
        string kind,
        string reason,
        List<LeftoverItem> results,
        HashSet<string> seen)
    {
        var fullPath = Path.GetFullPath(path);
        if (!seen.Add(fullPath) || !IsSafeDeletePath(fullPath))
        {
            return;
        }

        results.Add(new LeftoverItem
        {
            Path = fullPath,
            Kind = kind,
            SizeBytes = MeasurePath(fullPath),
            Reason = reason
        });
    }

    private static IReadOnlyList<string> BuildSafeScanRoots()
    {
        return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSafeDeletePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (ContainsProtectedSegment(fullPath))
            {
                return false;
            }

            var roots = BuildSafeScanRoots()
                .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"))
                .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .ToList();

            if (fullPath.Equals(windows, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(system, StringComparison.OrdinalIgnoreCase)
                || fullPath.Equals(profile, StringComparison.OrdinalIgnoreCase)
                || roots.Any(root => fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return roots.Any(root => fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeLeftoverForApp(InstalledApplication app, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var appTokens = BuildTokens(app.Name);
            if (appTokens.Count == 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(app.InstallLocation)
                && Directory.Exists(app.InstallLocation)
                && IsSafeDeletePath(app.InstallLocation)
                && fullPath.StartsWith(
                    Path.GetFullPath(app.InstallLocation).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var leafName = Path.GetFileName(fullPath);
            var parentName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? string.Empty);
            return MatchesCandidate(leafName, appTokens) || MatchesCandidate(parentName, appTokens);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsProtectedSegment(string fullPath)
    {
        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("SystemApps", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("WinSxS", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("System32", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("SysWOW64", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }

    private static long MeasurePath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return 0;
            }

            long size = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch
                {
                    // Locked files are counted as zero; delete will report them as skipped later.
                }
            }

            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static IReadOnlyList<string> BuildTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return TokenRegex()
            .Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(token => token.Length >= 4 && !IgnoredTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesCandidate(string candidate, IReadOnlyCollection<string> appTokens)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.ToLowerInvariant();
        return appTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildDedupKey(InstalledApplication app)
    {
        if (!string.IsNullOrWhiteSpace(app.PackageFullName))
        {
            return app.PackageFullName;
        }

        return $"{app.Kind}:{app.Name}:{app.Publisher}:{app.Version}";
    }

    private static bool IsHiddenUpdate(string name, string releaseType)
    {
        return releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)
            || releaseType.Contains("Security Update", StringComparison.OrdinalIgnoreCase)
            || releaseType.Contains("Update Rollup", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Security Update for ", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Hotfix for ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoreWindowsComponent(string name, string releaseType)
    {
        return releaseType.Contains("Driver", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Windows Driver Package", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Microsoft Windows", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(RegistryKey key, string name)
    {
        return key.GetValue(name)?.ToString()?.Trim() ?? string.Empty;
    }

    private static int ReadInt(RegistryKey key, string name)
    {
        return key.GetValue(name) is int value ? value : 0;
    }

    private static string ReadJsonString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : string.Empty;
    }

    private static bool ReadJsonBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    }

    private static string ResolveIconPath(string displayIcon, string installLocation)
    {
        var iconPath = NormalizeIconSpec(displayIcon);
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            return iconPath;
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            try
            {
                return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path).Contains("unins", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static string NormalizeIconSpec(string displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return string.Empty;
        }

        var value = Environment.ExpandEnvironmentVariables(displayIcon.Trim().Trim('"'));
        var commaIndex = value.LastIndexOf(',');
        if (commaIndex > 1 && int.TryParse(value[(commaIndex + 1)..].Trim(), out _))
        {
            value = value[..commaIndex].Trim().Trim('"');
        }

        return value;
    }

    private static string FindStoreLogoPath(string installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return string.Empty;
        }

        try
        {
            var assets = Path.Combine(installLocation, "Assets");
            var searchRoot = Directory.Exists(assets) ? assets : installLocation;
            return Directory.EnumerateFiles(searchRoot, "*.png", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.Contains("Square44x44Logo", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("StoreLogo", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Logo", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(path => Path.GetFileName(path).Contains("Square44x44Logo", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => Path.GetFileName(path).Length)
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QuotePowerShell(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex MsiProductCodeRegex();

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex TokenRegex();

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
