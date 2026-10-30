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

            string normalizedMarkerFileName =
                $"axi-{managerType.ToLowerInvariant()}repository.json";

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
                            normalizedMarkerFileName,
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
    string normalizedMarkerFileName,
    string requestedManagerType,
    string defaultBranch)
        {
            // -------------------------------------------------------------
            // First get the contents of the repository root.
            //
            // We do this because GitHub file paths are case-sensitive.
            // Instead of directly requesting:
            //
            //     axi-productrepository.json
            //
            // we inspect the actual filenames returned by GitHub and
            // perform our own case-insensitive comparison.
            // -------------------------------------------------------------

            string encodedAccount =
                Uri.EscapeDataString(account);

            string encodedRepositoryName =
                Uri.EscapeDataString(repositoryName);

            string contentsUrl =
                $"{GitHubApiUrl}/repos/" +
                $"{encodedAccount}/" +
                $"{encodedRepositoryName}/" +
                $"contents";

            if (!string.IsNullOrWhiteSpace(defaultBranch))
            {
                contentsUrl +=
                    $"?ref={Uri.EscapeDataString(defaultBranch)}";
            }

            HttpResponseMessage contentsResponse =
                await client.GetAsync(contentsUrl);

            if (contentsResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            if (!contentsResponse.IsSuccessStatusCode)
            {
                string error =
                    await contentsResponse.Content.ReadAsStringAsync();

                throw new Exception(
                    $"Unable to read repository contents from " +
                    $"'{repositoryName}'. " +
                    $"Status: {(int)contentsResponse.StatusCode}. " +
                    $"Response: {error}");
            }

            List<GitHubFileResponse> files =
                await DeserializeResponse<List<GitHubFileResponse>>(
                    contentsResponse);

            if (files == null || files.Count == 0)
            {
                return false;
            }

            // -------------------------------------------------------------
            // Find the marker file using case-insensitive filename matching.
            //
            // For example, all of these will match:
            //
            // axi-productrepository.json
            // axi-Productrepository.json
            // AXI-PRODUCTREPOSITORY.JSON
            // -------------------------------------------------------------

            string expectedMarkerFileName =
                normalizedMarkerFileName;

            GitHubFileResponse markerFile =
                files.FirstOrDefault(
                    file =>
                        string.Equals(
                            file.Name,
                            expectedMarkerFileName,
                            StringComparison.OrdinalIgnoreCase));

            if (markerFile == null ||
                string.IsNullOrWhiteSpace(markerFile.Name))
            {
                return false;
            }

            // Make sure we found a file and not a directory.
            if (!string.Equals(
                    markerFile.Type,
                    "file",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _logger.LogInformation(
                "Found marker file '{MarkerFileName}' in repository '{RepositoryName}'.",
                markerFile.Name,
                repositoryName);

            // -------------------------------------------------------------
            // Now request the actual filename returned by GitHub.
            //
            // This is important.
            //
            // If GitHub returned:
            //
            //     axi-Productrepository.json
            //
            // we must request that exact path instead of changing it to:
            //
            //     axi-productrepository.json
            // -------------------------------------------------------------

            string encodedFileName =
                Uri.EscapeDataString(markerFile.Name);

            string fileUrl =
                $"{GitHubApiUrl}/repos/" +
                $"{encodedAccount}/" +
                $"{encodedRepositoryName}/" +
                $"contents/{encodedFileName}";

            if (!string.IsNullOrWhiteSpace(defaultBranch))
            {
                fileUrl +=
                    $"?ref={Uri.EscapeDataString(defaultBranch)}";
            }

            HttpResponseMessage fileResponse =
                await client.GetAsync(fileUrl);

            if (fileResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            if (!fileResponse.IsSuccessStatusCode)
            {
                string error =
                    await fileResponse.Content.ReadAsStringAsync();

                throw new Exception(
                    $"Unable to read marker file '{markerFile.Name}' " +
                    $"from repository '{repositoryName}'. " +
                    $"Status: {(int)fileResponse.StatusCode}. " +
                    $"Response: {error}");
            }

            GitHubFileResponse fileContent =
                await DeserializeResponse<GitHubFileResponse>(
                    fileResponse);

            if (fileContent == null ||
                string.IsNullOrWhiteSpace(fileContent.Content))
            {
                return false;
            }

            // -------------------------------------------------------------
            // Decode JSON content.
            // -------------------------------------------------------------

            string jsonContent =
                DecodeBase64Content(
                    fileContent.Content);

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return false;
            }

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
                    $"Invalid JSON in '{markerFile.Name}' " +
                    $"for repository '{repositoryName}'.",
                    ex);
            }

            if (marker == null ||
                string.IsNullOrWhiteSpace(marker.ManagerType))
            {
                return false;
            }

            // -------------------------------------------------------------
            // ManagerType comparison is case-insensitive.
            //
            // "Product" == "product"
            // -------------------------------------------------------------

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
            public string Name { get; set; }

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