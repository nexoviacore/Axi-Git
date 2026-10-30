namespace AxiGitAPI.Models
{
    public class PushFolderRequest
    //{
    //    public string PhysicalPath { get; set; }

    //    public string TargetRepo { get; set; }

    //    public string PackageName { get; set; }

    //    public string PackageDescription { get; set; }

    //    public string PackageVersion { get; set; }

    //    public string PackageAuthor { get; set; }

    //    public string GitBranch { get; set; }
    //}
    {    
        public string PhysicalPath { get; set; }

        public string TargetRepo { get; set; }

        public string ProductName { get; set; }

        public string ProductDescription { get; set; }

        public string ProductVersion { get; set; }

        public string ProductAuthor { get; set; }

        public string GitBranch { get; set; }
        
        public string ManagerType { get; set; }

        public bool EnsureRepository { get; set; }

        public bool Compress { get; set; }
    }
}