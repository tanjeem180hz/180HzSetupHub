using System.Globalization;

namespace WinSetupHub.App.Services;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString(value >= 10 || unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
