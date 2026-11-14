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

public class NotificationServiceTests
{
    private static NotificationService Make(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHandler handler, params (string, string)[] accounts)
    {
        handler = new FakeHandler(respond);
        return new NotificationService(new InMemorySecretStore(accounts.Length == 0 ? [("alice", "t-alice")] : accounts), Gh.Api(handler));
    }

    [Theory]
    [InlineData("12345", true)]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData("12a", false)]
    [InlineData("../../user", false)]
    [InlineData("1/subscription", false)]
    [InlineData("123456789012345678901", false)]
    [InlineData(null, false)]
    public void A_thread_id_must_be_a_plain_number(string? id, bool valid) => Assert.Equal(valid, NotificationService.IsValidThreadId(id));

    [Theory]
    [InlineData("Stellatinus", true)]
    [InlineData("my.repo-name_2", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("a/b", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void An_owner_or_repository_name_must_be_a_plain_name(string? part, bool valid) => Assert.Equal(valid, NotificationService.IsValidRepoPart(part));

    [Fact]
    public async Task Marking_a_thread_read_patches_it_with_the_token_of_its_account()
    {
        var service = Make(_ => Gh.Status(HttpStatusCode.ResetContent), out var handler, ("alice", "t-alice"), ("bob", "t-bob"));

        var error = await service.MarkReadAsync("bob", "987", CancellationToken.None);

        Assert.Null(error);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Patch, "notifications/threads/987", "Bearer t-bob"), (sent.Method, sent.Path, sent.Authorization));
    }

    [Fact]
    public async Task Marking_a_repository_read_puts_to_its_notifications()
    {
        var service = Make(_ => Gh.Status(HttpStatusCode.ResetContent), out var handler);

        var error = await service.MarkRepositoryReadAsync("alice", "acme", "app", CancellationToken.None);

        Assert.Null(error);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Put, "repos/acme/app/notifications"), (sent.Method, sent.Path));
    }

    [Fact]
    public async Task Unsubscribing_deletes_the_thread_subscription_without_ignoring_it_forever()
    {
        var service = Make(_ => Gh.Status(HttpStatusCode.NoContent), out var handler);

        var error = await service.UnsubscribeAsync("alice", "55", CancellationToken.None);

        Assert.Null(error);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Delete, "notifications/threads/55/subscription"), (sent.Method, sent.Path));
    }

    [Fact]
    public async Task An_unknown_account_is_refused_without_calling_github()
    {
        var service = Make(_ => Gh.Status(HttpStatusCode.NoContent), out var handler);

        var error = await service.MarkReadAsync("nessuno", "1", CancellationToken.None);

        Assert.Equal("account-unknown", error!.Code);
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "rate-limited")]
    [InlineData(HttpStatusCode.NotFound, "github-http")]
    public async Task A_github_refusal_is_returned_as_a_stable_code(HttpStatusCode status, string code)
    {
        var service = Make(_ => Gh.Status(status), out _);

        var error = await service.MarkReadAsync("alice", "1", CancellationToken.None);

        Assert.Equal(code, error!.Code);
    }

    [Fact]
    public async Task A_network_failure_is_returned_as_an_error_not_thrown()
    {
        var service = Make(_ => throw new HttpRequestException("giù"), out _);

        var error = await service.MarkReadAsync("alice", "1", CancellationToken.None);

        Assert.Equal("network", error!.Code);
    }
}

/// <summary>Gli endpoint veri, con un GitHub finto dietro.</summary>
public sealed class NotificationEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private FakeHandler _github = null!;
    private HttpStatusCode _githubStatus = HttpStatusCode.ResetContent;

    public async Task InitializeAsync()
    {
        _github = new FakeHandler(_ => Gh.Status(_githubStatus));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISecretStore>(new InMemorySecretStore(("alice", "t-alice")));
        builder.Services.AddSingleton(Gh.Api(_github));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<DashboardService>();
        builder.Services.AddSingleton<IssueService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<SectionService>();
        builder.Services.AddSingleton(provider => new UpdateService(provider.GetRequiredService<Mira.Server.GitHub.GitHubApi>(), "1.2.0"));
        builder.Services.AddSingleton(new SeenStore(Path.Combine(Path.GetTempPath(), "mira-endpoints-" + Guid.NewGuid().ToString("N"))));
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
    public async Task Marking_read_answers_204_and_reaches_github()
    {
        var response = await _client.PostAsync("/api/notifications/alice/threads/42/read", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notifications/threads/42", Assert.Single(_github.Requests).Path);
    }

    [Theory]
    [InlineData("/api/notifications/alice/threads/abc/read")]
    [InlineData("/api/notifications/alice/threads/abc/unsubscribe")]
    [InlineData("/api/notifications/alice/repos/ac%20me/app/read")]
    public async Task Invalid_identifiers_answer_400_and_never_reach_github(string url)
    {
        var response = await _client.PostAsync(url, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _github.Count);
    }

    [Theory]
    [InlineData("/api/notifications/alice/repos/acme/../read")]
    [InlineData("/api/notifications/alice/repos/%2e%2e/app/read")]
    [InlineData("/api/notifications/alice/threads/1%2Fsubscription/read")]
    public async Task Path_tricks_never_reach_github(string url)
    {
        var response = await _client.PostAsync(url, null);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, response.StatusCode.ToString());
        Assert.Equal(0, _github.Count);
    }

    [Fact]
    public async Task An_unknown_account_answers_404()
    {
        var response = await _client.PostAsync("/api/notifications/nessuno/threads/1/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("account-unknown", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_github_refusal_answers_502_with_the_error_code()
    {
        _githubStatus = HttpStatusCode.Forbidden;

        var response = await _client.PostAsync("/api/notifications/alice/threads/1/read", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("rate-limited", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_post_is_accepted()
    {
        var response = await _client.GetAsync("/api/notifications/alice/threads/1/read");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
