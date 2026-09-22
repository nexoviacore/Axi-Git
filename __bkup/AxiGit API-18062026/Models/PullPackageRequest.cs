namespace AxiGitAPI.Models
{
    public class PullPackageRequest
    {
        public string PhysicalPath { get; set; }

        public string RepoUrl { get; set; }

        public string PackageName { get; set; }

        public string GitBranch { get; set; }
    }
}