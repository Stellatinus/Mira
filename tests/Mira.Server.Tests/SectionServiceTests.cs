using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mira.Server.Api;
using Mira.Server.Services;
using Mira.Server.Storage;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class SectionServiceTests
{
    private static SectionService Make(Router router, out FakeHandler handler)
    {
        handler = router.Handler();
        return new SectionService(new InMemorySecretStore(("alice", "t-alice")), Gh.Api(handler));
    }

    private static string[] Issues(int from, int count) =>
        Enumerable.Range(from, count).Select(i => Fixtures.Issue(i, $"i{i}", pr: true)).ToArray();

    [Theory]
    [InlineData("review", 2, true)]
    [InlineData("notifications", 10, true)]
    [InlineData("mine-issues", 5, true)]
    [InlineData("review", 1, false)]
    [InlineData("review", 11, false)]
    [InlineData("review", 0, false)]
    [InlineData("repos", 2, false)]
    [InlineData("issues", 2, false)]
    [InlineData("../user", 2, false)]
    [InlineData(null, 2, false)]
    public void Only_search_sections_and_notifications_can_be_paged_from_page_2_to_10(string? section, int page, bool valid) =>
        Assert.Equal(valid, SectionService.IsValidRequest(section, page));

    [Fact]
    public async Task Asks_github_for_the_requested_page_of_the_same_search_the_dashboard_uses()
    {
        var service = Make(new Router().On("search/issues", () => Gh.Json(Fixtures.Search(213, Issues(101, 100)))), out var handler);

        var (page, error) = await service.GetAsync("alice", "review", 2, CancellationToken.None);

        Assert.Null(error);
        var path = Uri.UnescapeDataString(Assert.Single(handler.Requests).Path);
        Assert.Contains("is:pr is:open review-requested:alice archived:false", path);
        Assert.Contains("per_page=100", path);
        Assert.Contains("page=2", path);
        Assert.Equal(100, page!.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(("review", "alice"), (item.Section, item.Account)));
    }

    [Theory]
    [InlineData(100, 213, true)]
    [InlineData(13, 213, false)]
    [InlineData(100, 200, false)]
    [InlineData(100, 300, true)]
    public async Task More_pages_exist_only_while_the_total_is_not_reached(int count, int total, bool hasMore)
    {
        var service = Make(new Router().On("search/issues", () => Gh.Json(Fixtures.Search(total, Issues(101, count)))), out _);

        var (page, _) = await service.GetAsync("alice", "assigned", 2, CancellationToken.None);

        Assert.Equal(hasMore, page!.HasMore);
        Assert.Equal(total, page.Total);
    }

    [Fact]
    public async Task Notifications_are_paged_with_their_own_page_size_and_have_no_total()
    {
        var fifty = Enumerable.Range(1, 50).Select(i => Fixtures.Notification(i.ToString(), $"n{i}", "Issue", null)).ToArray();
        var service = Make(new Router().On("notifications", () => Gh.Json("[" + string.Join(",", fifty) + "]")), out var handler);

        var (page, _) = await service.GetAsync("alice", "notifications", 3, CancellationToken.None);

        Assert.Equal("notifications?per_page=50&page=3", Assert.Single(handler.Requests).Path);
        Assert.Equal(50, page!.Items.Count);
        Assert.Null(page.Total);
        Assert.True(page.HasMore);
        Assert.All(page.Items, item => Assert.Equal("notifications", item.Section));
    }

    [Fact]
    public async Task An_unknown_account_is_refused_without_calling_github()
    {
        var service = Make(new Router(), out var handler);

        var (page, error) = await service.GetAsync("nessuno", "review", 2, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal("account-unknown", error!.Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task A_github_refusal_is_returned_as_an_error_and_not_cached()
    {
        var status = HttpStatusCode.Forbidden;
        var service = Make(new Router().On("search/issues", () => status == HttpStatusCode.OK ? Gh.Json(Fixtures.Search(1, Issues(1, 1))) : Gh.Status(status)), out var handler);

        var (page, error) = await service.GetAsync("alice", "review", 2, CancellationToken.None);
        Assert.Null(page);
        Assert.Equal("rate-limited", error!.Code);

        status = HttpStatusCode.OK;
        var (retry, retryError) = await service.GetAsync("alice", "review", 2, CancellationToken.None);
        Assert.Null(retryError);
        Assert.Single(retry!.Items);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task A_page_is_cached_for_a_minute_and_invalidate_clears_it()
    {
        var service = Make(new Router().On("search/issues", () => Gh.Json(Fixtures.Search(150, Issues(101, 50)))), out var handler);

        await service.GetAsync("alice", "review", 2, CancellationToken.None);
        await service.GetAsync("alice", "review", 2, CancellationToken.None);
        Assert.Equal(1, handler.Count);

        await service.GetAsync("alice", "review", 3, CancellationToken.None);
        await service.GetAsync("alice", "assigned", 2, CancellationToken.None);
        Assert.Equal(3, handler.Count);

        service.Invalidate();
        await service.GetAsync("alice", "review", 2, CancellationToken.None);
        Assert.Equal(4, handler.Count);
    }
}

public sealed class SectionEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private FakeHandler _github = null!;

    public async Task InitializeAsync()
    {
        _github = new Router().On("search/issues", () => Gh.Json(Fixtures.Search(213, Enumerable.Range(1, 100).Select(i => Fixtures.Issue(i, $"i{i}")).ToArray()))).Handler();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISecretStore>(new InMemorySecretStore(("alice", "t-alice")));
        builder.Services.AddSingleton(Gh.Api(_github));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<DashboardService>();
        builder.Services.AddSingleton<IssueService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<SectionService>();
        builder.Services.AddSingleton(new SeenStore(Path.Combine(Path.GetTempPath(), "mira-section-" + Guid.NewGuid().ToString("N"))));
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
    public async Task Returns_the_page_as_json_with_items_total_and_has_more()
    {
        var response = await _client.GetAsync("/api/section?account=alice&section=assigned&page=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(100, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(213, json.RootElement.GetProperty("total").GetInt32());
        Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Theory]
    [InlineData("/api/section?account=alice&section=assigned&page=1")]
    [InlineData("/api/section?account=alice&section=assigned&page=11")]
    [InlineData("/api/section?account=alice&section=repos&page=2")]
    [InlineData("/api/section?account=alice&section=..%2Fuser&page=2")]
    public async Task Invalid_requests_answer_400_and_never_reach_github(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _github.Count);
    }

    [Fact]
    public async Task An_unknown_account_answers_404()
    {
        var response = await _client.GetAsync("/api/section?account=nessuno&section=assigned&page=2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
