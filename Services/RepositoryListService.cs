using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AxiGitAPI.Models;

namespace AxiGitAPI.Services
{
    public class RepositoryListService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RepositoryListService> _logger;

        private const string GitHubApiUrl = "https://api.github.com";
        private const string GitHubApiVersion = "2022-11-28";

        public RepositoryListService(
            IHttpClientFactory httpClientFactory,
            ILogger<RepositoryListService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ListRepositoriesResponse> ListRepositories(
            ListRepositoriesRequest request)
        {
            if (request == null)
                throw new Exception("Request is required.");

            if (string.IsNullOrWhiteSpace(request.Account))
                throw new Exception("Account is required.");

            if (string.IsNullOrWhiteSpace(request.Token))
                throw new Exception("Token is required.");

            if (string.IsNullOrWhiteSpace(request.ManagerType))
                throw new Exception("ManagerType is required.");

            string account = request.Account.Trim();
            string token = request.Token.Trim();
            string managerType = request.ManagerType.Trim();

            string markerFileName =
                $"axi-{managerType}repository.json";

            _logger.LogInformation(
                "Starting repository search for account '{Account}' and manager type '{ManagerType}'.",
                account,
                managerType);

            HttpClient client = CreateGitHubClient(token);

            // ---------------------------------------------------------
            // Step 1:
            // Verify that the PAT belongs to the requested account.
            // ---------------------------------------------------------

            string authenticatedUserUrl = $"{GitHubApiUrl}/user";

            HttpResponseMessage userResponse =
                await client.GetAsync(authenticatedUserUrl);

            if (!userResponse.IsSuccessStatusCode)
            {
                string error =
                    await userResponse.Content.ReadAsStringAsync();

                throw new Exception(
                    $"GitHub authentication failed. Status: {(int)userResponse.StatusCode}. " +
                    $"Response: {error}");
            }

            GitHubUserResponse authenticatedUser =
                await DeserializeResponse<GitHubUserResponse>(
                    userResponse);

            if (authenticatedUser == null ||
                string.IsNullOrWhiteSpace(authenticatedUser.Login))
            {
                throw new Exception(
                    "Unable to determine the authenticated GitHub account.");
            }

            // Account comparison is case-insensitive.
            if (!string.Equals(
                    authenticatedUser.Login,
                    account,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    $"The provided PAT belongs to GitHub account '{authenticatedUser.Login}', " +
                    $"but the requested account is '{account}'.");
            }

            _logger.LogInformation(
                "GitHub authentication successful for account '{Account}'.",
                authenticatedUser.Login);

            // ---------------------------------------------------------
            // Step 2:
            // Get all repositories available to this authenticated user.
            // ---------------------------------------------------------

            List<GitHubRepository> repositories =
                await GetAllRepositories(client);

            _logger.LogInformation(
                "Found {RepositoryCount} repositories for account '{Account}'.",
                repositories.Count,
                account);

            // ---------------------------------------------------------
            // Step 3:
            // Check every repository for the marker file.
            // ---------------------------------------------------------

            List<string> listrepo = new List<string>();

            foreach (GitHubRepository repository in repositories)
            {
                if (string.IsNullOrWhiteSpace(repository.Name))
                    continue;

                string repositoryName = repository.Name;

                _logger.LogInformation(
                    "Checking repository '{RepositoryName}'.",
                    repositoryName);

                try
                {
                    bool isMatchingRepository =
                        await IsMatchingRepository(
                            client,
                            account,
                            repositoryName,
                            markerFileName,
                            managerType,
                            repository.DefaultBranch);

                    if (isMatchingRepository)
                    {
                        listrepo.Add(repositoryName);

                        _logger.LogInformation(
                            "Repository '{RepositoryName}' matched manager type '{ManagerType}'.",
                            repositoryName,
                            managerType);
                    }
                }
                catch (Exception ex)
                {
                    // A problem with one repository should not stop the
                    // complete repository search.
                    _logger.LogWarning(
                        ex,
                        "Unable to process repository '{RepositoryName}'. Skipping repository.",
                        repositoryName);
                }
            }

            // ---------------------------------------------------------
            // Step 4:
            // Return final result.
            // ---------------------------------------------------------

            return new ListRepositoriesResponse
            {
                RepoCount = listrepo.Count,
                RepoNames = listrepo
            };
        }

        private HttpClient CreateGitHubClient(string token)
        {
            HttpClient client =
                _httpClientFactory.CreateClient();

            client.BaseAddress =
                new Uri(GitHubApiUrl);

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/vnd.github+json"));

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            client.DefaultRequestHeaders.UserAgent.Clear();

            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(
                    "AxiGitAPI",
                    "1.0"));

            client.DefaultRequestHeaders.Remove(
                "X-GitHub-Api-Version");

            client.DefaultRequestHeaders.Add(
                "X-GitHub-Api-Version",
                GitHubApiVersion);

            return client;
        }

