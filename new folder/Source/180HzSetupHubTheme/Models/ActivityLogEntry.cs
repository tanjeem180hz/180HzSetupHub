using System;

namespace SetupHub180Hz.Models
{
    public enum ActivityType { Info, Success, Warning, Error }

    public class ActivityLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = "";
        public ActivityType Type { get; set; } = ActivityType.Info;

        public string TimestampDisplay => Timestamp.ToString("dd MMM, HH:mm:ss");
    }
}
