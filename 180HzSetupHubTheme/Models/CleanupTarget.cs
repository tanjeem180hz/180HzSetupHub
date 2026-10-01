using System.Collections.Generic;

namespace SetupHub180Hz.Models
{
    public class CleanupTarget
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public List<string>? AdditionalPaths { get; set; }
        public long SizeBytes { get; set; }
        public bool IsSelected { get; set; } = true;

        public string SizeDisplay
        {
            get
            {
                double mb = SizeBytes / 1024.0 / 1024.0;
                return mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
            }
        }
    }
}
