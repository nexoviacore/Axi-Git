using System.Net;
using System.Text.Json;

namespace AxiGitAPI.Tests
{
    // WebApplicationFactory runs in the Development environment, where Swagger is enabled.
    public class SwaggerTests : IDisposable
    {
        private readonly AxiGitApiFactory _factory = new();

        public void Dispose() => _factory.Dispose();

        private async Task<JsonElement> GetOpenApiDocument()
        {
            HttpResponseMessage response =
                await _factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        [Fact]
        public async Task Documents_ListDirectories_with_its_request_and_every_response_status()
        {
            JsonElement document = await GetOpenApiDocument();

            JsonElement operation =
                document.GetProperty("paths").GetProperty("/api/git/listdirectories").GetProperty("post");

            Assert.True(operation.TryGetProperty("requestBody", out _));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));

            string[] statuses = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToArray();
            Assert.Equal(new[] { "200", "400", "404", "500", "502" }, statuses.OrderBy(s => s).ToArray());
        }

        [Fact]
        public async Task Documents_the_existing_endpoints_too()
        {
            JsonElement paths = (await GetOpenApiDocument()).GetProperty("paths");

            Assert.True(paths.TryGetProperty("/api/git/push", out _));
            Assert.True(paths.TryGetProperty("/api/git/pull", out _));
            Assert.True(paths.TryGetProperty("/api/git/listrepositories", out _));
        }

        [Fact]
        public async Task Serves_the_Swagger_UI()
        {
            HttpResponseMessage response =
                await _factory.CreateClient().GetAsync("/swagger/index.html");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
        }
    }
}
