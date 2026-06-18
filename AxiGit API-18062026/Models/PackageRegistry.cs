namespace AxiGitAPI.Models
{
    public class PackageRegistry
    {
        public List<PackageDetails> Packages { get; set; }
            = new List<PackageDetails>();
        public List<string> GitBranches { get; set; } = new();
    }
}