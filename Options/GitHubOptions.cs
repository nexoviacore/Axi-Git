namespace AxiGitAPI.Options
{
    // Bound from the "GitHub" configuration section.
    // The real Token lives in the git-ignored appsettings.Local.json
    // (see docs/adr/0001-pat-from-server-config.md).
    public class GitHubOptions
    {
        public const string SectionName = "GitHub";

        public string Username { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public string RepoOwner { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public string RepoUrl { get; set; } = string.Empty;
    }
}
