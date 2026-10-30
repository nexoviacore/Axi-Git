using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxiGitAPI.Models;

namespace AxiGitAPI.Services
{
    // Lists the Directories (and their Versions) on a Repository's main branch.
    // Authenticates with the PAT from server configuration via the named
    // GitHub HttpClient (see docs/adr/0001-pat-from-server-config.md).
    public class DirectoryListService
    {
        public const string GitHubClientName = "GitHub";

        private const string Branch = "main";

        private const string LatestFolderName = "Latest";

        private static readonly Regex VersionFolderPattern =
            new Regex(
                @"^Version(\d{1,9})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly IHttpClientFactory _httpClientFactory;

        private readonly ILogger<DirectoryListService> _logger;

        public DirectoryListService(
            IHttpClientFactory httpClientFactory,
            ILogger<DirectoryListService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ListDirectoriesResponse> ListDirectories(
            ListDirectoriesRequest request,
            CancellationToken cancellationToken)
        {
            string account = request.Account.Trim();
            string repo = request.Repo.Trim();

            string repoPath =
                $"repos/{Uri.EscapeDataString(account)}/{Uri.EscapeDataString(repo)}";

            HttpClient client =
                _httpClientFactory.CreateClient(GitHubClientName);

            using HttpResponseMessage commitResponse =
                await GetFromGitHub(
                    client,
                    $"{repoPath}/commits/{Branch}",
                    cancellationToken);

            // GitHub answers 409 Conflict when the Repository has no commits yet.
            // An empty Repository is valid: it simply has no Directories.
            if (commitResponse.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogInformation(
                    "Repository '{Account}/{Repository}' is empty.",
                    account,
                    repo);

                return CreateResponse(request, account, repo, null, new List<DirectoryEntry>());
            }

            // Only the commit lookup can tell us the Repository or main is missing:
            // 404 = unknown Repository or not visible to the PAT,
            // 422 = no main branch ("No commit found for SHA: main").
            if (commitResponse.StatusCode == HttpStatusCode.NotFound ||
                commitResponse.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                await LogGitHubFailure(commitResponse, account, repo, cancellationToken);

                throw new RepositoryNotFoundException(account, repo);
            }

            GitHubCommit commit =
                await ReadJsonOrThrow<GitHubCommit>(
                    commitResponse,
                    account,
                    repo,
                    cancellationToken);

            using HttpResponseMessage treeResponse =
                await GetFromGitHub(
                    client,
                    $"{repoPath}/git/trees/{commit.Commit.Tree.Sha}?recursive=1",
                    cancellationToken);

            GitHubTree tree =
                await ReadJsonOrThrow<GitHubTree>(
                    treeResponse,
                    account,
                    repo,
                    cancellationToken);

            List<DirectoryEntry> directories =
                BuildDirectories(tree.Tree);

            _logger.LogInformation(
                "Found {DirectoryCount} directories in Repository '{Account}/{Repository}' at {CommitSha}.",
                directories.Count,
                account,
                repo,
                commit.Sha);

            return CreateResponse(request, account, repo, commit.Sha, directories);
        }

        private static ListDirectoriesResponse CreateResponse(
            ListDirectoriesRequest request,
            string account,
            string repo,
            string? commitSha,
            List<DirectoryEntry> directories)
        {
            return new ListDirectoriesResponse
            {
                Account = account,
                Repo = repo,
                ManagerType = request.ManagerType.Trim(),
                Branch = Branch,
                CommitSha = commitSha,
                RetrievedAt = DateTimeOffset.UtcNow,
                DirectoryCount = directories.Count,
                Directories = directories
            };
        }

        private static List<DirectoryEntry> BuildDirectories(
            List<GitHubTreeEntry> entries)
        {
            // Second-level folders, grouped by their top-level Directory.
            return entries
                .Where(e => e.Type == "tree")
                .Select(e => e.Path.Split('/'))
                .Where(segments => segments.Length == 2)
                .GroupBy(segments => segments[0], segments => segments[1])
                .Select(group =>
                {
                    // Latest keeps its stored spelling too: GitHub paths are case-sensitive.
                    string? latest =
                        group.FirstOrDefault(name =>
                            string.Equals(name, LatestFolderName, StringComparison.OrdinalIgnoreCase));

                    // Version folders keep their stored spelling; order by N, not text.
                    List<string> versions =
                        group
                            .Select(name => VersionFolderPattern.Match(name))
                            .Where(match => match.Success)
                            .OrderBy(match => int.Parse(match.Groups[1].Value))
                            .Select(match => match.Value)
                            .ToList();

                    if (latest != null)
                        versions.Insert(0, latest);

                    return new DirectoryEntry
                    {
                        Name = group.Key,
                        Versions = versions
                    };
                })
                // A top-level folder is a Directory only if it holds Latest or a Version.
                .Where(directory => directory.Versions.Count > 0)
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // A GitHub that cannot be reached (connection failure, timeout) is a GitHubApiException (502).
        // A cancellation requested by our own caller is left to propagate.
        private async Task<HttpResponseMessage> GetFromGitHub(
            HttpClient client,
            string url,
            CancellationToken cancellationToken)
        {
            try
            {
                return await client.GetAsync(url, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "GitHub request '{Url}' could not be sent.", url);

                throw new GitHubApiException();
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "GitHub request '{Url}' timed out.", url);

                throw new GitHubApiException();
            }
        }

        // Any non-success status here is a GitHubApiException (502).
        // GitHub's body is logged but never put into the exception message.
        private async Task<T> ReadJsonOrThrow<T>(
            HttpResponseMessage response,
            string account,
            string repo,
            CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
            {
                await LogGitHubFailure(response, account, repo, cancellationToken);

                throw new GitHubApiException((int)response.StatusCode);
            }

            string json =
                await response.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new JsonException(
                    $"GitHub returned no content for '{response.RequestMessage?.RequestUri}'.");
        }

        private async Task LogGitHubFailure(
            HttpResponseMessage response,
            string account,
            string repo,
            CancellationToken cancellationToken)
        {
            string body =
                await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning(
                "GitHub request '{Url}' for Repository '{Account}/{Repository}' failed with {StatusCode}: {Response}",
                response.RequestMessage?.RequestUri,
                account,
                repo,
                (int)response.StatusCode,
                body);
        }

        // -------------------------------------------------------------
        // GitHub API response models
        // -------------------------------------------------------------

        private class GitHubCommit
        {
            public string Sha { get; set; } = string.Empty;

            public GitHubCommitDetail Commit { get; set; } = new();
        }

        private class GitHubCommitDetail
        {
            public GitHubSha Tree { get; set; } = new();
        }

        private class GitHubSha
        {
            public string Sha { get; set; } = string.Empty;
        }

        private class GitHubTree
        {
            public bool Truncated { get; set; }

            public List<GitHubTreeEntry> Tree { get; set; } = new();
        }

        private class GitHubTreeEntry
        {
            public string Path { get; set; } = string.Empty;

            public string Type { get; set; } = string.Empty;
        }
    }
}
