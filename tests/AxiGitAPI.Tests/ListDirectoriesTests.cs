using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AxiGitAPI.Tests
{
    public class ListDirectoriesTests : IDisposable
    {
        private readonly AxiGitApiFactory _factory = new();

        public void Dispose() => _factory.Dispose();

        private async Task<(HttpStatusCode Status, JsonElement Body)> ListDirectories(
            string account = "acme",
            string repo = "axi-products",
            string managerType = "Product")
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/git/listdirectories",
                new { account, repo, managerType });

            string json = await response.Content.ReadAsStringAsync();

            return (response.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
        }

        private async Task<(HttpStatusCode Status, JsonElement Body, string? ContentType)> Post(
            string url,
            object payload)
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.PostAsJsonAsync(url, payload);

            string json = await response.Content.ReadAsStringAsync();

            return (
                response.StatusCode,
                JsonDocument.Parse(json).RootElement.Clone(),
                response.Content.Headers.ContentType?.MediaType);
        }

        private static bool HasErrorFor(JsonElement problem, string field) =>
            problem.TryGetProperty("errors", out JsonElement errors) &&
            errors.EnumerateObject().Any(e =>
                string.Equals(e.Name, field, StringComparison.OrdinalIgnoreCase));

        private static string[] Names(JsonElement body) =>
            body.GetProperty("directories")
                .EnumerateArray()
                .Select(d => d.GetProperty("name").GetString()!)
                .ToArray();

        private static string[] VersionsOf(JsonElement body, string directory) =>
            body.GetProperty("directories")
                .EnumerateArray()
                .Single(d => d.GetProperty("name").GetString() == directory)
                .GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetString()!)
                .ToArray();

        [Fact]
        public async Task Returns_the_Directories_and_Versions_on_main_with_the_full_response_contract()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "Customer/",
                "Customer/Latest/",
                "Customer/Latest/version.info",
                "Customer/Version1/",
                "Customer/Version1/app.dll",
                "Customer/Version2/",
                "Customer/Version2/app.dll");

            DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

            var (status, body) = await ListDirectories();

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("acme", body.GetProperty("account").GetString());
            Assert.Equal("axi-products", body.GetProperty("repo").GetString());
            Assert.Equal("Product", body.GetProperty("managerType").GetString());
            Assert.Equal("main", body.GetProperty("branch").GetString());
            Assert.Equal("c0ffee1", body.GetProperty("commitSha").GetString());
            Assert.True(body.GetProperty("retrievedAt").GetDateTimeOffset() >= before);
            Assert.Equal(TimeSpan.Zero, body.GetProperty("retrievedAt").GetDateTimeOffset().Offset);
            Assert.Equal(1, body.GetProperty("directoryCount").GetInt32());
            Assert.Equal(new[] { "Customer" }, Names(body));
            Assert.Equal(new[] { "Latest", "Version1", "Version2" }, VersionsOf(body, "Customer"));
        }

        [Fact]
        public async Task Ignores_top_level_files_and_hidden_folders()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                ".github/",
                ".github/workflows/",
                ".github/workflows/build.yml",
                "docs/",
                "docs/images/",
                "readme.md",
                "axi-productrepository.json",
                "Customer/",
                "Customer/Version1/",
                "Customer/notes/");

            var (status, body) = await ListDirectories();

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(new[] { "Customer", "docs" }, Names(body));
            Assert.Equal(new[] { "Version1" }, VersionsOf(body, "Customer"));
            Assert.Empty(VersionsOf(body, "docs"));
            Assert.Equal(2, body.GetProperty("directoryCount").GetInt32());
        }

        [Fact]
        public async Task Includes_Directories_that_hold_only_Latest_or_only_Versions()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "Billing/",
                "Billing/Latest/",
                "Customer/",
                "Customer/Version3/");

            var (_, body) = await ListDirectories();

            Assert.Equal(new[] { "Billing", "Customer" }, Names(body));
            Assert.Equal(new[] { "Latest" }, VersionsOf(body, "Billing"));
            Assert.Equal(new[] { "Version3" }, VersionsOf(body, "Customer"));
        }

        [Fact]
        public async Task Lists_Latest_first_then_Versions_in_numeric_order_matched_case_insensitively_keeping_stored_spelling()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "Customer/",
                "Customer/version10/",
                "Customer/Version2/",
                "Customer/latest/",
                "Customer/VERSION1/",
                "Customer/Version9/");

            var (_, body) = await ListDirectories();

            Assert.Equal(
                new[] { "latest", "VERSION1", "Version2", "Version9", "version10" },
                VersionsOf(body, "Customer"));
        }

        [Fact]
        public async Task Lists_Directories_in_case_insensitive_name_order()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "orders/",
                "orders/Version1/",
                "Billing/",
                "Billing/Version1/",
                "customer/",
                "customer/Version1/");

            var (_, body) = await ListDirectories();

            Assert.Equal(new[] { "Billing", "customer", "orders" }, Names(body));
        }

        [Fact]
        public async Task Lists_a_Version_published_as_a_zip_archive()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "Customer/",
                "Customer/Latest/",
                "Customer/Latest/Customer_Latest.zip",
                "Customer/Latest/version.info",
                "Customer/Version1/",
                "Customer/Version1/Customer_version1.zip");

            var (_, body) = await ListDirectories();

            Assert.Equal(new[] { "Latest", "Version1" }, VersionsOf(body, "Customer"));
        }

        [Theory]
        [InlineData("Product")]
        [InlineData("ChangeRequest")]
        [InlineData("Plugin")]
        public async Task Returns_Versions_and_echoes_the_Manager_Type_for_every_Manager_Type(string managerType)
        {
            _factory.GitHub.Repository(
                "acme", "axi-repo", "c0ffee1",
                "Thing/",
                "Thing/Latest/",
                "Thing/Version1/");

            var (status, body) = await ListDirectories(repo: "axi-repo", managerType: managerType);

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(managerType, body.GetProperty("managerType").GetString());
            Assert.Equal(new[] { "Latest", "Version1" }, VersionsOf(body, "Thing"));
        }

        [Theory]
        [InlineData("account", null)]
        [InlineData("account", "")]
        [InlineData("account", "   ")]
        [InlineData("repo", null)]
        [InlineData("repo", "")]
        [InlineData("repo", "   ")]
        [InlineData("managerType", null)]
        [InlineData("managerType", "")]
        [InlineData("managerType", "   ")]
        public async Task Rejects_a_missing_or_blank_field_with_400_ProblemDetails_naming_it(
            string field,
            string? value)
        {
            var payload = new Dictionary<string, string?>
            {
                ["account"] = "acme",
                ["repo"] = "axi-products",
                ["managerType"] = "Product",
                [field] = value
            };

            var (status, problem, contentType) =
                await Post("/api/git/listdirectories", payload);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(400, problem.GetProperty("status").GetInt32());
            Assert.True(HasErrorFor(problem, field), $"Expected an error naming '{field}': {problem}");
            Assert.Empty(_factory.GitHub.Requests);
        }

        [Fact]
        public async Task Rejects_a_request_with_no_fields_at_all_with_400()
        {
            var (status, problem, _) =
                await Post("/api/git/listdirectories", new { });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.True(HasErrorFor(problem, "account"));
            Assert.True(HasErrorFor(problem, "repo"));
            Assert.True(HasErrorFor(problem, "managerType"));
        }

        [Fact]
        public async Task Rejects_an_unknown_Manager_Type_with_400_ProblemDetails()
        {
            var (status, problem, contentType) = await Post(
                "/api/git/listdirectories",
                new { account = "acme", repo = "axi-products", managerType = "Widget" });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.True(HasErrorFor(problem, "managerType"), $"Expected an error naming 'managerType': {problem}");
            Assert.Empty(_factory.GitHub.Requests);
        }

        [Theory]
        [InlineData("product")]
        [InlineData("PRODUCT")]
        [InlineData("changerequest")]
        [InlineData("PLUGIN")]
        public async Task Accepts_a_Manager_Type_in_any_letter_case(string managerType)
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "Customer/",
                "Customer/Version1/");

            var (status, _) = await ListDirectories(managerType: managerType);

            Assert.Equal(HttpStatusCode.OK, status);
        }

        [Fact]
        public async Task Returns_500_ProblemDetails_without_internal_details_on_an_unexpected_failure()
        {
            _factory.GitHub.RespondRaw(
                "/repos/acme/axi-products/commits/main",
                HttpStatusCode.OK,
                "{ this is not json SECRET-INTERNAL-MARKER");

            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/git/listdirectories",
                new { account = "acme", repo = "axi-products", managerType = "Product" });

            string raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            JsonElement problem = JsonDocument.Parse(raw).RootElement;
            Assert.Equal(500, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain("SECRET-INTERNAL-MARKER", raw);
            Assert.DoesNotContain("JsonException", raw);
            Assert.DoesNotContain("at AxiGitAPI", raw);
            Assert.DoesNotContain(AxiGitApiFactory.TestPat, raw);
        }

        private async Task<(HttpStatusCode Status, JsonElement Problem, string Raw, string? ContentType)> ListDirectoriesRaw()
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/git/listdirectories",
                new { account = "acme", repo = "axi-products", managerType = "Product" });

            string raw = await response.Content.ReadAsStringAsync();

            return (
                response.StatusCode,
                JsonDocument.Parse(raw).RootElement.Clone(),
                raw,
                response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task Returns_404_ProblemDetails_when_the_Repository_is_not_found()
        {
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/commits/main",
                HttpStatusCode.NotFound,
                new { message = "Not Found" });

            var (status, problem, raw, contentType) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(404, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain(AxiGitApiFactory.TestPat, raw);
        }

        [Fact]
        public async Task Returns_404_ProblemDetails_when_the_Repository_has_no_main_branch()
        {
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/commits/main",
                HttpStatusCode.UnprocessableEntity,
                new { message = "No commit found for SHA: main" });

            var (status, problem, raw, contentType) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(404, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain(AxiGitApiFactory.TestPat, raw);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public async Task Returns_502_ProblemDetails_when_GitHub_rejects_the_PAT_or_fails(HttpStatusCode gitHubStatus)
        {
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/commits/main",
                gitHubStatus,
                new { message = "GitHub said no SECRET-GITHUB-BODY" });

            var (status, problem, raw, contentType) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.BadGateway, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(502, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain(AxiGitApiFactory.TestPat, raw);
            Assert.DoesNotContain("SECRET-GITHUB-BODY", raw);
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.UnprocessableEntity)]
        public async Task Returns_502_when_reading_the_tree_fails_after_the_commit_was_found(HttpStatusCode treeStatus)
        {
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/commits/main",
                HttpStatusCode.OK,
                new { sha = "c0ffee1", commit = new { tree = new { sha = "tree-c0ffee1" } } });
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/git/trees/tree-c0ffee1?recursive=1",
                treeStatus,
                new { message = "tree failed SECRET-GITHUB-BODY" });

            var (status, problem, raw, contentType) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.BadGateway, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(502, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain(AxiGitApiFactory.TestPat, raw);
            Assert.DoesNotContain("SECRET-GITHUB-BODY", raw);
        }

        public static TheoryData<Exception> UnreachableGitHubFailures => new()
        {
            new HttpRequestException("No such host is known. SECRET-TRANSPORT-DETAIL"),
            new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout. SECRET-TRANSPORT-DETAIL",
                new TimeoutException())
        };

        [Theory]
        [MemberData(nameof(UnreachableGitHubFailures))]
        public async Task Returns_502_ProblemDetails_when_GitHub_cannot_be_reached(Exception failure)
        {
            _factory.GitHub.Throw("/repos/acme/axi-products/commits/main", failure);

            var (status, problem, raw, contentType) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.BadGateway, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(502, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain("SECRET-TRANSPORT-DETAIL", raw);
        }

        [Fact]
        public async Task Returns_200_with_no_Directories_for_an_empty_Repository()
        {
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/commits/main",
                HttpStatusCode.Conflict,
                new { message = "Git Repository is empty." });

            var (status, body, _, _) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("main", body.GetProperty("branch").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("commitSha").ValueKind);
            Assert.Equal(0, body.GetProperty("directoryCount").GetInt32());
            Assert.Empty(body.GetProperty("directories").EnumerateArray());
        }

        private static readonly string[] LargeRepositoryPaths =
        {
            "zeta/",
            "zeta/Version1/",
            ".github/",
            ".github/workflows/",
            "readme.md",
            "Customer/",
            "Customer/latest/",
            "Customer/latest/version.info",
            "Customer/Version10/",
            "Customer/Version2/",
            "Customer/Version2/Customer_version2.zip",
            "Customer/notes/",
            "billing/",
            "billing/Latest/",
            "docs/",
            "docs/images/"
        };

        [Fact]
        public async Task Returns_every_Directory_when_GitHub_truncates_the_recursive_tree()
        {
            _factory.GitHub.TruncatedRepository("acme", "axi-products", "c0ffee1", LargeRepositoryPaths);

            var (status, body) = await ListDirectories();

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("c0ffee1", body.GetProperty("commitSha").GetString());
            Assert.Equal(4, body.GetProperty("directoryCount").GetInt32());
            Assert.Equal(new[] { "billing", "Customer", "docs", "zeta" }, Names(body));
            Assert.Equal(new[] { "Latest" }, VersionsOf(body, "billing"));
            Assert.Equal(new[] { "latest", "Version2", "Version10" }, VersionsOf(body, "Customer"));
            Assert.Empty(VersionsOf(body, "docs"));
            Assert.Equal(new[] { "Version1" }, VersionsOf(body, "zeta"));
        }

        [Fact]
        public async Task Returns_502_rather_than_a_partial_list_when_a_folder_listing_is_also_truncated()
        {
            _factory.GitHub.TruncatedRepository("acme", "axi-products", "c0ffee1", LargeRepositoryPaths);
            _factory.GitHub.Respond(
                "/repos/acme/axi-products/git/trees/sha-Customer",
                HttpStatusCode.OK,
                new
                {
                    sha = "sha-Customer",
                    truncated = true,
                    tree = new[] { new { path = "latest", type = "tree", sha = "sha-Customer/latest" } }
                });

            var (status, problem, raw, contentType) = await ListDirectoriesRaw();

            Assert.Equal(HttpStatusCode.BadGateway, status);
            Assert.Equal("application/problem+json", contentType);
            Assert.Equal(502, problem.GetProperty("status").GetInt32());
            Assert.DoesNotContain(AxiGitApiFactory.TestPat, raw);
        }

        [Fact]
        public async Task Makes_no_fallback_calls_when_the_tree_is_not_truncated()
        {
            _factory.GitHub.Repository("acme", "axi-products", "c0ffee1", LargeRepositoryPaths);

            var (status, _) = await ListDirectories();

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(
                new[]
                {
                    "/repos/acme/axi-products/commits/main",
                    "/repos/acme/axi-products/git/trees/tree-c0ffee1?recursive=1"
                },
                _factory.GitHub.Requests.Select(r => r.RequestUri!.PathAndQuery).ToArray());
        }

        [Fact]
        public async Task Keeps_CORS_headers_on_a_500_so_browser_clients_can_read_the_problem()
        {
            _factory.GitHub.RespondRaw(
                "/repos/acme/axi-products/commits/main",
                HttpStatusCode.OK,
                "not json");

            HttpClient client = _factory.CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/git/listdirectories")
            {
                Content = JsonContent.Create(new { account = "acme", repo = "axi-products", managerType = "Product" })
            };
            request.Headers.Add("Origin", "https://axicmdline.example");

            HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins),
                "Expected Access-Control-Allow-Origin on the 500 response.");
            Assert.Equal("*", origins!.Single());
        }

        [Fact]
        public async Task Existing_endpoints_keep_their_own_error_response_shape()
        {
            var (status, body, _) = await Post(
                "/api/git/push",
                new
                {
                    physicalPath = @"Z:\does\not\exist\axigit-test",
                    targetRepo = "axi-products",
                    productName = "Customer",
                    productDescription = "d",
                    productVersion = "1",
                    productAuthor = "a",
                    gitBranch = "main",
                    managerType = "Product",
                    ensureRepository = false,
                    compress = false
                });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.False(body.GetProperty("success").GetBoolean());
            Assert.Equal("Physical path does not exist.", body.GetProperty("message").GetString());
        }

        [Theory]
        [InlineData("Product")]
        [InlineData("ChangeRequest")]
        [InlineData("Plugin")]
        [InlineData("plugin")]
        public async Task Lists_every_visible_top_level_folder_even_without_Versions(string managerType)
        {
            _factory.GitHub.Repository(
                "acme", "axpert-plugins", "c0ffee1",
                ".github/",
                ".github/workflows/",
                "readme.md",
                "PlgTest/",
                "PlgTest/CustomPages/",
                "PlgTest/Structures/",
                "Axi_Beta/",
                "Axi_Beta/README.md",
                "Axi_Beta/PluginScripts/",
                "OnlyFiles/",
                "OnlyFiles/plugin.json");

            var (status, body) = await ListDirectories(repo: "axpert-plugins", managerType: managerType);

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(3, body.GetProperty("directoryCount").GetInt32());
            Assert.Equal(new[] { "Axi_Beta", "OnlyFiles", "PlgTest" }, Names(body));
            Assert.Empty(VersionsOf(body, "Axi_Beta"));
            Assert.Empty(VersionsOf(body, "OnlyFiles"));
            Assert.Empty(VersionsOf(body, "PlgTest"));
        }

        [Theory]
        [InlineData("Product")]
        [InlineData("ChangeRequest")]
        [InlineData("Plugin")]
        public async Task Lists_folders_with_and_without_Versions_side_by_side(string managerType)
        {
            _factory.GitHub.Repository(
                "acme", "axpert-plugins", "c0ffee1",
                "__bkp/",
                "__bkp/Axi/",
                "PlgTest/",
                "PlgTest/Structures/",
                "Versioned/",
                "Versioned/Version2/",
                "Versioned/Latest/",
                "Versioned/Version1/");

            var (_, body) = await ListDirectories(repo: "axpert-plugins", managerType: managerType);

            Assert.Equal(new[] { "PlgTest", "Versioned", "__bkp" }, Names(body));
            Assert.Empty(VersionsOf(body, "__bkp"));
            Assert.Empty(VersionsOf(body, "PlgTest"));
            Assert.Equal(new[] { "Latest", "Version1", "Version2" }, VersionsOf(body, "Versioned"));
        }

        [Fact]
        public async Task Authenticates_to_GitHub_with_the_configured_PAT_and_ignores_any_token_in_the_request()
        {
            _factory.GitHub.Repository(
                "acme", "axi-products", "c0ffee1",
                "Customer/",
                "Customer/Version1/");

            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/git/listdirectories",
                new { account = "acme", repo = "axi-products", managerType = "Product", token = "token-from-request" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(_factory.GitHub.Requests);
            Assert.All(_factory.GitHub.Requests, request =>
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(AxiGitApiFactory.TestPat, request.Headers.Authorization?.Parameter);
            });
        }
    }
}
