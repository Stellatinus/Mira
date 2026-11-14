using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mira.Server.Api;
using Mira.Server.Services;
using Mira.Server.Storage;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public sealed class SeenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mira-seen-" + Guid.NewGuid().ToString("N"));
    private const string Url = "https://github.com/acme/app/issues/1";
    private static readonly DateTimeOffset Updated = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("https://github.com/acme/app/issues/1", true)]
    [InlineData("https://github.com/acme/app/pull/2", true)]
    [InlineData("https://gist.github.com/x/1", true)]
    [InlineData("http://github.com/acme/app", false)]
    [InlineData("https://evil.example/github.com", false)]
    [InlineData("https://notgithub.com/a", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_github_addresses_are_accepted(string? url, bool valid) => Assert.Equal(valid, SeenStore.IsValidUrl(url));

    [Fact]
    public void An_address_that_is_too_long_is_refused() =>
        Assert.False(SeenStore.IsValidUrl("https://github.com/" + new string('a', SeenStore.MaxUrlLength)));

    [Fact]
    public async Task An_empty_store_has_no_entries_and_creates_no_file()
    {
        var entries = await new SeenStore(_dir).GetAllAsync();

        Assert.Empty(entries);
        Assert.False(File.Exists(Path.Combine(_dir, "seen.json")));
    }

    [Fact]
    public async Task A_saved_entry_survives_a_new_instance()
    {
        var snooze = Updated.AddDays(1);
        await new SeenStore(_dir).SetAsync(Url, Updated, snooze);

        var entry = Assert.Single(await new SeenStore(_dir).GetAllAsync());

        Assert.Equal(Url, entry.Url);
        Assert.Equal(Updated, entry.UpdatedAt);
        Assert.Equal(snooze, entry.SnoozeUntil);
    }

    [Fact]
    public async Task Saving_the_same_address_again_replaces_it()
    {
        var store = new SeenStore(_dir);
        await store.SetAsync(Url, Updated, snoozeUntil: null);
        await store.SetAsync(Url, Updated.AddHours(5), null);

        Assert.Equal(Updated.AddHours(5), Assert.Single(await store.GetAllAsync()).UpdatedAt);
    }

    [Fact]
    public async Task Removing_one_entry_keeps_the_others_and_removing_without_an_address_clears_all()
    {
        var store = new SeenStore(_dir);
        await store.SetAsync(Url, Updated, null);
        await store.SetAsync("https://github.com/acme/app/issues/2", Updated, null);

        await store.RemoveAsync(Url);
        Assert.Equal("https://github.com/acme/app/issues/2", Assert.Single(await store.GetAllAsync()).Url);

        await store.RemoveAsync(null);
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Removing_an_unknown_address_is_harmless()
    {
        var store = new SeenStore(_dir);
        await store.SetAsync(Url, Updated, null);

        await store.RemoveAsync("https://github.com/acme/app/issues/99");

        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task A_damaged_file_is_set_aside_and_the_store_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "seen.json"), "{ non è json");
        var store = new SeenStore(_dir);

        Assert.Empty(await store.GetAllAsync());
        Assert.True(File.Exists(Path.Combine(_dir, "seen.json.rotto")));

        await store.SetAsync(Url, Updated, null);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Entries_with_an_address_that_is_not_valid_are_ignored_when_reading()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "seen.json"),
            """[{"Url":"https://evil.example/x","UpdatedAt":"2026-11-10T10:00:00+00:00","SnoozeUntil":null,"SavedAt":"2026-11-10T10:00:00+00:00"}]""");

        Assert.Empty(await new SeenStore(_dir).GetAllAsync());
    }

    [Fact]
    public async Task Old_entries_are_pruned_when_something_new_is_saved()
    {
        Directory.CreateDirectory(_dir);
        var old = DateTimeOffset.UtcNow - SeenStore.Retention - TimeSpan.FromDays(1);
        await File.WriteAllTextAsync(Path.Combine(_dir, "seen.json"),
            $$"""[{"Url":"https://github.com/acme/app/issues/7","UpdatedAt":"{{old:o}}","SnoozeUntil":null,"SavedAt":"{{old:o}}"}]""");
        var store = new SeenStore(_dir);
        Assert.Single(await store.GetAllAsync());

        await store.SetAsync(Url, Updated, null);

        Assert.Equal(Url, Assert.Single(await store.GetAllAsync()).Url);
    }

    [Fact]
    public async Task The_file_is_readable_only_by_the_user_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;
        await new SeenStore(_dir).SetAsync(Url, Updated, null);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, "seen.json")));
    }

    [Fact]
    public async Task Concurrent_saves_do_not_lose_entries()
    {
        var store = new SeenStore(_dir);

        await Task.WhenAll(Enumerable.Range(1, 40).Select(i => store.SetAsync($"https://github.com/acme/app/issues/{i}", Updated, null)));

        Assert.Equal(40, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task The_comment_count_and_the_hidden_flag_survive_a_new_instance()
    {
        await new SeenStore(_dir).SetAsync(Url, Updated, null, comments: 7, hidden: false);

        var entry = Assert.Single(await new SeenStore(_dir).GetAllAsync());

        Assert.Equal(7, entry.Comments);
        Assert.False(entry.Hidden);
    }

    [Fact]
    public async Task An_entry_saved_before_these_fields_existed_still_hides_and_has_no_count()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "seen.json"),
            """[{"Url":"https://github.com/acme/app/issues/1","UpdatedAt":"2026-11-10T10:00:00+00:00","SnoozeUntil":null,"SavedAt":"2099-01-01T00:00:00+00:00"}]""");

        var entry = Assert.Single(await new SeenStore(_dir).GetAllAsync());

        Assert.True(entry.Hidden);
        Assert.Null(entry.Comments);
    }

    [Fact]
    public async Task Baselines_are_added_without_hiding_and_without_touching_existing_entries()
    {
        var store = new SeenStore(_dir);
        await store.SetAsync(Url, Updated, null, comments: 9, hidden: true);

        await store.AddBaselinesAsync(
        [
            new SeenBaseline(Url, Updated.AddDays(5), 100),
            new SeenBaseline("https://github.com/acme/app/issues/2", Updated, 3),
        ]);

        var entries = (await store.GetAllAsync()).ToDictionary(e => e.Url);
        Assert.Equal(2, entries.Count);
        Assert.Equal((9, true, Updated), (entries[Url].Comments, entries[Url].Hidden, entries[Url].UpdatedAt));
        var added = entries["https://github.com/acme/app/issues/2"];
        Assert.Equal((3, false, null), (added.Comments, added.Hidden, added.SnoozeUntil));
    }

    [Fact]
    public async Task Baselines_with_a_bad_address_or_a_negative_count_are_ignored()
    {
        var store = new SeenStore(_dir);

        await store.AddBaselinesAsync(
        [
            new SeenBaseline("https://evil.example/x", Updated, 1),
            new SeenBaseline("https://github.com/acme/app/issues/3", Updated, -4),
        ]);

        Assert.Empty(await store.GetAllAsync());
        Assert.False(File.Exists(Path.Combine(_dir, "seen.json")));
    }
}

