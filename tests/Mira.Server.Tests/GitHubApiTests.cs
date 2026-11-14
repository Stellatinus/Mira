using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class GitHubApiTests
{
    private const string Expiration = "github-authentication-token-expiration";

    private static async Task<ApiResponse<GhUser>> GetUserAsync(FakeHandler handler, string token = "ghp_test") =>
        await Gh.Api(handler).GetAsync<GhUser>(token, "user", CancellationToken.None);

    [Theory]
    [InlineData("2026-12-01 10:00:00 UTC", "2026-12-01T10:00:00+00:00")]
    [InlineData("2026-12-01 10:00:00 +0100", "2026-12-01T09:00:00+00:00")]
    [InlineData("2026-12-01 10:00:00 +01:00", "2026-12-01T09:00:00+00:00")]
    [InlineData("2026-12-01 10:00:00 -0500", "2026-12-01T15:00:00+00:00")]
    [InlineData("2026-12-01 10:00:00 +0000", "2026-12-01T10:00:00+00:00")]
    public async Task Reads_the_token_expiration_in_the_formats_github_documents(string header, string expectedUtc)
    {
        var handler = new FakeHandler(_ => Gh.Json(Gh.UserJson, HttpStatusCode.OK, (Expiration, header)));

        var response = await GetUserAsync(handler);

        Assert.True(response.Ok);
        Assert.NotNull(response.TokenExpiresAt);
        Assert.Equal(DateTimeOffset.Parse(expectedUtc), response.TokenExpiresAt!.Value.ToUniversalTime());
    }

    [Theory]
    [InlineData("domani")]
    [InlineData("2026-12-01")]
    [InlineData("")]
    public async Task An_unrecognised_expiration_is_ignored_without_failing_the_request(string header)
    {
        var handler = new FakeHandler(_ => Gh.Json(Gh.UserJson, HttpStatusCode.OK, (Expiration, header)));

        var response = await GetUserAsync(handler);

        Assert.True(response.Ok);
        Assert.Null(response.TokenExpiresAt);
        Assert.Equal("alice", response.Data!.Login);
    }

    [Fact]
    public async Task A_token_without_expiration_has_no_date()
    {
        var response = await GetUserAsync(new FakeHandler(_ => Gh.Json(Gh.UserJson)));

        Assert.Null(response.TokenExpiresAt);
    }

    [Fact]
    public async Task Reads_the_scopes_of_a_classic_token()
    {
        var handler = new FakeHandler(_ => Gh.Json(Gh.UserJson, HttpStatusCode.OK, ("x-oauth-scopes", "repo, notifications, read:org")));

        var response = await GetUserAsync(handler);

        Assert.Equal(new[] { "repo", "notifications", "read:org" }, response.Scopes);
    }

    [Fact]
    public async Task A_fine_grained_token_has_no_scopes_header()
    {
        var response = await GetUserAsync(new FakeHandler(_ => Gh.Json(Gh.UserJson)));

        Assert.Null(response.Scopes);
    }

    [Fact]
    public async Task Sends_the_token_as_a_bearer_header_on_every_call()
    {
        var handler = new FakeHandler(_ => Gh.Json(Gh.UserJson));

        await GetUserAsync(handler, "ghp_secret");

        Assert.Equal("Bearer ghp_secret", Assert.Single(handler.Requests).Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", null)]
    [InlineData(HttpStatusCode.Forbidden, "rate-limited", null)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limited", null)]
    [InlineData(HttpStatusCode.InternalServerError, "github-http", "500")]
    [InlineData(HttpStatusCode.NotFound, "github-http", "404")]
    public async Task Maps_http_errors_to_stable_codes(HttpStatusCode status, string code, string? detail)
    {
        var response = await GetUserAsync(new FakeHandler(_ => Gh.Status(status)));

        Assert.False(response.Ok);
        Assert.Equal(status, response.Status);
        Assert.Equal(code, response.Error!.Code);
        Assert.Equal(detail, response.Error.Detail);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task A_network_failure_becomes_a_network_error_not_an_exception()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("sconnesso"));

        var response = await GetUserAsync(handler);

        Assert.False(response.Ok);
        Assert.Equal("network", response.Error!.Code);
        Assert.Equal("sconnesso", response.Error.Detail);
    }

    [Fact]
    public async Task A_client_timeout_becomes_a_timeout_error()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout"));

        var response = await GetUserAsync(handler);

        Assert.Equal("timeout", response.Error!.Code);
    }

    [Fact]
    public async Task A_cancellation_requested_by_the_caller_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHandler(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Gh.Api(handler).GetAsync<GhUser>("t", "user", cts.Token));
    }

    [Fact]
    public async Task Graphql_keeps_partial_data_and_the_errors_reported_next_to_it()
    {
        const string body = """
            {"data":{"viewer":{"repos":{"nodes":[null]},"starred":{"nodes":[]}}},
             "errors":[{"type":"FORBIDDEN","message":"Resource protected by organization SAML enforcement"}]}
            """;
        var handler = new FakeHandler(_ => Gh.Json(body));

        var response = await Gh.Api(handler).QueryAsync<GqlData>("t", "query { viewer { login } }", new { first = 1 }, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.NotNull(response.Data!.Data!.Viewer);
        Assert.Equal("FORBIDDEN", Assert.Single(response.Data.Errors!).Type);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("graphql", sent.Path);
        Assert.Contains("\"query\"", sent.Body);
    }
}
