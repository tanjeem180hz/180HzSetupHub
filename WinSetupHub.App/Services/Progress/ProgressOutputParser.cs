using System.Globalization;
using System.Text.RegularExpressions;

namespace WinSetupHub.App.Services.Progress;

public static class ProgressOutputParser
{
    private static readonly Regex AnsiRegex = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

    private static readonly Regex PercentRegex = new(
        @"(?<percent>\d{1,3}(?:[\.,]\d+)?)\s*%",
        RegexOptions.Compiled);

    private static readonly Regex TransferRegex = new(
        @"(?<current>\d+(?:[\.,]\d+)?)\s*(?<currentUnit>B|KB|KiB|MB|MiB|GB|GiB|bytes?)\s*/\s*(?<total>\d+(?:[\.,]\d+)?)\s*(?<totalUnit>B|KB|KiB|MB|MiB|GB|GiB|bytes?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryReadPercent(string output, out double percent)
    {
        percent = 0;
        output = Normalize(output);
        var match = PercentRegex.Match(output);
        if (!match.Success)
        {
            return false;
        }

        return TryReadNumber(match.Groups["percent"].Value, out percent);
    }

    public static bool TryReadTransferPercent(string output, out double percent)
    {
        if (TryReadTransfer(output, out var progress))
        {
            percent = progress.Percent;
            return true;
        }

        percent = 0;
        return false;
    }

    public static bool TryReadTransfer(string output, out TransferProgress progress)
    {
        progress = default;
        output = Normalize(output);
        var match = TransferRegex.Match(output);
        if (!match.Success)
        {
            return false;
        }

        if (!TryReadNumber(match.Groups["current"].Value, out var current)
            || !TryReadNumber(match.Groups["total"].Value, out var total))
        {
            return false;
        }

        var currentBytes = ToBytes(current, match.Groups["currentUnit"].Value);
        var totalBytes = ToBytes(total, match.Groups["totalUnit"].Value);
        if (totalBytes <= 0)
        {
            return false;
        }

        var percent = Math.Clamp(currentBytes / totalBytes * 100, 0, 100);
        progress = new TransferProgress(
            (long)Math.Round(currentBytes),
            (long)Math.Round(totalBytes),
            percent);
        return true;
    }

    public static string Normalize(string output)
    {
        return AnsiRegex
            .Replace(output, string.Empty)
            .Replace('\b', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static bool TryReadNumber(string value, out double number)
    {
        return double.TryParse(
            value.Replace(",", ".", StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static double ToBytes(double value, string unit)
    {
        return unit.ToUpperInvariant() switch
        {
            "GB" or "GIB" => value * 1024 * 1024 * 1024,
            "MB" or "MIB" => value * 1024 * 1024,
            "KB" or "KIB" => value * 1024,
            _ => value
        };
    }
}

public readonly record struct TransferProgress(long CurrentBytes, long TotalBytes, double Percent);
