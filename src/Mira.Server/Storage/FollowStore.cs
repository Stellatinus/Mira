using System.Text.Json;

namespace Mira.Server.Storage;

/// <summary>
/// Quali repository l'utente segue per le release, oltre ai preferiti (che si seguono di base): quelli aggiunti a mano
/// e quelli che ha tolto. Un repository è seguito se è preferito o aggiunto, e non è tolto. I nomi sono «proprietario/nome»
/// in minuscolo. Il file <c>follow.json</c> sta nella cartella di configurazione (non contiene segreti).
/// </summary>
public sealed record FollowList(IReadOnlyList<string> Added, IReadOnlyList<string> Muted);

public sealed class FollowStore
{
    public const int MaxRepos = 200;

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FollowStore(string directory)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _path = Path.Combine(directory, "follow.json");
    }

    /// <summary>«proprietario/nome»: lettere, cifre, trattini, punti e underscore; senza «.» o «..» come parti.</summary>
    public static bool IsValidRepo(string? repo)
    {
        if (string.IsNullOrEmpty(repo) || repo.Length > 200) return false;
        var parts = repo.Split('/');
        return parts.Length == 2 && parts.All(p => p.Length is > 0 and <= 100 && p is not "." and not ".." &&
            p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
    }

    public async Task<FollowList> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadAsync(ct); }
        finally { _gate.Release(); }
    }

    /// <summary>Segue o smette di seguire un repository. Segue: lo aggiunge e lo toglie dai tolti; smette: lo toglie dagli aggiunti e lo mette tra i tolti.</summary>
    public async Task SetAsync(string repo, bool follow, CancellationToken ct = default)
    {
        var key = repo.ToLowerInvariant();
        await _gate.WaitAsync(ct);
        try
        {
            var current = await ReadAsync(ct);
            var added = current.Added.Where(r => r != key).ToList();
            var muted = current.Muted.Where(r => r != key).ToList();
            if (follow) added.Add(key); else muted.Add(key);
            // Oltre il limite si perdono le voci più vecchie, non quella appena scelta.
            if (added.Count > MaxRepos) added = added.TakeLast(MaxRepos).ToList();
            if (muted.Count > MaxRepos) muted = muted.TakeLast(MaxRepos).ToList();
            await WriteAsync(new FollowList(added, muted), ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<FollowList> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new FollowList([], []);
        try
        {
            await using var stream = File.OpenRead(_path);
            var list = await JsonSerializer.DeserializeAsync<FollowList>(stream, cancellationToken: ct);
            return new FollowList(
                (list?.Added ?? []).Where(IsValidRepo).Select(r => r.ToLowerInvariant()).Distinct().ToList(),
                (list?.Muted ?? []).Where(IsValidRepo).Select(r => r.ToLowerInvariant()).Distinct().ToList());
        }
        catch (JsonException)
        {
            File.Move(_path, _path + ".rotto", overwrite: true);
            return new FollowList([], []);
        }
    }

    private async Task WriteAsync(FollowList list, CancellationToken ct)
    {
        var temp = _path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using (var stream = new FileStream(temp, options))
            await JsonSerializer.SerializeAsync(stream, list, cancellationToken: ct);

        File.Move(temp, _path, overwrite: true);
    }
}