        private async Task<List<GitHubRepository>> GetAllRepositories(
            HttpClient client)
        {
            List<GitHubRepository> repositories =
                new List<GitHubRepository>();

            int page = 1;

            while (true)
            {
                string url =
                    $"{GitHubApiUrl}/user/repos" +
                    $"?per_page=100" +
                    $"&page={page}";

                HttpResponseMessage response =
                    await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    string error =
                        await response.Content.ReadAsStringAsync();

                    throw new Exception(
                        $"Unable to retrieve repositories. " +
                        $"Status: {(int)response.StatusCode}. " +
                        $"Response: {error}");
                }

                List<GitHubRepository> pageRepositories =
                    await DeserializeResponse<List<GitHubRepository>>(
                        response);

                if (pageRepositories == null ||
                    pageRepositories.Count == 0)
                {
                    break;
                }

                repositories.AddRange(pageRepositories);

                // GitHub allows a maximum of 100 repositories per page.
                // If fewer than 100 were returned, this was the final page.
                if (pageRepositories.Count < 100)
                    break;

                page++;
            }

            return repositories;
        }

        private async Task<bool> IsMatchingRepository(
            HttpClient client,
            string account,
            string repositoryName,
            string markerFileName,
            string requestedManagerType,
            string defaultBranch)
        {
            string encodedAccount =
                Uri.EscapeDataString(account);

            string encodedRepositoryName =
                Uri.EscapeDataString(repositoryName);

            string encodedFileName =
                Uri.EscapeDataString(markerFileName);

            string url =
                $"{GitHubApiUrl}/repos/" +
                $"{encodedAccount}/" +
                $"{encodedRepositoryName}/" +
                $"contents/{encodedFileName}";

            // We intentionally do not pass ref here.
            // GitHub will use the repository's default branch.
            if (!string.IsNullOrWhiteSpace(defaultBranch))
            {
                url +=
                    $"?ref={Uri.EscapeDataString(defaultBranch)}";
            }

            HttpResponseMessage response =
                await client.GetAsync(url);

            // Marker file does not exist in this repository.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                string error =
                    await response.Content.ReadAsStringAsync();

                throw new Exception(
                    $"Unable to read marker file from repository " +
                    $"'{repositoryName}'. " +
                    $"Status: {(int)response.StatusCode}. " +
                    $"Response: {error}");
            }

            GitHubFileResponse fileResponse =
                await DeserializeResponse<GitHubFileResponse>(
                    response);

            if (fileResponse == null)
                return false;

            if (!string.Equals(
                    fileResponse.Type,
                    "file",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileResponse.Content))
                return false;

            // GitHub Contents API returns file content as Base64.
            string jsonContent =
                DecodeBase64Content(fileResponse.Content);

            if (string.IsNullOrWhiteSpace(jsonContent))
                return false;

            RepositoryMarker marker;

            try
            {
                marker =
                    JsonSerializer.Deserialize<RepositoryMarker>(
                        jsonContent,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
            }
            catch (JsonException ex)
            {
                throw new Exception(
                    $"Invalid JSON in '{markerFileName}' " +
                    $"for repository '{repositoryName}'.",
                    ex);
            }

            if (marker == null ||
                string.IsNullOrWhiteSpace(marker.ManagerType))
            {
                return false;
            }

            // Manager type comparison is NOT case-sensitive.
            return string.Equals(
                marker.ManagerType.Trim(),
                requestedManagerType.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private string DecodeBase64Content(string content)
        {
            string cleanedContent =
                content.Replace("\r", "")
                       .Replace("\n", "")
                       .Trim();

            byte[] bytes =
                Convert.FromBase64String(cleanedContent);

            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private async Task<T> DeserializeResponse<T>(
            HttpResponseMessage response)
        {
            string json =
                await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonSerializer.Deserialize<T>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        // -------------------------------------------------------------
        // GitHub API response models
        // -------------------------------------------------------------

        private class GitHubUserResponse
        {
            public string Login { get; set; }
        }

        private class GitHubRepository
        {
            public string Name { get; set; }

            public string DefaultBranch { get; set; }
        }

        private class GitHubFileResponse
        {
            public string Type { get; set; }

            public string Encoding { get; set; }

            public string Content { get; set; }
        }

        private class RepositoryMarker
        {
            public string ManagerType { get; set; }
        }
    }
}