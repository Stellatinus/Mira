using System.Security.Cryptography;
using System.Text;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Tests;

/// <summary>Questi test usano variabili d'ambiente (MIRA_MASTER_KEY, MIRA_DATA_DIR): non possono girare in parallelo.</summary>
[Collection("environment")]
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mira-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldKey = Environment.GetEnvironmentVariable("MIRA_MASTER_KEY");
    private readonly string? _oldDir = Environment.GetEnvironmentVariable("MIRA_DATA_DIR");

    public SecretStoreTests()
    {
        Environment.SetEnvironmentVariable("MIRA_MASTER_KEY", null);
        Environment.SetEnvironmentVariable("MIRA_DATA_DIR", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MIRA_MASTER_KEY", _oldKey);
        Environment.SetEnvironmentVariable("MIRA_DATA_DIR", _oldDir);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task A_saved_token_survives_a_new_instance()
    {
        await new EncryptedFileSecretStore(_dir).SaveAsync("alice", "ghp_first");

        var loaded = await new EncryptedFileSecretStore(_dir).LoadAllAsync();

        Assert.Equal("ghp_first", loaded["alice"]);
    }

    [Fact]
    public async Task The_token_never_appears_in_clear_in_the_file()
    {
        await new EncryptedFileSecretStore(_dir).SaveAsync("alice", "ghp_VISIBLE_SECRET");

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_dir, "tokens.enc"));

        Assert.DoesNotContain("ghp_VISIBLE_SECRET", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("alice", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Two_saves_of_the_same_data_give_different_files_because_the_nonce_is_fresh()
    {
        var store = new EncryptedFileSecretStore(_dir);
        await store.SaveAsync("alice", "ghp_x");
        var first = await File.ReadAllBytesAsync(Path.Combine(_dir, "tokens.enc"));
        await store.SaveAsync("alice", "ghp_x");
        var second = await File.ReadAllBytesAsync(Path.Combine(_dir, "tokens.enc"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Logins_are_case_insensitive_and_a_second_save_replaces_the_first()
    {
        var store = new EncryptedFileSecretStore(_dir);
        await store.SaveAsync("Alice", "old");
        await store.SaveAsync("alice", "new");

        var loaded = await store.LoadAllAsync();

        Assert.Equal("new", Assert.Single(loaded).Value);
        Assert.Equal("new", loaded["ALICE"]);
    }

    [Fact]
    public async Task Removing_a_token_leaves_the_others_and_removing_an_unknown_one_is_harmless()
    {
        var store = new EncryptedFileSecretStore(_dir);
        await store.SaveAsync("alice", "a");
        await store.SaveAsync("bob", "b");

        await store.RemoveAsync("alice");
        await store.RemoveAsync("nessuno");

        Assert.Equal(new[] { "bob" }, (await store.LoadAllAsync()).Keys);
    }

    [Fact]
    public async Task With_no_file_yet_the_store_is_empty_and_creates_nothing()
    {
        var loaded = await new EncryptedFileSecretStore(_dir).LoadAllAsync();

        Assert.Empty(loaded);
        Assert.False(File.Exists(Path.Combine(_dir, "master.key")));
    }

    [Fact]
    public async Task Files_are_readable_only_by_the_user_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;
        await new EncryptedFileSecretStore(_dir).SaveAsync("alice", "x");

        var owner = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(owner, File.GetUnixFileMode(Path.Combine(_dir, "tokens.enc")));
        Assert.Equal(owner, File.GetUnixFileMode(Path.Combine(_dir, "master.key")));
        Assert.Equal(owner | UnixFileMode.UserExecute, File.GetUnixFileMode(_dir));
    }

    [Fact]
    public async Task The_key_can_come_from_the_environment_and_then_no_key_file_is_written()
    {
        Environment.SetEnvironmentVariable("MIRA_MASTER_KEY", NewKey());
        var store = new EncryptedFileSecretStore(_dir);
        await store.SaveAsync("alice", "ghp_env");

        Assert.Equal("ghp_env", (await store.LoadAllAsync())["alice"]);
        Assert.False(File.Exists(Path.Combine(_dir, "master.key")));
    }

    [Fact]
    public async Task A_key_of_the_wrong_length_is_refused()
    {
        Environment.SetEnvironmentVariable("MIRA_MASTER_KEY", Convert.ToBase64String(new byte[16]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new EncryptedFileSecretStore(_dir).SaveAsync("a", "b"));
    }

    [Fact]
    public async Task A_different_key_gives_a_clear_error_and_does_not_damage_the_file()
    {
        Environment.SetEnvironmentVariable("MIRA_MASTER_KEY", NewKey());
        await new EncryptedFileSecretStore(_dir).SaveAsync("alice", "x");
        var before = await File.ReadAllBytesAsync(Path.Combine(_dir, "tokens.enc"));

        Environment.SetEnvironmentVariable("MIRA_MASTER_KEY", NewKey());
        var error = await Assert.ThrowsAsync<MiraException>(() => new EncryptedFileSecretStore(_dir).LoadAllAsync());

        Assert.Equal("secrets-key-mismatch", error.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_dir, "tokens.enc")));
    }

    [Fact]
    public async Task A_truncated_file_is_reported_as_corrupt()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(Path.Combine(_dir, "tokens.enc"), new byte[10]);

        var error = await Assert.ThrowsAsync<MiraException>(() => new EncryptedFileSecretStore(_dir).LoadAllAsync());

        Assert.Equal("secrets-corrupt", error.Code);
    }

    [Fact]
    public async Task A_tampered_file_does_not_decrypt()
    {
        var store = new EncryptedFileSecretStore(_dir);
        await store.SaveAsync("alice", "x");
        var path = Path.Combine(_dir, "tokens.enc");
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        var error = await Assert.ThrowsAsync<MiraException>(() => store.LoadAllAsync());

        Assert.Equal("secrets-key-mismatch", error.Code);
    }

    [Fact]
    public void The_data_directory_can_be_chosen_with_an_environment_variable()
    {
        Environment.SetEnvironmentVariable("MIRA_DATA_DIR", _dir);

        Assert.Equal(Path.GetFullPath(_dir), DataDirectory.Resolve());
    }

    [Fact]
    public void Without_the_variable_the_data_directory_is_a_mira_folder_in_the_user_profile()
    {
        var resolved = DataDirectory.Resolve();

        Assert.Equal("Mira", Path.GetFileName(resolved));
        Assert.True(Path.IsPathRooted(resolved));
    }
}

public class AppVersionTests
{
    [Fact]
    public void The_version_has_no_build_metadata_suffix() => Assert.DoesNotContain('+', AppVersion.Current);

    [Fact]
    public void A_build_from_source_is_marked_as_a_development_version() => Assert.Equal("0.0.0-dev", AppVersion.Current);
}
