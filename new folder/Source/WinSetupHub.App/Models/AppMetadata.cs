namespace SetupHub180Hz.Models
{
    public record AppMetadata(string? Homepage, string? PublisherUrl, string? SupportUrl)
    {
        public string? BestLink => Homepage ?? PublisherUrl ?? SupportUrl;
    }
}
