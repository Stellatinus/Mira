using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Services;
using Mira.Server.Storage;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public sealed class FollowStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mira-follow-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("Stellatinus/Mira", true)]
    [InlineData("a/b", true)]
    [InlineData("my-org/my.repo_2", true)]
    [InlineData("noslash", false)]
    [InlineData("a/b/c", false)]
    [InlineData("../x", false)]
    [InlineData("a/..", false)]
    [InlineData("a/b c", false)]
    [InlineData("a/b\"c", false)]
    [InlineData("a/b{", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_plain_owner_slash_name_is_valid(string? repo, bool valid) => Assert.Equal(valid, FollowStore.IsValidRepo(repo));

    [Fact]
    public async Task Following_adds_and_unfollowing_mutes_and_names_are_lowercase()
    {
        var store = new FollowStore(_dir);

        await store.SetAsync("Acme/App", follow: true);
        await store.SetAsync("Acme/Other", follow: false);
        var list = await store.GetAsync();

        Assert.Equal(new[] { "acme/app" }, list.Added);
        Assert.Equal(new[] { "acme/other" }, list.Muted);
    }

    [Fact]
    public async Task Changing_your_mind_moves_the_repository_between_the_lists()
    {
        var store = new FollowStore(_dir);
        await store.SetAsync("acme/app", false);
        await store.SetAsync("acme/app", true);
        var list = await store.GetAsync();
        Assert.Equal(new[] { "acme/app" }, list.Added);
        Assert.Empty(list.Muted);

        await store.SetAsync("ACME/APP", false);
        list = await new FollowStore(_dir).GetAsync();
        Assert.Empty(list.Added);
        Assert.Equal(new[] { "acme/app" }, list.Muted);
    }

    [Fact]
    public async Task A_damaged_file_is_set_aside_and_the_store_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "follow.json"), "non è json");

        var list = await new FollowStore(_dir).GetAsync();

        Assert.Empty(list.Added);
        Assert.True(File.Exists(Path.Combine(_dir, "follow.json.rotto")));
    }

    [Fact]
    public async Task Names_that_are_not_valid_are_ignored_when_reading()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "follow.json"), """{"Added":["ok/repo","../evil","a\"b/c"],"Muted":["x/y","bad"]}""");

        var list = await new FollowStore(_dir).GetAsync();

        Assert.Equal(new[] { "ok/repo" }, list.Added);
        Assert.Equal(new[] { "x/y" }, list.Muted);
    }
}

