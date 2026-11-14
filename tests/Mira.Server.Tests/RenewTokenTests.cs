using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mira.Server.Api;
using Mira.Server.Services;
using Mira.Server.Storage;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class RenewTokenTests
{
    private static (AccountService Service, InMemorySecretStore Store, FakeHandler Handler) Make(Func<HttpResponseMessage> user)
    {
        var store = new InMemorySecretStore(("alice", "old-token"), ("bob", "bobs-token"));
        var handler = new FakeHandler(_ => user());
        return (new AccountService(store, Gh.Api(handler)), store, handler);
    }

    [Fact]
    public async Task A_new_token_of_the_same_account_replaces_the_old_one()
    {
        var (service, store, _) = Make(() => Gh.Json(Gh.UserJson, HttpStatusCode.OK, ("x-oauth-scopes", "repo, notifications")));

        var result = await service.RenewAsync("alice", "  new-token  ", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("alice", result.Account!.Login);
        var tokens = await store.LoadAllAsync();
        Assert.Equal("new-token", tokens["alice"]);
        Assert.Equal("bobs-token", tokens["bob"]);
        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public async Task The_login_is_compared_ignoring_case()
    {
        var (service, store, _) = Make(() => Gh.Json(Gh.UserJson));

        var result = await service.RenewAsync("ALICE", "new-token", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("new-token", (await store.LoadAllAsync())["alice"]);
    }

    [Fact]
    public async Task A_token_of_another_account_is_refused_and_nothing_changes()
    {
        var (service, store, _) = Make(() => Gh.Json("""{"login":"mallory"}"""));

        var result = await service.RenewAsync("alice", "mallorys-token", CancellationToken.None);

        Assert.Equal("token-other-account", result.Error!.Code);
        Assert.Equal("mallory", result.Error.Detail);
        Assert.Equal("old-token", (await store.LoadAllAsync())["alice"]);
        Assert.DoesNotContain("mallorys-token", (await store.LoadAllAsync()).Values);
    }

    [Fact]
    public async Task An_invalid_token_is_refused_and_the_old_one_stays()
    {
        var (service, store, _) = Make(() => Gh.Status(HttpStatusCode.Unauthorized));

        var result = await service.RenewAsync("alice", "bad", CancellationToken.None);

        Assert.Equal("token-invalid", result.Error!.Code);
        Assert.Equal("old-token", (await store.LoadAllAsync())["alice"]);
    }

    [Fact]
    public async Task An_empty_token_is_refused_without_calling_github()
    {
        var (service, _, handler) = Make(() => Gh.Json(Gh.UserJson));

        var result = await service.RenewAsync("alice", "   ", CancellationToken.None);

        Assert.Equal("token-empty", result.Error!.Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task An_account_that_is_not_connected_cannot_be_renewed_and_github_is_not_called()
    {
        var (service, store, handler) = Make(() => Gh.Json(Gh.UserJson));

        var result = await service.RenewAsync("nessuno", "new-token", CancellationToken.None);

        Assert.Equal("account-unknown", result.Error!.Code);
        Assert.Equal(0, handler.Count);
        Assert.Equal(2, (await store.LoadAllAsync()).Count);
    }

    [Fact]
    public async Task When_github_is_unreachable_the_old_token_stays()
    {
        var (service, store, _) = Make(() => throw new HttpRequestException("giù"));

        var result = await service.RenewAsync("alice", "new-token", CancellationToken.None);

        Assert.Equal("github-unreachable", result.Error!.Code);
        Assert.Equal("old-token", (await store.LoadAllAsync())["alice"]);
    }
}

public sealed class RenewEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private InMemorySecretStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new InMemorySecretStore(("alice", "old-token"));
        var github = new FakeHandler(request => request.Headers.Authorization!.Parameter == "mallory" ? Gh.Json("""{"login":"mallory"}""") : Gh.Json(Gh.UserJson));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISecretStore>(_store);
        builder.Services.AddSingleton(Gh.Api(github));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<DashboardService>();
        builder.Services.AddSingleton<IssueService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<SectionService>();
        builder.Services.AddSingleton(new FollowStore(Path.Combine(Path.GetTempPath(), "mira-follow-" + Guid.NewGuid().ToString("N"))));
        builder.Services.AddSingleton<ReleaseService>();
        builder.Services.AddSingleton(provider => new UpdateService(provider.GetRequiredService<Mira.Server.GitHub.GitHubApi>(), "1.2.0"));
        builder.Services.AddSingleton(new SeenStore(Path.Combine(Path.GetTempPath(), "mira-renew-" + Guid.NewGuid().ToString("N"))));
        _app = builder.Build();
        _app.MapMiraApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static StringContent Body(string token) => new($$"""{"token":"{{token}}"}""", System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Renewing_answers_200_with_the_account_status_and_saves_the_token()
    {
        var response = await _client.PostAsync("/api/accounts/alice/renew", Body("new-token"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"login\":\"alice\"", await response.Content.ReadAsStringAsync());
        Assert.Equal("new-token", (await _store.LoadAllAsync())["alice"]);
    }

    [Fact]
    public async Task A_token_of_another_account_answers_400_with_the_code()
    {
        var response = await _client.PostAsync("/api/accounts/alice/renew", Body("mallory"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("token-other-account", await response.Content.ReadAsStringAsync());
        Assert.Equal("old-token", (await _store.LoadAllAsync())["alice"]);
    }

    [Fact]
    public async Task An_unknown_account_answers_404()
    {
        var response = await _client.PostAsync("/api/accounts/nessuno/renew", Body("new-token"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
