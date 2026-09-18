namespace AxiGitAPI.Models
{
    public class PushFolderRequest
    {
        public string PhysicalPath { get; set; }

        public string TargetRepo { get; set; }

        public string PackageName { get; set; }

        public string PackageDescription { get; set; }

        public string PackageVersion { get; set; }

        public string PackageAuthor { get; set; }

        public string GitBranch { get; set; }
    }
}