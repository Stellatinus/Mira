using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Services;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class AccountServiceTests
{
    private static (AccountService Service, InMemorySecretStore Store, FakeHandler Handler) Make(
        Func<HttpResponseMessage> user, params (string, string)[] saved)
    {
        var store = new InMemorySecretStore(saved);
        var handler = new FakeHandler(_ => user());
        return (new AccountService(store, Gh.Api(handler)), store, handler);
    }

    [Fact]
    public async Task An_empty_token_is_refused_without_calling_github()
    {
        var (service, _, handler) = Make(() => Gh.Json(Gh.UserJson));

        var result = await service.AddAsync("   ", CancellationToken.None);

        Assert.Equal("token-empty", result.Error!.Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task An_invalid_token_is_refused_and_not_saved()
    {
        var (service, store, _) = Make(() => Gh.Status(HttpStatusCode.Unauthorized));

        var result = await service.AddAsync("ghp_bad", CancellationToken.None);

        Assert.Equal("token-invalid", result.Error!.Code);
        Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task When_github_is_unreachable_the_cause_is_kept()
    {
        var (service, store, _) = Make(() => throw new HttpRequestException("giù"));

        var result = await service.AddAsync("ghp_x", CancellationToken.None);

        Assert.Equal("github-unreachable", result.Error!.Code);
        Assert.Equal("network", result.Error.Cause!.Code);
        Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task A_valid_token_is_trimmed_and_saved_under_the_login_github_reports()
    {
        var (service, store, _) = Make(() => Gh.Json(Gh.UserJson, HttpStatusCode.OK, ("x-oauth-scopes", "repo, notifications")));

        var result = await service.AddAsync("  ghp_ok  ", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("alice", result.Account!.Login);
        Assert.Equal("ok", result.Account.State);
        Assert.Empty(result.Account.Warnings);
        Assert.Equal("ghp_ok", (await store.LoadAllAsync())["alice"]);
    }

    [Fact]
    public async Task A_token_without_the_needed_scopes_gets_a_warning_for_each_missing_one()
    {
        var (service, _, _) = Make(() => Gh.Json(Gh.UserJson, HttpStatusCode.OK, ("x-oauth-scopes", "read:org")));

        var result = await service.AddAsync("ghp_x", CancellationToken.None);

        Assert.Equal(new[] { "missing-scope:notifications", "missing-scope:repo" }, result.Account!.Warnings);
    }

    [Fact]
    public async Task A_fine_grained_token_is_flagged_because_it_cannot_read_notifications()
    {
        var (service, _, _) = Make(() => Gh.Json(Gh.UserJson));

        var result = await service.AddAsync("github_pat_x", CancellationToken.None);

        Assert.Equal(new[] { "fine-grained" }, result.Account!.Warnings);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "invalid")]
    [InlineData(HttpStatusCode.InternalServerError, "error")]
    public async Task The_status_of_a_saved_account_follows_what_github_answers(HttpStatusCode status, string state)
    {
        var (service, _, _) = Make(() => Gh.Status(status), ("alice", "ghp_x"));

        var statuses = await service.GetStatusesAsync(CancellationToken.None);

        var account = Assert.Single(statuses);
        Assert.Equal("alice", account.Login);
        Assert.Equal(state, account.State);
        Assert.Empty(account.Warnings);
    }

    [Fact]
    public async Task Removing_an_account_forgets_its_token()
    {
        var (service, store, _) = Make(() => Gh.Json(Gh.UserJson), ("alice", "ghp_x"), ("bob", "ghp_y"));

        await service.RemoveAsync("alice", CancellationToken.None);

        Assert.Equal(new[] { "bob" }, (await store.LoadAllAsync()).Keys);
    }

    [Fact]
    public async Task Statuses_are_sorted_by_login_ignoring_case()
    {
        var store = new InMemorySecretStore(("Zed", "t1"), ("alice", "t2"), ("Bob", "t3"));
        var handler = new FakeHandler(request =>
            Gh.Json($$"""{"login":"{{request.Headers.Authorization!.Parameter switch { "t1" => "Zed", "t2" => "alice", _ => "Bob" }}}"}"""));
        var service = new AccountService(store, Gh.Api(handler));

        var statuses = await service.GetStatusesAsync(CancellationToken.None);

        Assert.Equal(new[] { "alice", "Bob", "Zed" }, statuses.Select(s => s.Login));
    }
}
