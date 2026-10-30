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

            GitHubCommit commit =
                await GetJson<GitHubCommit>(
                    client,
                    $"{repoPath}/commits/{Branch}",
                    cancellationToken);

            GitHubTree tree =
                await GetJson<GitHubTree>(
                    client,
                    $"{repoPath}/git/trees/{commit.Commit.Tree.Sha}?recursive=1",
                    cancellationToken);

            List<DirectoryEntry> directories =
                BuildDirectories(tree.Tree);

            _logger.LogInformation(
                "Found {DirectoryCount} directories in Repository '{Account}/{Repository}' at {CommitSha}.",
                directories.Count,
                account,
                repo,
                commit.Sha);

            return new ListDirectoriesResponse
            {
                Account = account,
                Repo = repo,
                ManagerType = request.ManagerType.Trim(),
                Branch = Branch,
                CommitSha = commit.Sha,
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

        private static async Task<T> GetJson<T>(
            HttpClient client,
            string url,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response =
                await client.GetAsync(url, cancellationToken);

            string json =
                await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"GitHub request '{url}' failed. " +
                    $"Status: {(int)response.StatusCode}. Response: {json}");
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new Exception($"GitHub request '{url}' returned no content.");
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
