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

            string rootTreeSha = commit.Commit.Tree.Sha;

            GitHubTree tree =
                await GetTree(
                    client,
                    repoPath,
                    rootTreeSha,
                    recursive: true,
                    account,
                    repo,
                    cancellationToken);

            // GitHub caps recursive trees; for a large Repository it returns a partial
            // tree flagged "truncated". Directories only need two levels, so read those
            // level by level instead.
            List<GitHubTreeEntry> entries =
                tree.Truncated
                    ? await GetTopTwoLevels(client, repoPath, rootTreeSha, account, repo, cancellationToken)
                    : tree.Tree;

            List<DirectoryEntry> directories = BuildDirectories(entries);

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

        // Every visible top-level folder is a Directory, for every Manager Type.
        // Its Versions are listed when present, else [].
        private static List<DirectoryEntry> BuildDirectories(List<GitHubTreeEntry> entries)
        {
            List<string[]> folders =
                entries
                    .Where(e => e.Type == "tree")
                    .Select(e => e.Path.Split('/'))
                    .ToList();

            // Second-level folder names, keyed by their top-level folder.
            ILookup<string, string> childFolders =
                folders
                    .Where(segments => segments.Length == 2)
                    .ToLookup(segments => segments[0], segments => segments[1]);

            return folders
                .Where(segments => segments.Length == 1)
                .Select(segments => segments[0])
                .Where(name => !name.StartsWith('.'))
                .Select(name => new DirectoryEntry
                {
                    Name = name,
                    Versions = SelectableVersions(childFolders[name])
                })
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // "Latest" first (when present), then Version{N} in numeric order,
        // each with its stored spelling: GitHub paths are case-sensitive.
        private static List<string> SelectableVersions(IEnumerable<string> childFolderNames)
        {
            List<string> names = childFolderNames.ToList();

            string? latest =
                names.FirstOrDefault(name =>
                    string.Equals(name, LatestFolderName, StringComparison.OrdinalIgnoreCase));

            List<string> versions =
                names
                    .Select(name => VersionFolderPattern.Match(name))
                    .Where(match => match.Success)
                    .OrderBy(match => int.Parse(match.Groups[1].Value))
                    .Select(match => match.Value)
                    .ToList();

            if (latest != null)
                versions.Insert(0, latest);

            return versions;
        }

        // Lists every top-level folder and its immediate children, with paths
        // shaped like a recursive tree's ("Folder/Child").
        private async Task<List<GitHubTreeEntry>> GetTopTwoLevels(
            HttpClient client,
            string repoPath,
            string rootTreeSha,
            string account,
            string repo,
            CancellationToken cancellationToken)
        {
            _logger.LogWarning(
                "Tree for Repository '{Account}/{Repository}' was truncated by GitHub; " +
                "reading top-level folders one by one.",
                account,
                repo);

            GitHubTree root =
                await GetCompleteTree(client, repoPath, rootTreeSha, account, repo, cancellationToken);

            List<GitHubTreeEntry> entries = new List<GitHubTreeEntry>();

            foreach (GitHubTreeEntry folder in root.Tree.Where(e => e.Type == "tree"))
            {
                entries.Add(folder);

                GitHubTree children =
                    await GetCompleteTree(client, repoPath, folder.Sha, account, repo, cancellationToken);

                entries.AddRange(
                    children.Tree.Select(child => new GitHubTreeEntry
                    {
                        Path = $"{folder.Path}/{child.Path}",
                        Type = child.Type,
                        Sha = child.Sha
                    }));
            }

            return entries;
        }

        // A non-recursive tree that GitHub still truncates cannot be listed completely;
        // fail (502) rather than return a Directory list with Versions silently missing.
        private async Task<GitHubTree> GetCompleteTree(
            HttpClient client,
            string repoPath,
            string treeSha,
            string account,
            string repo,
            CancellationToken cancellationToken)
        {
            GitHubTree tree =
                await GetTree(client, repoPath, treeSha, recursive: false, account, repo, cancellationToken);

            if (tree.Truncated)
            {
                _logger.LogWarning(
                    "Tree {TreeSha} in Repository '{Account}/{Repository}' is too large for GitHub to list.",
                    treeSha,
                    account,
                    repo);

                throw GitHubApiException.IncompleteListing();
            }

            return tree;
        }

        private async Task<GitHubTree> GetTree(
            HttpClient client,
            string repoPath,
            string treeSha,
            bool recursive,
            string account,
            string repo,
            CancellationToken cancellationToken)
        {
            string url =
                $"{repoPath}/git/trees/{Uri.EscapeDataString(treeSha)}" +
                (recursive ? "?recursive=1" : string.Empty);

            using HttpResponseMessage response =
                await GetFromGitHub(client, url, cancellationToken);

            return await ReadJsonOrThrow<GitHubTree>(
                response,
                account,
                repo,
                cancellationToken);
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

            public string Sha { get; set; } = string.Empty;
        }
    }
}
