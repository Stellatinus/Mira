using Mira.Server.GitHub;
using Mira.Server.Models;

namespace Mira.Server.Services;

/// <summary>
/// Il risultato del controllo degli aggiornamenti. Latest, Url e PublishedAt mancano se non si è riusciti a leggere
/// l'ultima release; Available è vero solo se la versione pubblicata è più nuova di quella in uso.
/// </summary>
public sealed record UpdateInfo(
    string Current,
    string? Latest,
    string? Url,
    DateTimeOffset? PublishedAt,
    bool Available,
    DateTimeOffset CheckedAt,
    ErrorInfo? Error);

/// <summary>
/// Controlla se esiste una release di Mira più nuova di quella in uso, leggendo l'ultima release pubblicata del progetto
/// (<c>GET /repos/{repo}/releases/latest</c>, che salta bozze e versioni di prova). Non scarica e non installa nulla.
/// Si fa solo alla richiesta, senza token (le richieste anonime hanno un limite basso: 60 all'ora) e il risultato
/// resta in cache per qualche ora. Una build dal sorgente (<c>0.0.0-dev</c>) non ha mai aggiornamenti da proporre.
/// </summary>
public sealed class UpdateService(GitHubApi api, string currentVersion, string repository = "Stellatinus/Mira")
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan ForcedRefreshMinAge = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateInfo? _cache;

    public async Task<UpdateInfo> GetAsync(bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var maxAge = force ? ForcedRefreshMinAge : CacheTtl;
            if (_cache is not null && DateTimeOffset.UtcNow - _cache.CheckedAt < maxAge)
                return _cache;

            var info = await CheckAsync(ct);
            // Un errore non si tiene: al prossimo controllo si riprova.
            if (info.Error is null) _cache = info;
            return info;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateInfo> CheckAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Una versione di sviluppo non è una release: non c'è niente con cui confrontarla.
        if (!SemVer.TryParse(currentVersion, out var current) || currentVersion.Contains("-dev", StringComparison.Ordinal))
            return new UpdateInfo(currentVersion, null, null, null, false, now, null);

        var response = await api.GetAnonymousAsync<GhRelease>($"repos/{repository}/releases/latest", ct);
        if (!response.Ok || response.Data is null)
            return new UpdateInfo(currentVersion, null, null, null, false, now, response.Error ?? new ErrorInfo("unknown"));

        var release = response.Data;
        if (release.Draft || !SemVer.TryParse(release.TagName, out var latest))
            return new UpdateInfo(currentVersion, null, null, null, false, now, new ErrorInfo("update-unreadable"));

        return new UpdateInfo(currentVersion, latest.ToString(), release.HtmlUrl, release.PublishedAt, latest.CompareTo(current) > 0, now, null);
    }
}
