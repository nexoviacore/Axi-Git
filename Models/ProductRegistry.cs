//namespace AxiGitAPI.Models
//{
//    public class PackageRegistry
//    {
//        public List<PackageDetails> Packages { get; set; }
//            = new List<PackageDetails>();
//        public List<string> GitBranches { get; set; } = new();
//    }

//}

namespace AxiGitAPI.Models
{
    public class ProductRegistry
    {
        public string ManagerType { get; set; }

        public List<ProductDetails> Products { get; set; }
            = new List<ProductDetails>();
    }
}