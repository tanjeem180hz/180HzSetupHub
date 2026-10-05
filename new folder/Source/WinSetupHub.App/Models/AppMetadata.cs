namespace SetupHub180Hz.Models
{
    public record AppMetadata(string? Homepage, string? PublisherUrl, string? SupportUrl, string? Version = null)
    {
        public string? BestLink => Homepage ?? PublisherUrl ?? SupportUrl;
    }
}
