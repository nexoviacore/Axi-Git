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
