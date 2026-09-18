using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Models
{
    public class ListRepositoriesResponse
    {
        public int RepoCount { get; set; }

        public List<string> RepoNames { get; set; }
    }
}
