namespace AxiGitAPI.Models
{
    public class PackageDetails
    {
        public string PackageName { get; set; }

        public string Branch { get; set; }

        public string PackageDescription { get; set; }

        public string PackageVersion { get; set; }

        public string PackageAuthor { get; set; }

        public string PackageCreatedOn { get; set; }

        public string Icon { get; set; }

        public string ColorClass { get; set; }

        public List<string> Tags { get; set; }

        public string Category { get; set; }
    }
}