public sealed class ReleaseServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mira-release-" + Guid.NewGuid().ToString("N"));
    private static readonly string Recent = DateTimeOffset.UtcNow.AddDays(-2).ToString("o");
    private static readonly string Old = DateTimeOffset.UtcNow.AddDays(-90).ToString("o");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string Repo(string name, string? tag, string? published, bool isPrivate = false, string? releaseName = null, string? body = null) =>
        "{\"nameWithOwner\":\"" + name + "\",\"isPrivate\":" + (isPrivate ? "true" : "false") + ",\"latestRelease\":" +
        (tag is null ? "null" : "{\"name\":" + (releaseName is null ? "null" : "\"" + releaseName + "\"") + ",\"tagName\":\"" + tag + "\",\"url\":\"https://github.com/" + name + "/releases/tag/" + tag +
            "\",\"publishedAt\":" + (published is null ? "null" : "\"" + published + "\"") + ",\"description\":" + (body is null ? "null" : System.Text.Json.JsonSerializer.Serialize(body)) + "}") + "}";

    private static string Starred(params string[] repos) =>
        "{\"data\":{\"viewer\":{\"starredRepositories\":{\"nodes\":[" + string.Join(",", repos) + "]}}}}";

    private static string Aliased(params (string Alias, string? Repo)[] repos) =>
        "{\"data\":{" + string.Join(",", repos.Select(r => $"\"{r.Alias}\":{r.Repo ?? "null"}")) + "}}";

    private ReleaseService Make(Router router, out FakeHandler handler, FollowStore? follow = null, params (string, string)[] accounts)
    {
        handler = router.Handler();
        return new ReleaseService(new InMemorySecretStore(accounts.Length == 0 ? [("alice", "t-alice")] : accounts), Gh.Api(handler), follow ?? new FollowStore(_dir));
    }

    private static Router Github(string starredJson) => new Router().OnGraphQl("starredRepositories", () => Gh.Json(starredJson));

    [Fact]
    public async Task The_latest_release_of_every_starred_repository_becomes_an_item()
    {
        var service = Make(Github(Starred(Repo("acme/app", "v1.2.0", Recent, releaseName: "Acme 1.2", body: "Novità"), Repo("acme/lib", "v0.3.0", Recent))), out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        var item = Assert.Single(snapshot.Items, i => i.Repo == "acme/app");
        Assert.Equal(("releases", "release", "Acme 1.2"), (item.Section, item.Type, item.Title));
        Assert.Equal("https://github.com/acme/app/releases/tag/v1.2.0", item.Url);
        Assert.Equal("Novità", item.Description);
        Assert.Equal("v0.3.0", Assert.Single(snapshot.Items, i => i.Repo == "acme/lib").Title);
    }

    [Fact]
    public async Task Repositories_without_a_release_or_without_a_date_give_nothing()
    {
        var service = Make(Github(Starred(Repo("acme/none", null, null), Repo("acme/nodate", "v1", published: null))), out _);

        Assert.Empty((await service.GetAsync(false, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Only_releases_from_the_last_30_days_are_shown_newest_first()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-20).ToString("o");
        var service = Make(Github(Starred(Repo("acme/old", "v1", Old), Repo("acme/mid", "v1", older), Repo("acme/new", "v1", Recent))), out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(new[] { "acme/new", "acme/mid" }, snapshot.Items.Select(i => i.Repo));
    }

    [Fact]
    public async Task A_muted_repository_is_not_shown_even_if_starred()
    {
        var follow = new FollowStore(_dir);
        await follow.SetAsync("Acme/App", follow: false);
        var service = Make(Github(Starred(Repo("acme/app", "v1", Recent), Repo("acme/lib", "v1", Recent))), out _, follow);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal("acme/lib", Assert.Single(snapshot.Items).Repo);
    }

    [Fact]
    public async Task A_repository_added_by_hand_is_read_with_an_aliased_query_per_repository()
    {
        var follow = new FollowStore(_dir);
        await follow.SetAsync("other/tool", follow: true);
        var router = Github(Starred())
            .OnGraphQl("repository(owner", () => Gh.Json(Aliased(("r0", Repo("other/tool", "v2.0.0", Recent)))));
        var service = Make(router, out var handler, follow);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal("other/tool", Assert.Single(snapshot.Items).Repo);
        var query = Assert.Single(handler.Requests, r => r.Body?.Contains("repository(owner") == true).Body!;
        // JsonContent escapa le virgolette come \u0022.
        Assert.Contains("r0: repository(owner: \\u0022other\\u0022, name: \\u0022tool\\u0022)", query);
    }

    [Fact]
    public async Task A_repository_that_one_account_cannot_see_is_not_an_error()
    {
        var follow = new FollowStore(_dir);
        await follow.SetAsync("secret/repo", follow: true);
        var router = Github(Starred())
            .OnGraphQl("repository(owner", () => Gh.Json("""{"data":{"r0":null},"errors":[{"type":"NOT_FOUND","message":"Could not resolve"}]}"""));
        var service = Make(router, out _, follow);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Empty(snapshot.Items);
        Assert.Empty(snapshot.Errors);
    }

    [Fact]
    public async Task The_same_release_seen_by_two_accounts_is_listed_once()
    {
        var service = Make(Github(Starred(Repo("acme/app", "v1", Recent))), out _, accounts: [("alice", "t1"), ("bob", "t2")]);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Single(snapshot.Items);
    }

    [Fact]
    public async Task A_failing_account_is_reported_while_the_others_still_load()
    {
        var router = new Router().OnGraphQl("starredRepositories", () => Gh.Json(Starred(Repo("acme/app", "v1", Recent))));
        // L'account «bob» usa un altro token: la richiesta fallisce solo per lui.
        var handler = new FakeHandler(request => request.Headers.Authorization!.Parameter == "t2"
            ? Gh.Status(HttpStatusCode.Forbidden)
            : router.Handle(request));
        var service = new ReleaseService(new InMemorySecretStore(("alice", "t1"), ("bob", "t2")), Gh.Api(handler), new FollowStore(_dir));

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Single(snapshot.Items);
        var error = Assert.Single(snapshot.Errors);
        Assert.Equal(("bob", "releases", "rate-limited"), (error.Account, error.Section, error.Error.Code));
    }

    [Fact]
    public async Task Graphql_errors_without_data_are_reported()
    {
        var service = Make(Github("""{"data":null,"errors":[{"type":"RATE_LIMITED","message":"x"}]}"""), out _);

        var error = Assert.Single((await service.GetAsync(false, CancellationToken.None)).Errors);

        Assert.Equal("rate-limited", error.Error.Code);
    }

    [Fact]
    public async Task The_result_is_cached_and_invalidate_clears_it()
    {
        var service = Make(Github(Starred(Repo("acme/app", "v1", Recent))), out var handler);

        await service.GetAsync(false, CancellationToken.None);
        await service.GetAsync(false, CancellationToken.None);
        Assert.Equal(1, handler.Count);

        service.Invalidate();
        await service.GetAsync(false, CancellationToken.None);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task A_long_release_note_is_cut_with_an_ellipsis()
    {
        var service = Make(Github(Starred(Repo("acme/app", "v1", Recent, body: new string('x', 900)))), out _);

        var item = Assert.Single((await service.GetAsync(false, CancellationToken.None)).Items);

        Assert.Equal(501, item.Description!.Length);
        Assert.EndsWith("…", item.Description);
    }
}
