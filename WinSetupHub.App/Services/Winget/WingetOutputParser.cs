using WinSetupHub.App.Services;

namespace WinSetupHub.App.Services.Winget;

public static class WingetOutputParser
{
    public static string? ReadField(string output, string fieldName)
    {
        var prefix = $"{fieldName}:";
        var line = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return line is null
            ? null
            : line[prefix.Length..].Trim();
    }

    public static (string? InstalledVersion, string? AvailableVersion) ReadVersions(string output, string packageId)
    {
        var line = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(candidate => candidate.Contains(packageId, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(line))
        {
            return (null, null);
        }

        var idIndex = line.IndexOf(packageId, StringComparison.OrdinalIgnoreCase);
        if (idIndex < 0)
        {
            return (null, null);
        }

        var versionTokens = line[(idIndex + packageId.Length)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return versionTokens.Length switch
        {
            >= 2 => (versionTokens[0], versionTokens[1]),
            1 => (versionTokens[0], null),
            _ => (null, null)
        };
    }

    public static (string? InstalledVersion, string? AvailableVersion) ReadVersionsByName(string output, string packageName)
    {
        var line = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(candidate => candidate.StartsWith(packageName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(line))
        {
            return (null, null);
        }

        var versionTokens = line[packageName.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return versionTokens.Length switch
        {
            >= 3 => (versionTokens[1], versionTokens[2]),
            >= 2 => (versionTokens[1], null),
            _ => (null, null)
        };
    }

    public static (string? InstalledVersion, string? AvailableVersion) ReadVersionsFromListLine(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var packageIdIndex = Array.FindIndex(tokens, token => token.Contains('.', StringComparison.Ordinal));

        if (packageIdIndex < 0 || packageIdIndex + 1 >= tokens.Length)
        {
            return (null, null);
        }

        var installedVersion = tokens[packageIdIndex + 1];
        var availableVersion = packageIdIndex + 2 < tokens.Length ? tokens[packageIdIndex + 2] : null;
        return (installedVersion, availableVersion);
    }

    public static WingetInstallerInfo? ReadInstallerInfo(string output)
    {
        var installerType = ReadField(output, "Installer Type");
        var installerUrl = ReadField(output, "Installer Url");
        var installerSha256 = ReadField(output, "Installer SHA256");

        return string.IsNullOrWhiteSpace(installerUrl)
            ? null
            : new WingetInstallerInfo(installerType ?? string.Empty, installerUrl, installerSha256 ?? string.Empty);
    }

    public static IReadOnlyList<AvailablePackageUpdate> ReadAvailableUpdates(string output)
    {
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var headerIndex = Array.FindIndex(lines, line =>
            line.TrimStart().StartsWith("Name", StringComparison.OrdinalIgnoreCase)
            && line.Contains(" Id ", StringComparison.Ordinal)
            && line.Contains("Available", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            return [];
        }

        var header = lines[headerIndex];
        var idIndex = header.IndexOf("Id", StringComparison.Ordinal);
        var versionIndex = header.IndexOf("Version", StringComparison.Ordinal);
        var availableIndex = header.IndexOf("Available", StringComparison.Ordinal);
        var sourceIndex = header.IndexOf("Source", StringComparison.Ordinal);

        if (idIndex < 0 || versionIndex <= idIndex || availableIndex <= versionIndex || sourceIndex <= availableIndex)
        {
            return [];
        }

        var updates = new List<AvailablePackageUpdate>();
        foreach (var rawLine in lines.Skip(headerIndex + 1))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)
                || line.TrimStart().StartsWith("-", StringComparison.Ordinal)
                || line.Contains("upgrades available", StringComparison.OrdinalIgnoreCase)
                || line.Contains("No installed package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = ReadColumn(line, 0, idIndex);
            var id = ReadColumn(line, idIndex, versionIndex);
            var installed = ReadColumn(line, versionIndex, availableIndex);
            var available = ReadColumn(line, availableIndex, sourceIndex);
            var source = sourceIndex < line.Length ? line[sourceIndex..].Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(installed)
                || string.IsNullOrWhiteSpace(available))
            {
                continue;
            }

            updates.Add(new AvailablePackageUpdate(name, id, installed, available, source));
        }

        return updates;
    }

    private static string ReadColumn(string line, int start, int end)
    {
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var length = Math.Max(0, Math.Min(end, line.Length) - start);
        return line.Substring(start, length).Trim();
    }
}

public sealed record WingetInstallerInfo(string InstallerType, string InstallerUrl, string InstallerSha256);
