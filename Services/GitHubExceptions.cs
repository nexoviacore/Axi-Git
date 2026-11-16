namespace AxiGitAPI.Services
{
    // Messages are returned to clients by GlobalExceptionHandler, so they must
    // never contain the PAT or GitHub's raw response body.

    // The Repository, or its main branch, does not exist or is not visible to the PAT.
    public class RepositoryNotFoundException : Exception
    {
        public RepositoryNotFoundException(string account, string repository)
            : base(
                $"Repository '{account}/{repository}' or its main branch was not found, " +
                "or the GitHub token cannot access it.")
        {
        }
    }

    // GitHub answered 401: the PAT sent in the request is wrong, expired or revoked.
    public class GitHubPatRejectedException : Exception
    {
        public GitHubPatRejectedException()
            : base("GitHub did not accept the PAT. Check that it is correct and has not expired or been revoked.")
        {
        }
    }

    // GitHub failed or could not be reached.
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
