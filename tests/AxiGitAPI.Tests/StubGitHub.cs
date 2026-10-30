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

        public void RespondRaw(string pathAndQuery, HttpStatusCode status, string rawBody)
        {
            _routes[pathAndQuery] = (status, rawBody);
        }

        // Simulates GitHub being unreachable (connection failure, timeout) for a path.
        public void Throw(string pathAndQuery, Exception exception)
        {
            _failures[pathAndQuery] = exception;
        }

        private readonly Dictionary<string, Exception> _failures =
            new(StringComparer.OrdinalIgnoreCase);

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

        // Like Repository, but GitHub truncates the recursive tree (returns only the
        // first entry). Every folder is also served as a non-recursive tree, keyed by
        // a stable sha ("sha-root" for the root, "sha-{path}" for each folder).
        public void TruncatedRepository(
            string account,
            string repo,
            string commitSha,
            params string[] paths)
        {
            const string rootSha = "sha-root";

            Respond(
                $"/repos/{account}/{repo}/commits/main",
                HttpStatusCode.OK,
                new { sha = commitSha, commit = new { tree = new { sha = rootSha } } });

            Respond(
                $"/repos/{account}/{repo}/git/trees/{rootSha}?recursive=1",
                HttpStatusCode.OK,
                new
                {
                    sha = rootSha,
                    truncated = true,
                    tree = paths.Take(1).Select(p => new
                    {
                        path = p.TrimEnd('/'),
                        type = p.EndsWith('/') ? "tree" : "blob"
                    })
                });

            var folders = paths
                .Where(p => p.EndsWith('/'))
                .Select(p => p.TrimEnd('/'))
                .Prepend(string.Empty);

            foreach (string folder in folders)
            {
                string prefix = folder.Length == 0 ? string.Empty : folder + "/";
                string folderSha = folder.Length == 0 ? rootSha : "sha-" + folder;

                var children = paths
                    .Select(p => (Path: p.TrimEnd('/'), IsFolder: p.EndsWith('/')))
                    .Where(p => p.Path.StartsWith(prefix, StringComparison.Ordinal) &&
                                p.Path.Length > prefix.Length &&
                                !p.Path.Substring(prefix.Length).Contains('/'))
                    .Select(p => new
                    {
                        path = p.Path.Substring(prefix.Length),
                        type = p.IsFolder ? "tree" : "blob",
                        sha = p.IsFolder ? "sha-" + p.Path : "blob-" + p.Path
                    });

                Respond(
                    $"/repos/{account}/{repo}/git/trees/{folderSha}",
                    HttpStatusCode.OK,
                    new { sha = folderSha, truncated = false, tree = children });
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            string key = request.RequestUri!.PathAndQuery;

            if (_failures.TryGetValue(key, out Exception? failure))
            {
                return Task.FromException<HttpResponseMessage>(failure);
            }

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
