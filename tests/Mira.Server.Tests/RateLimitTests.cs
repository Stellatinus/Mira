using System.Globalization;
using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Services;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class RateLimitTests
{
    private static (string, string)[] Limits(string resource, int limit, int remaining, DateTimeOffset reset) =>
    [
        ("x-ratelimit-limit", limit.ToString()),
        ("x-ratelimit-remaining", remaining.ToString()),
        ("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString()),
        ("x-ratelimit-resource", resource),
    ];

    private static HttpResponseMessage Answer(string resource, int limit, int remaining, DateTimeOffset reset, HttpStatusCode status = HttpStatusCode.OK) =>
        Gh.Json(Gh.UserJson, status, Limits(resource, limit, remaining, reset));

    [Fact]
    public async Task The_limit_of_each_resource_is_remembered_from_the_response_headers()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(1);
        var handler = new FakeHandler(request => request.RequestUri!.PathAndQuery.StartsWith("/search")
            ? Answer("search", 30, 28, reset)
            : Answer("core", 5000, 4990, reset.AddMinutes(50)));
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        await api.GetAsync<GhUser>("t", "search/issues?q=x", CancellationToken.None);

        var limits = api.RateLimitsFor("t");
        Assert.Equal(new[] { "core", "search" }, limits.Select(l => l.Resource));
        var search = limits.Single(l => l.Resource == "search");
        Assert.Equal((30, 28), (search.Limit, search.Remaining));
        Assert.Equal(reset.ToUnixTimeSeconds(), search.ResetsAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_response_without_the_headers_leaves_nothing_to_report()
    {
        var api = Gh.Api(new FakeHandler(_ => Gh.Json(Gh.UserJson)));

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Empty(api.RateLimitsFor("t"));
    }

    [Fact]
    public async Task Without_a_resource_header_the_limit_is_the_core_one()
    {
        var api = Gh.Api(new FakeHandler(_ => Gh.Json(Gh.UserJson, HttpStatusCode.OK,
            ("x-ratelimit-limit", "60"), ("x-ratelimit-remaining", "59"), ("x-ratelimit-reset", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()))));

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Equal("core", Assert.Single(api.RateLimitsFor("t")).Resource);
    }

    [Fact]
    public async Task Each_token_has_its_own_limits()
    {
        var api = Gh.Api(new FakeHandler(request => Answer("search", 30, request.Headers.Authorization!.Parameter == "a" ? 1 : 29, DateTimeOffset.UtcNow.AddMinutes(1))));

        await api.GetAsync<GhUser>("a", "user", CancellationToken.None);
        await api.GetAsync<GhUser>("b", "user", CancellationToken.None);

        Assert.Equal(1, api.RateLimitsFor("a").Single().Remaining);
        Assert.Equal(29, api.RateLimitsFor("b").Single().Remaining);
        Assert.Empty(api.RateLimitsFor("c"));
    }

    [Fact]
    public async Task A_window_that_has_already_reset_counts_as_full_again()
    {
        var api = Gh.Api(new FakeHandler(_ => Answer("search", 30, 0, DateTimeOffset.UtcNow.AddSeconds(-5))));

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Equal(30, api.RateLimitsFor("t").Single().Remaining);
    }

    [Fact]
    public async Task The_limit_is_also_read_from_failed_responses()
    {
        var api = Gh.Api(new FakeHandler(_ => Answer("search", 30, 0, DateTimeOffset.UtcNow.AddMinutes(1), HttpStatusCode.Forbidden)));

        await api.GetAsync<GhUser>("t", "search/issues", CancellationToken.None);

        Assert.Equal(0, api.RateLimitsFor("t").Single().Remaining);
    }

    [Fact]
    public async Task When_the_requests_are_over_the_error_says_when_to_retry()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(1);
        var api = Gh.Api(new FakeHandler(_ => Answer("search", 30, 0, reset, HttpStatusCode.Forbidden)));

        var response = await api.GetAsync<GhUser>("t", "search/issues", CancellationToken.None);

        Assert.Equal("rate-limited", response.Error!.Code);
        var retry = DateTimeOffset.Parse(response.Error.Detail!, CultureInfo.InvariantCulture);
        Assert.Equal(reset.ToUnixTimeSeconds(), retry.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_secondary_limit_uses_retry_after()
    {
        var api = Gh.Api(new FakeHandler(_ =>
        {
            var response = Gh.Status(HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("Retry-After", "60");
            return response;
        }));

        var response = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        var retry = DateTimeOffset.Parse(response.Error!.Detail!, CultureInfo.InvariantCulture);
        Assert.InRange((retry - DateTimeOffset.UtcNow).TotalSeconds, 55, 65);
    }

    [Fact]
    public async Task A_refusal_with_requests_left_gives_no_retry_time()
    {
        // Un 403 con richieste ancora disponibili è un accesso negato (permessi, SSO), non un limite.
        var api = Gh.Api(new FakeHandler(_ => Answer("core", 5000, 4000, DateTimeOffset.UtcNow.AddMinutes(5), HttpStatusCode.Forbidden)));

        var response = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Equal("rate-limited", response.Error!.Code);
        Assert.Null(response.Error.Detail);
    }

    [Fact]
    public async Task The_dashboard_reports_the_limits_of_each_account_after_loading()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(1);
        var handler = new Router()
            .On(path => path == "user", _ => Answer("core", 5000, 4999, reset.AddMinutes(59)))
            .On("graphql", () => Gh.Json(Fixtures.Repositories([]), HttpStatusCode.OK, Limits("graphql", 5000, 4990, reset.AddMinutes(59))))
            .On("notifications", () => Gh.Json("[]"))
            .On("search/issues", () => Gh.Json(Fixtures.Search(0), HttpStatusCode.OK, Limits("search", 30, 25, reset)))
            .Handler();
        var service = new DashboardService(new InMemorySecretStore(("alice", "t")), Gh.Api(handler));

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        var limits = Assert.Single(snapshot.Accounts).RateLimits!;
        Assert.Equal(new[] { "core", "graphql", "search" }, limits.Select(l => l.Resource));
        Assert.Equal(25, limits.Single(l => l.Resource == "search").Remaining);
    }

    [Fact]
    public async Task The_account_list_reports_the_limits_too()
    {
        var api = Gh.Api(new FakeHandler(_ => Answer("core", 5000, 4000, DateTimeOffset.UtcNow.AddMinutes(30))));
        var service = new AccountService(new InMemorySecretStore(("alice", "t")), api);

        var status = Assert.Single(await service.GetStatusesAsync(CancellationToken.None));

        Assert.Equal(4000, Assert.Single(status.RateLimits!).Remaining);
    }
}
