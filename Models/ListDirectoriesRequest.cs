namespace AxiGitAPI.Models
{
    public class ListDirectoriesRequest
    {
        public string Account { get; set; } = string.Empty;

        public string Repo { get; set; } = string.Empty;

        public string ManagerType { get; set; } = string.Empty;
    }
}
