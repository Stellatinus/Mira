using Mira.Server.Storage;

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
}
