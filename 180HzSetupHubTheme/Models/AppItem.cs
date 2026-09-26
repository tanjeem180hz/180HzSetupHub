namespace SetupHub180Hz.Models
{
    public class AppItem
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string AvailableVersion { get; set; } = "";
        public string Source { get; set; } = "";

        public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion);
    }
}
