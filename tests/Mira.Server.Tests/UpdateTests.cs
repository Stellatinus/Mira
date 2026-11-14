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

public class SemVerTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0, null)]
    [InlineData("v1.2.0", 1, 2, 0, null)]
    [InlineData("V10.20.30", 10, 20, 30, null)]
    [InlineData("1.3.0-rc.1", 1, 3, 0, "rc.1")]
    [InlineData("0.0.0-dev", 0, 0, 0, "dev")]
    [InlineData("1.0.0+abc123", 1, 0, 0, null)]
    [InlineData("1.0.0-beta.2+sha.5", 1, 0, 0, "beta.2")]
    [InlineData("  1.0.1  ", 1, 0, 1, null)]
    public void Parses_the_versions_mira_uses(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(SemVer.TryParse(text, out var v));
        Assert.Equal(new SemVer(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("1.2.x")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("latest")]
    public void Refuses_what_is_not_a_version(string? text) => Assert.False(SemVer.TryParse(text, out _));

    [Theory]
    [InlineData("1.2.0", "1.1.9", 1)]
    [InlineData("1.2.0", "1.2.0", 0)]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.3.0", "1.3.0-rc.1", 1)]
    [InlineData("1.3.0-rc.1", "1.3.0", -1)]
    [InlineData("1.3.0-rc.1", "1.3.0-rc.2", -1)]
    [InlineData("1.3.0-rc.10", "1.3.0-rc.2", 1)]
    [InlineData("1.3.0-beta", "1.3.0-rc", -1)]
    [InlineData("1.3.0-rc", "1.3.0-rc.1", -1)]
    [InlineData("1.3.0-1", "1.3.0-alpha", -1)]
    [InlineData("1.2.9", "1.3.0-rc.1", -1)]
    public void Compares_versions_like_semantic_versioning(string a, string b, int expected)
    {
        Assert.True(SemVer.TryParse(a, out var left));
        Assert.True(SemVer.TryParse(b, out var right));
        Assert.Equal(expected, Math.Sign(left.CompareTo(right)));
        Assert.Equal(-expected, Math.Sign(right.CompareTo(left)));
    }

    [Fact]
    public void Prints_back_the_version_without_the_v_or_the_build_metadata()
    {
        SemVer.TryParse("v1.3.0-rc.1+abc", out var v);

        Assert.Equal("1.3.0-rc.1", v.ToString());
    }
}

public class UpdateServiceTests
{
    private static string Release(string tag, bool draft = false, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","name":"Mira {{tag}}","html_url":"https://github.com/Stellatinus/Mira/releases/tag/{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"published_at":"2026-11-13T10:00:00Z"}""";

    private static UpdateService Make(string current, Func<HttpResponseMessage> latest, out FakeHandler handler)
    {
        handler = new Router().On("releases/latest", latest).Handler();
        return new UpdateService(Gh.Api(handler), current);
    }

    [Fact]
    public async Task A_newer_release_is_reported_with_its_link_and_date()
    {
        var service = Make("1.2.0", () => Gh.Json(Release("v1.3.0")), out _);

        var info = await service.GetAsync(false, CancellationToken.None);

        Assert.True(info.Available);
        Assert.Equal(("1.2.0", "1.3.0"), (info.Current, info.Latest));
        Assert.Equal("https://github.com/Stellatinus/Mira/releases/tag/v1.3.0", info.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero), info.PublishedAt);
        Assert.Null(info.Error);
    }

    [Theory]
    [InlineData("1.2.0", "v1.2.0")]
    [InlineData("1.3.0", "v1.2.0")]
    [InlineData("1.2.0-rc.1", "v1.1.0")]
    public async Task The_same_or_an_older_release_is_not_an_update(string current, string tag)
    {
        var service = Make(current, () => Gh.Json(Release(tag)), out _);

        var info = await service.GetAsync(false, CancellationToken.None);

        Assert.False(info.Available);
        Assert.Equal(tag[1..], info.Latest);
    }

    [Fact]
    public async Task A_prerelease_in_use_is_offered_the_stable_version_that_follows_it()
    {
        var service = Make("1.3.0-rc.1", () => Gh.Json(Release("v1.3.0")), out _);

        Assert.True((await service.GetAsync(false, CancellationToken.None)).Available);
    }

    [Theory]
    [InlineData("0.0.0-dev")]
    [InlineData("1.0.0-dev")]
    public async Task A_build_from_source_never_asks_github_and_never_offers_an_update(string current)
    {
        var service = Make(current, () => Gh.Json(Release("v9.9.9")), out var handler);

        var info = await service.GetAsync(false, CancellationToken.None);

        Assert.False(info.Available);
        Assert.Null(info.Latest);
        Assert.Null(info.Error);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task The_request_is_anonymous_and_asks_for_the_latest_release_of_the_project()
    {
        var service = Make("1.2.0", () => Gh.Json(Release("v1.2.0")), out var handler);

        await service.GetAsync(false, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("repos/Stellatinus/Mira/releases/latest", sent.Path);
        Assert.Null(sent.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "github-http")]
    [InlineData(HttpStatusCode.Forbidden, "rate-limited")]
    [InlineData(HttpStatusCode.InternalServerError, "github-http")]
    public async Task A_github_error_is_reported_and_not_cached(HttpStatusCode status, string code)
    {
        var failing = true;
        var service = Make("1.2.0", () => failing ? Gh.Status(status) : Gh.Json(Release("v1.3.0")), out var handler);

        var first = await service.GetAsync(false, CancellationToken.None);
        Assert.Equal(code, first.Error!.Code);
        Assert.False(first.Available);

        failing = false;
        var second = await service.GetAsync(false, CancellationToken.None);
        Assert.True(second.Available);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task A_network_failure_is_reported_as_an_error_not_thrown()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("giù"));
        var service = new UpdateService(Gh.Api(handler), "1.2.0");

        var info = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal("network", info.Error!.Code);
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("1.2")]
    public async Task A_tag_that_is_not_a_version_is_reported_as_unreadable(string tag)
    {
        var service = Make("1.2.0", () => Gh.Json(Release(tag)), out _);

        var info = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal("update-unreadable", info.Error!.Code);
        Assert.False(info.Available);
    }

    [Fact]
    public async Task A_draft_is_never_offered()
    {
        var service = Make("1.2.0", () => Gh.Json(Release("v2.0.0", draft: true)), out _);

        var info = await service.GetAsync(false, CancellationToken.None);

        Assert.False(info.Available);
        Assert.Equal("update-unreadable", info.Error!.Code);
    }

    [Fact]
    public async Task The_answer_is_cached_for_hours_and_a_forced_check_right_after_is_still_cached()
    {
        var service = Make("1.2.0", () => Gh.Json(Release("v1.3.0")), out var handler);

        await service.GetAsync(false, CancellationToken.None);
        await service.GetAsync(false, CancellationToken.None);
        await service.GetAsync(force: true, CancellationToken.None);

        Assert.Equal(1, handler.Count);
    }
}

public sealed class UpdateEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var github = new Router().On("releases/latest", () => Gh.Json("""{"tag_name":"v1.3.0","html_url":"https://github.com/Stellatinus/Mira/releases/tag/v1.3.0","draft":false,"prerelease":false,"published_at":"2026-11-13T10:00:00Z"}""")).Handler();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        builder.Services.AddSingleton(Gh.Api(github));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<DashboardService>();
        builder.Services.AddSingleton<IssueService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<SectionService>();
        builder.Services.AddSingleton(new FollowStore(Path.Combine(Path.GetTempPath(), "mira-follow-" + Guid.NewGuid().ToString("N"))));
        builder.Services.AddSingleton<ReleaseService>();
        builder.Services.AddSingleton(provider => new UpdateService(provider.GetRequiredService<Mira.Server.GitHub.GitHubApi>(), "1.2.0"));
        builder.Services.AddSingleton(new SeenStore(Path.Combine(Path.GetTempPath(), "mira-update-" + Guid.NewGuid().ToString("N"))));
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

    [Fact]
    public async Task The_endpoint_returns_the_update_information_as_json()
    {
        var response = await _client.GetAsync("/api/update");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("1.2.0", json.RootElement.GetProperty("current").GetString());
        Assert.Equal("1.3.0", json.RootElement.GetProperty("latest").GetString());
        Assert.True(json.RootElement.GetProperty("available").GetBoolean());
    }
}
