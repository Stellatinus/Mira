using System.Net;
using Mira.Server.Services;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class IssueServiceTests
{
    private static Router Github() => new Router()
        .On("user/orgs", () => Gh.Json("[]"))
        .On("search/issues", () => Gh.Json(Fixtures.Search(0)));

    private static IssueService Service(Router router, out FakeHandler handler, params (string, string)[] accounts)
    {
        handler = router.Handler();
        return new IssueService(new InMemorySecretStore(accounts.Length == 0 ? [("alice", "t-alice")] : accounts), Gh.Api(handler));
    }

    private static int Searches(FakeHandler handler, string text) =>
        handler.Requests.Count(r => r.Path.StartsWith("search/issues", StringComparison.Ordinal)
            && Uri.UnescapeDataString(r.Path).Contains(text, StringComparison.Ordinal));

    [Fact]
    public async Task Searches_the_users_own_repositories_their_organizations_and_what_concerns_them_elsewhere()
    {
        var router = Github().On("user/orgs", () => Gh.Json("""[{"login":"acme"}]"""));
        var service = Service(router, out var handler);

        await service.GetAsync("issues", false, CancellationToken.None);

        foreach (var qualifier in new[] { "user:alice", "org:acme", "author:alice", "assignee:alice" })
            Assert.Equal(1, Searches(handler, qualifier));
    }

    [Fact]
    public async Task An_organization_shared_by_two_accounts_is_downloaded_only_once()
    {
        var router = Github().On("user/orgs", () => Gh.Json("""[{"login":"acme"}]"""));
        var service = Service(router, out var handler, ("alice", "t-alice"), ("bob", "t-bob"));

        await service.GetAsync("issues", false, CancellationToken.None);

        Assert.Equal(1, Searches(handler, "org:acme"));
        Assert.Equal(1, Searches(handler, "user:alice"));
        Assert.Equal(1, Searches(handler, "user:bob"));
    }

    [Fact]
    public async Task The_same_issue_found_by_several_searches_is_listed_once()
    {
        var same = Fixtures.Issue(5, "doppia");
        var router = Github()
            .On("user:alice", () => Gh.Json(Fixtures.Search(1, same)))
            .On("assignee:alice", () => Gh.Json(Fixtures.Search(1, same, Fixtures.Issue(6, "altra", updatedAt: "2026-11-12T00:00:00Z"))));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync("issues", false, CancellationToken.None);

        Assert.Equal(new[] { "altra", "doppia" }, snapshot.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Downloads_every_page_until_the_results_are_over()
    {
        var page1 = Enumerable.Range(1, 100).Select(i => Fixtures.Issue(i, $"i{i}")).ToArray();
        var page2 = Enumerable.Range(101, 50).Select(i => Fixtures.Issue(i, $"i{i}")).ToArray();
        var router = Github().On(path => path.Contains("user:alice"), request =>
            Gh.Json(Fixtures.Search(150, request.RequestUri!.Query.Contains("page=2") ? page2 : page1)));
        var service = Service(router, out var handler);

        var snapshot = await service.GetAsync("issues", false, CancellationToken.None);

        Assert.Equal(150, snapshot.Items.Count);
        Assert.Equal(2, Searches(handler, "user:alice"));
    }

    [Fact]
    public async Task Stops_at_the_total_even_when_the_last_page_is_full()
    {
        var page = Enumerable.Range(1, 100).Select(i => Fixtures.Issue(i, $"i{i}")).ToArray();
        var router = Github().On(path => path.Contains("user:alice"), _ => Gh.Json(Fixtures.Search(100, page)));
        var service = Service(router, out var handler);

        await service.GetAsync("issues", false, CancellationToken.None);

        Assert.Equal(1, Searches(handler, "user:alice"));
    }

    [Fact]
    public async Task A_failing_page_keeps_what_was_downloaded_and_reports_the_error()
    {
        var page1 = Enumerable.Range(1, 100).Select(i => Fixtures.Issue(i, $"i{i}")).ToArray();
        var router = Github().On(path => path.Contains("user:alice"), request =>
            request.RequestUri!.Query.Contains("page=2") ? Gh.Status(HttpStatusCode.Forbidden) : Gh.Json(Fixtures.Search(300, page1)));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync("issues", false, CancellationToken.None);

        Assert.Equal(100, snapshot.Items.Count);
        var error = Assert.Single(snapshot.Errors);
        Assert.Equal(("alice", "issues", "rate-limited"), (error.Account, error.Section, error.Error.Code));
    }

    [Fact]
    public async Task If_the_organizations_cannot_be_listed_the_own_repositories_are_still_searched()
    {
        var router = Github().On("user/orgs", () => Gh.Status(HttpStatusCode.InternalServerError))
            .On("user:alice", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(1, "mia"))));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync("issues", false, CancellationToken.None);

        Assert.Single(snapshot.Items);
        Assert.Equal("github-http", Assert.Single(snapshot.Errors).Error.Code);
    }

    [Fact]
    public async Task The_result_is_cached_and_invalidate_clears_it()
    {
        var service = Service(Github(), out var handler);
        await service.GetAsync("issues", false, CancellationToken.None);
        var firstRound = handler.Count;

        await service.GetAsync("issues", false, CancellationToken.None);
        Assert.Equal(firstRound, handler.Count);

        service.Invalidate();
        await service.GetAsync("issues", false, CancellationToken.None);
        Assert.Equal(firstRound * 2, handler.Count);
    }

    [Fact]
    public void Only_the_issues_view_exists() => Assert.Equal(new[] { "issues" }, IssueService.Views);
}
