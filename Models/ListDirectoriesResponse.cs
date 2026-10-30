namespace AxiGitAPI.Models
{
    public class ListDirectoriesResponse
    {
        public string Account { get; set; } = string.Empty;

        public string Repo { get; set; } = string.Empty;

        public string ManagerType { get; set; } = string.Empty;

        public string Branch { get; set; } = string.Empty;

        public string CommitSha { get; set; } = string.Empty;

        public DateTimeOffset RetrievedAt { get; set; }

        public int DirectoryCount { get; set; }

        public List<DirectoryEntry> Directories { get; set; } = new();
    }

    // One Directory in a Repository and its selectable Versions
    // ("Latest" first when present, then Version{N} in numeric order).
    public class DirectoryEntry
    {
        public string Name { get; set; } = string.Empty;

        public List<string> Versions { get; set; } = new();
    }
}
