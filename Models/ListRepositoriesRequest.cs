using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Models
{
    public class ListRepositoriesRequest
    {
        public string Account { get; set; }
        public string Token { get; set; }
        public string ManagerType { get; set; }
    }
}
