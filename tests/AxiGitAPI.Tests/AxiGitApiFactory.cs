using AxiGitAPI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AxiGitAPI.Tests
{
    // Hosts the real AxiGIT API in memory. Only GitHub is faked (StubGitHub).
    // Configuration still holds a known PAT, so tests can prove ListDirectories
    // uses the request's PAT instead and never leaks the configured one.
    public class AxiGitApiFactory : WebApplicationFactory<Program>
    {
        public const string TestPat = "test-pat-from-config";

        public StubGitHub GitHub { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitHub:Token"] = TestPat
                });
            });

            builder.ConfigureServices(services =>
            {
                services
                    .AddHttpClient(DirectoryListService.GitHubClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => GitHub);
            });
        }
    }
}
