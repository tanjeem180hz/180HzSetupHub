namespace WinSetupHub.App.Services.Progress;

public static class InstallProgressFormatter
{
    public static string Build(
        double progressPercent,
        bool isIndeterminate,
        DateTime startedAt,
        string speedText,
        bool includeEta,
        string? transferText = null)
    {
        var elapsed = DateTime.Now - startedAt;
        var parts = new List<string>
        {
            isIndeterminate ? "Preparing" : $"{progressPercent:0}% complete"
        };

        if (!string.IsNullOrWhiteSpace(transferText))
        {
            parts.Add(transferText);
        }

        if (!string.IsNullOrWhiteSpace(speedText))
        {
            parts.Add(speedText);
        }

        parts.Add($"elapsed {FormatDuration(elapsed)}");

        if (includeEta && !isIndeterminate && progressPercent > 1 && progressPercent < 100)
        {
            var totalSeconds = elapsed.TotalSeconds / (progressPercent / 100);
            var remaining = TimeSpan.FromSeconds(Math.Max(0, totalSeconds - elapsed.TotalSeconds));
            parts.Add($"about {FormatDuration(remaining)} left");
        }

        return string.Join(" - ", parts);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return duration.TotalMinutes >= 1
            ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{Math.Max(0, duration.Seconds)}s";
    }
}