public sealed class SeenEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private SeenStore _store = null!;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mira-seen-ep-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        _store = new SeenStore(_dir);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_store);
        builder.Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        builder.Services.AddSingleton(Gh.Api(new FakeHandler(_ => Gh.Status(System.Net.HttpStatusCode.OK))));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<DashboardService>();
        builder.Services.AddSingleton<IssueService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<SectionService>();
        builder.Services.AddSingleton(new FollowStore(Path.Combine(Path.GetTempPath(), "mira-follow-" + Guid.NewGuid().ToString("N"))));
        builder.Services.AddSingleton<ReleaseService>();
        builder.Services.AddSingleton(provider => new UpdateService(provider.GetRequiredService<Mira.Server.GitHub.GitHubApi>(), "1.2.0"));
        _app = builder.Build();
        _app.MapMiraApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Put_saves_the_comment_count_and_the_hidden_flag()
    {
        var response = await _client.PutAsync("/api/seen", Json(
            """{"url":"https://github.com/acme/app/issues/1","updatedAt":"2026-11-10T10:00:00Z","snoozeUntil":null,"comments":5,"hidden":false}"""));

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(await _store.GetAllAsync());
        Assert.Equal((5, false), (entry.Comments, entry.Hidden));
    }

    [Fact]
    public async Task Put_without_the_hidden_flag_hides_like_before()
    {
        await _client.PutAsync("/api/seen", Json("""{"url":"https://github.com/acme/app/issues/1","updatedAt":"2026-11-10T10:00:00Z"}"""));

        Assert.True(Assert.Single(await _store.GetAllAsync()).Hidden);
    }

    [Fact]
    public async Task Put_with_a_bad_address_answers_400()
    {
        var response = await _client.PutAsync("/api/seen", Json("""{"url":"https://evil.example/x","updatedAt":"2026-11-10T10:00:00Z"}"""));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _store.GetAllAsync());
    }

    [Fact]
    public async Task Baseline_adds_the_missing_entries()
    {
        var response = await _client.PostAsync("/api/seen/baseline", Json(
            """{"items":[{"url":"https://github.com/acme/app/issues/1","updatedAt":"2026-11-10T10:00:00Z","comments":2},{"url":"https://github.com/acme/app/issues/2","updatedAt":"2026-11-10T10:00:00Z","comments":0}]}"""));

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, (await _store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Baseline_with_an_empty_body_is_harmless()
    {
        var response = await _client.PostAsync("/api/seen/baseline", Json("{}"));

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }
}
