using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class ConditionalRequestTests
{
    private static HttpResponseMessage WithEtag(string json, string etag, params (string, string)[] headers) =>
        Gh.Json(json, HttpStatusCode.OK, headers.Append(("ETag", etag)).ToArray());

    private static HttpResponseMessage NotModified(params (string, string)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    [Fact]
    public async Task The_second_read_sends_the_etag_and_a_304_reuses_the_first_answer()
    {
        var calls = 0;
        var handler = new FakeHandler(request => ++calls == 1 ? WithEtag(Gh.UserJson, "\"v1\"") : NotModified());
        var api = Gh.Api(handler);

        var first = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        var second = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Null(handler.Requests[0].IfNoneMatch);
        Assert.Equal("\"v1\"", handler.Requests[1].IfNoneMatch);
        Assert.True(second.Ok);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Data, second.Data);
        Assert.Equal("alice", second.Data!.Login);
    }

    [Fact]
    public async Task A_changed_answer_replaces_the_cached_one_and_its_new_etag_is_used_next()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls switch
        {
            1 => WithEtag("""{"login":"alice"}""", "\"v1\""),
            2 => WithEtag("""{"login":"alice2"}""", "\"v2\""),
            _ => NotModified(),
        });
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        var changed = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        var reused = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Equal("alice2", changed.Data!.Login);
        Assert.Equal("\"v2\"", handler.Requests[2].IfNoneMatch);
        Assert.Equal("alice2", reused.Data!.Login);
    }

    [Fact]
    public async Task Without_an_etag_nothing_is_cached_and_no_conditional_header_is_sent()
    {
        var handler = new FakeHandler(_ => Gh.Json(Gh.UserJson));
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.All(handler.Requests, r => Assert.Null(r.IfNoneMatch));
    }

    [Fact]
    public async Task Different_paths_and_different_tokens_do_not_share_answers()
    {
        var handler = new FakeHandler(request => WithEtag(Gh.UserJson, "\"" + request.Headers.Authorization!.Parameter + "\""));
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("a", "user", CancellationToken.None);
        await api.GetAsync<GhUser>("b", "user", CancellationToken.None);
        await api.GetAsync<GhUser>("a", "other", CancellationToken.None);
        await api.GetAsync<GhUser>("a", "user", CancellationToken.None);

        Assert.Null(handler.Requests[1].IfNoneMatch);
        Assert.Null(handler.Requests[2].IfNoneMatch);
        Assert.Equal("\"a\"", handler.Requests[3].IfNoneMatch);
    }

    [Fact]
    public async Task A_304_keeps_the_cached_expiration_and_scopes_unless_it_repeats_them()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls switch
        {
            1 => WithEtag(Gh.UserJson, "\"v1\"", ("github-authentication-token-expiration", "2026-12-01 10:00:00 UTC"), ("x-oauth-scopes", "repo")),
            2 => NotModified(),
            _ => NotModified(("x-oauth-scopes", "repo, notifications")),
        });
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        var second = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        var third = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero), second.TokenExpiresAt);
        Assert.Equal(new[] { "repo" }, second.Scopes);
        Assert.Equal(new[] { "repo", "notifications" }, third.Scopes);
    }

    [Fact]
    public async Task A_304_still_updates_the_rate_limit_information()
    {
        var calls = 0;
        var reset = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString();
        var handler = new FakeHandler(_ => ++calls == 1
            ? WithEtag(Gh.UserJson, "\"v1\"", ("x-ratelimit-limit", "30"), ("x-ratelimit-remaining", "20"), ("x-ratelimit-reset", reset), ("x-ratelimit-resource", "search"))
            : NotModified(("x-ratelimit-limit", "30"), ("x-ratelimit-remaining", "20"), ("x-ratelimit-reset", reset), ("x-ratelimit-resource", "search")));
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("t", "search/issues?q=x", CancellationToken.None);
        await api.GetAsync<GhUser>("t", "search/issues?q=x", CancellationToken.None);

        Assert.Equal(20, api.RateLimitsFor("t").Single().Remaining);
    }

    [Fact]
    public async Task A_failed_response_is_not_cached()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1 ? Gh.Status(HttpStatusCode.Forbidden) : WithEtag(Gh.UserJson, "\"v1\""));
        var api = Gh.Api(handler);

        var failed = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        var ok = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.False(failed.Ok);
        Assert.True(ok.Ok);
        Assert.Null(handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task A_304_with_nothing_cached_is_reported_as_an_error_not_as_empty_data()
    {
        var api = Gh.Api(new FakeHandler(_ => NotModified()));

        var response = await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("github-http", response.Error!.Code);
        Assert.Equal("304", response.Error.Detail);
    }

    [Fact]
    public async Task Anonymous_reads_are_conditional_too_and_separate_from_the_ones_with_a_token()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls <= 2 ? WithEtag("""{"tag_name":"v1","html_url":"u","draft":false,"prerelease":false}""", "\"r1\"") : NotModified());
        var api = Gh.Api(handler);

        await api.GetAnonymousAsync<GhRelease>("repos/x/y/releases/latest", CancellationToken.None);
        await api.GetAsync<GhRelease>("t", "repos/x/y/releases/latest", CancellationToken.None);
        await api.GetAnonymousAsync<GhRelease>("repos/x/y/releases/latest", CancellationToken.None);

        Assert.Null(handler.Requests[1].IfNoneMatch);
        Assert.Equal("\"r1\"", handler.Requests[2].IfNoneMatch);
    }

    [Fact]
    public async Task Graphql_posts_are_never_conditional()
    {
        var handler = new FakeHandler(_ => WithEtag("""{"data":{}}""", "\"g\""));
        var api = Gh.Api(handler);

        await api.QueryAsync<GqlData>("t", "query { viewer { login } }", null, CancellationToken.None);
        await api.QueryAsync<GqlData>("t", "query { viewer { login } }", null, CancellationToken.None);

        Assert.All(handler.Requests, r => Assert.Null(r.IfNoneMatch));
    }

    [Fact]
    public async Task Only_the_most_recent_answers_are_kept()
    {
        var handler = new FakeHandler(request => WithEtag(Gh.UserJson, "\"" + request.RequestUri!.PathAndQuery.GetHashCode() + "\""));
        var api = Gh.Api(handler);
        var extra = 5;

        for (var i = 0; i < GitHubApi.MaxCachedResponses + extra; i++)
            await api.GetAsync<GhUser>("t", $"user/{i}", CancellationToken.None);
        var before = handler.Count;

        await api.GetAsync<GhUser>("t", "user/0", CancellationToken.None);                                   // la più vecchia è uscita
        await api.GetAsync<GhUser>("t", $"user/{GitHubApi.MaxCachedResponses + extra - 1}", CancellationToken.None); // l'ultima c'è ancora

        Assert.Null(handler.Requests[before].IfNoneMatch);
        Assert.NotNull(handler.Requests[before + 1].IfNoneMatch);
    }

    [Fact]
    public async Task A_huge_answer_is_not_kept()
    {
        var huge = "{\"login\":\"" + new string('x', 1_100_000) + "\"}";
        var handler = new FakeHandler(_ => WithEtag(huge, "\"big\""));
        var api = Gh.Api(handler);

        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);
        await api.GetAsync<GhUser>("t", "user", CancellationToken.None);

        Assert.Null(handler.Requests[1].IfNoneMatch);
    }
}
