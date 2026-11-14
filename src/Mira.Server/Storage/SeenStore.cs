using System.Text.Json;
using Mira.Server.Models;

namespace Mira.Server.Storage;

/// <summary>
/// Un elemento che l'utente ha segnato come «visto» o rimandato. La chiave è l'indirizzo dell'elemento
/// (lo stesso elemento può comparire in più sezioni e per più account: si nasconde ovunque).
/// UpdatedAt è l'ultimo aggiornamento che l'utente ha visto: se l'elemento cambia dopo, riappare da solo.
/// SnoozeUntil, se c'è, è il momento in cui un elemento rimandato torna da vedere comunque.
/// SavedAt serve solo a ripulire le voci vecchie.
/// </summary>
public sealed record SeenEntry(string Url, DateTimeOffset UpdatedAt, DateTimeOffset? SnoozeUntil, DateTimeOffset SavedAt);

/// <summary>
/// Lo stato «visto» in locale, in un file della cartella di configurazione (accanto ai token ma non cifrato:
/// contiene solo indirizzi e date, nessun segreto). Non tocca mai GitHub. Le voci non toccate da più di
/// 180 giorni si cancellano da sole, così il file non cresce per sempre.
/// </summary>
public sealed class SeenStore
{
    public const int MaxEntries = 20_000;
    public const int MaxUrlLength = 500;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(180);

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SeenStore(string directory)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _path = Path.Combine(directory, "seen.json");
    }

    /// <summary>Solo indirizzi di GitHub, di lunghezza ragionevole: l'API è locale, ma non si salva qualsiasi cosa.</summary>
    public static bool IsValidUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.Length <= MaxUrlLength &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<SeenEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return (await ReadAsync(ct)).Values.OrderBy(e => e.Url, StringComparer.Ordinal).ToList(); }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(string url, DateTimeOffset updatedAt, DateTimeOffset? snoozeUntil, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            var now = DateTimeOffset.UtcNow;
            all[url] = new SeenEntry(url, updatedAt, snoozeUntil, now);
            Prune(all, now);
            await WriteAsync(all, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Toglie una voce (torna da vedere); senza indirizzo le toglie tutte.</summary>
    public async Task RemoveAsync(string? url, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            var changed = url is null ? all.Count > 0 : all.Remove(url);
            if (url is null) all.Clear();
            if (changed) await WriteAsync(all, ct);
        }
        finally { _gate.Release(); }
    }

    private static void Prune(Dictionary<string, SeenEntry> all, DateTimeOffset now)
    {
        foreach (var key in all.Where(p => now - p.Value.SavedAt > Retention).Select(p => p.Key).ToList())
            all.Remove(key);

        // Oltre il limite si perdono le voci più vecchie, non quelle appena salvate.
        if (all.Count > MaxEntries)
        {
            foreach (var key in all.OrderBy(p => p.Value.SavedAt).Take(all.Count - MaxEntries).Select(p => p.Key).ToList())
                all.Remove(key);
        }
    }

    private async Task<Dictionary<string, SeenEntry>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, SeenEntry>(StringComparer.Ordinal);

        try
        {
            await using var stream = File.OpenRead(_path);
            var list = await JsonSerializer.DeserializeAsync<List<SeenEntry>>(stream, cancellationToken: ct) ?? [];
            return list.Where(e => IsValidUrl(e.Url)).GroupBy(e => e.Url).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // File rovinato: non è un segreto e si ricostruisce, ma non si butta senza lasciarne traccia.
            File.Move(_path, _path + ".rotto", overwrite: true);
            return new Dictionary<string, SeenEntry>(StringComparer.Ordinal);
        }
    }

    private async Task WriteAsync(Dictionary<string, SeenEntry> all, CancellationToken ct)
    {
        var temp = _path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using (var stream = new FileStream(temp, options))
            await JsonSerializer.SerializeAsync(stream, all.Values.OrderBy(e => e.Url, StringComparer.Ordinal).ToList(), cancellationToken: ct);

        File.Move(temp, _path, overwrite: true);
    }
}
