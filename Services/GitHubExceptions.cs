namespace AxiGitAPI.Services
{
    // Messages are returned to clients by GlobalExceptionHandler, so they must
    // never contain the PAT or GitHub's raw response body.

    // The Repository, or its main branch, does not exist or is not visible to the configured PAT.
    public class RepositoryNotFoundException : Exception
    {
        public RepositoryNotFoundException(string account, string repository)
            : base(
                $"Repository '{account}/{repository}' or its main branch was not found, " +
                "or the configured GitHub token cannot access it.")
        {
        }
    }

    // GitHub rejected the configured PAT, failed, or could not be reached.
    public class GitHubApiException : Exception
    {
        public GitHubApiException()
            : base("GitHub could not be reached.")
        {
        }

        public GitHubApiException(int gitHubStatusCode)
            : base($"GitHub returned an unexpected response (HTTP {gitHubStatusCode}).")
        {
        }

        private GitHubApiException(string clientSafeMessage)
            : base(clientSafeMessage)
        {
        }

        // GitHub truncated a listing even when read folder by folder.
        public static GitHubApiException IncompleteListing() =>
            new GitHubApiException("GitHub returned an incomplete listing for this Repository.");
    }
}
