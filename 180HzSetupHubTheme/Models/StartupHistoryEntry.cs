using System;

namespace SetupHub180Hz.Models
{
    public class StartupHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ItemName { get; set; } = "";
        public string Action { get; set; } = "Enabled"; // "Enabled" or "Disabled"
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Source { get; set; } = "";
        public string Command { get; set; } = "";
        public string Notes { get; set; } = "";

        public string FormattedTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string DateDisplay => Timestamp.ToString("MMM dd, yyyy");
        public string TimeDisplay => Timestamp.ToString("hh:mm:ss tt");

        public string TimeAgo
        {
            get
            {
                var diff = DateTime.Now - Timestamp;
                if (diff.TotalSeconds < 60) return "Just now";
                if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
                return Timestamp.ToString("MMM dd, yyyy");
            }
        }

        public bool IsEnabledAction => string.Equals(Action, "Enabled", StringComparison.OrdinalIgnoreCase);
        public string BadgeBackground => IsEnabledAction ? "#152E20" : "#2E1515";
        public string BadgeForeground => IsEnabledAction ? "#10B981" : "#EF4444";
        public string BadgeBorder => IsEnabledAction ? "#059669" : "#DC2626";
        public string ActionText => IsEnabledAction ? "● Enabled (Turned ON)" : "○ Disabled (Turned OFF)";
    }
}
