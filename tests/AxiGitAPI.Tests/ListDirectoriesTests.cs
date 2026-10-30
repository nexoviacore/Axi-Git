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
        public async Task Ignores_top_level_folders_and_files_that_hold_no_Latest_or_Version()
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
            Assert.Equal(new[] { "Customer" }, Names(body));
            Assert.Equal(new[] { "Version1" }, VersionsOf(body, "Customer"));
            Assert.Equal(1, body.GetProperty("directoryCount").GetInt32());
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
