using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mira.Server.GitHub;
using Mira.Server.Storage;

namespace Mira.Server.Tests.Support;

/// <summary>Un HttpMessageHandler che risponde con quello che decide il test e ricorda le richieste ricevute.</summary>
public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, string? Authorization, string? Body)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, string Path, string? Authorization, string? Body)> Requests
    {
        get { lock (_requests) return _requests.ToList(); }
    }

    public int Count => Requests.Count;

    public int CountMatching(string pathContains) => Requests.Count(r => r.Path.Contains(pathContains, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
            _requests.Add((request.Method, request.RequestUri!.PathAndQuery.TrimStart('/'), request.Headers.Authorization?.ToString(), body));
        return respond(request);
    }
}

public sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.github.com/") };
}

public static class Gh
{
    public static GitHubApi Api(HttpMessageHandler handler) => new(new FakeFactory(handler), NullLogger<GitHubApi>.Instance);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("{}") };

    public const string UserJson = """{"login":"alice","name":"Alice","avatar_url":"https://avatars.example/alice","html_url":"https://github.com/alice"}""";
}

/// <summary>Un archivio di segreti in memoria, per i test dei servizi.</summary>
public sealed class InMemorySecretStore(params (string Login, string Token)[] initial) : ISecretStore
{
    private readonly Dictionary<string, string> _data = initial.ToDictionary(x => x.Login, x => x.Token, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, string>> LoadAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_data, StringComparer.OrdinalIgnoreCase));

    public Task SaveAsync(string name, string secret, CancellationToken ct = default)
    {
        _data[name] = secret;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string name, CancellationToken ct = default)
    {
        _data.Remove(name);
        return Task.CompletedTask;
    }
}

/// <summary>Risposte finte di GitHub nella forma documentata dall'API (campi limitati a quelli che Mira legge).</summary>
public static class Fixtures
{
    public static string Issue(long id, string title, string repo = "acme/app", bool pr = false, bool draft = false,
        int comments = 0, string? body = null, string[]? assignees = null, string updatedAt = "2026-11-10T10:00:00Z", string author = "bob")
    {
        var assigneesJson = string.Join(",", (assignees ?? []).Select(a => $"{{\"login\":\"{a}\"}}"));
        var kind = pr ? "pull" : "issues";
        return $$"""
            {"id":{{id}},"number":{{id}},"title":{{System.Text.Json.JsonSerializer.Serialize(title)}},
             "html_url":"https://github.com/{{repo}}/{{kind}}/{{id}}","state":"open","draft":{{(draft ? "true" : "false")}},
             "comments":{{comments}},"updated_at":"{{updatedAt}}","repository_url":"https://api.github.com/repos/{{repo}}",
             "user":{"login":"{{author}}"},{{(pr ? "\"pull_request\":{\"url\":\"x\"}," : "")}}
             "body":{{(body is null ? "null" : System.Text.Json.JsonSerializer.Serialize(body))}},
             "assignees":[{{assigneesJson}}]}
            """;
    }

    public static string Search(int total, params string[] issues) =>
        "{\"total_count\":" + total + ",\"incomplete_results\":false,\"items\":[" + string.Join(",", issues) + "]}";

    public static string Notification(string id, string title, string type, string? apiUrl, string reason = "mention",
        string repo = "acme/app", string updatedAt = "2026-11-10T09:00:00Z") =>
        $$$"""
            {"id":"{{{id}}}","unread":true,"reason":"{{{reason}}}","updated_at":"{{{updatedAt}}}",
             "subject":{"title":"{{{title}}}","url":{{{(apiUrl is null ? "null" : "\"" + apiUrl + "\"")}}},"type":"{{{type}}}"},
             "repository":{"full_name":"{{{repo}}}","html_url":"https://github.com/{{{repo}}}"}}
            """;

    public static string Repo(long id, string name, string owner = "alice", bool isPrivate = false, bool customImage = false) =>
        $$$"""
            {"databaseId":{{{id}}},"name":"{{{name}}}","nameWithOwner":"{{{owner}}}/{{{name}}}","description":"d","url":"https://github.com/{{{owner}}}/{{{name}}}",
             "isPrivate":{{{(isPrivate ? "true" : "false")}}},"stargazerCount":3,"pushedAt":"2026-11-10T08:00:00Z","updatedAt":"2026-11-10T08:00:00Z",
             "primaryLanguage":{"name":"C#"},"issues":{"totalCount":4},"pullRequests":{"totalCount":2},
             "openGraphImageUrl":"https://og.example/{{{name}}}.png","usesCustomOpenGraphImage":{{{(customImage ? "true" : "false")}}},
             "owner":{"avatarUrl":"https://avatars.example/{{{owner}}}"},"isFork":false,"parent":null,"unassigned":{"totalCount":1}}
            """;

    public static string Repositories(string[] repos, string[]? starred = null) =>
        "{\"data\":{\"viewer\":{\"repos\":{\"nodes\":[" + string.Join(",", repos) + "]},\"starred\":{\"nodes\":[" + string.Join(",", starred ?? []) + "]}}}}";
}

/// <summary>Smista le richieste in base al percorso (già decodificato), come farebbe GitHub.</summary>
public sealed class Router
{
    private readonly List<(Func<string, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public Router On(Func<string, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    public Router On(string pathContains, Func<HttpResponseMessage> respond) =>
        On(path => path.Contains(pathContains, StringComparison.Ordinal), _ => respond());

    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery.TrimStart('/'));
        // Vince l'ultima regola aggiunta: un test parte dalle risposte di base e ne cambia solo alcune.
        foreach (var (match, respond) in Enumerable.Reverse(_routes))
        {
            if (match(path)) return respond(request);
        }
        return Gh.Status(System.Net.HttpStatusCode.NotFound);
    }

    public FakeHandler Handler() => new(Handle);
}
