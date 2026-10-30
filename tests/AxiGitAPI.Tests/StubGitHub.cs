using System.Net;
using System.Text;
using System.Text.Json;

namespace AxiGitAPI.Tests
{
    // Fake GitHub REST API: answers canned responses by request path
    // and records every request it receives.
    public class StubGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes =
            new(StringComparer.OrdinalIgnoreCase);

        public List<HttpRequestMessage> Requests { get; } = new();

        public void Respond(string pathAndQuery, HttpStatusCode status, object body)
        {
            _routes[pathAndQuery] = (status, JsonSerializer.Serialize(body));
        }

        // Sets up a Repository whose main branch is at commitSha and holds the given tree paths.
        // Paths ending in "/" are folders ("tree"); all others are files ("blob").
        public void Repository(
            string account,
            string repo,
            string commitSha,
            params string[] paths)
        {
            string treeSha = "tree-" + commitSha;

            Respond(
                $"/repos/{account}/{repo}/commits/main",
                HttpStatusCode.OK,
                new { sha = commitSha, commit = new { tree = new { sha = treeSha } } });

            Respond(
                $"/repos/{account}/{repo}/git/trees/{treeSha}?recursive=1",
                HttpStatusCode.OK,
                new
                {
                    sha = treeSha,
                    truncated = false,
                    tree = paths.Select(p => new
                    {
                        path = p.TrimEnd('/'),
                        type = p.EndsWith('/') ? "tree" : "blob"
                    })
                });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            string key = request.RequestUri!.PathAndQuery;

            if (!_routes.TryGetValue(key, out var route))
            {
                route = (HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
            }

            return Task.FromResult(new HttpResponseMessage(route.Status)
            {
                Content = new StringContent(route.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
