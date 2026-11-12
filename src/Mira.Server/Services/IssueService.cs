using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>
/// Le liste complete di issue per le viste «Issue» e «Non assegnate»: a differenza della dashboard, che si
/// ferma alle 100 più recenti, qui si scaricano tutte le pagine dei risultati (la ricerca di GitHub arriva
/// a 1000). Si fa alla richiesta, quando si apre la vista, e il risultato resta in cache per qualche minuto.
/// </summary>
public sealed class IssueService(ISecretStore store, GitHubApi api)
{
    private const int PageSize = 100;
    private const int MaxPages = 10;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ForcedRefreshMinAge = TimeSpan.FromSeconds(30);

    /// <summary>
    /// «issues»: le issue aperte che ti riguardano (aperte da te o assegnate a te).
    /// «unassigned»: issue aperte senza assegnatario nei repository tuoi e delle tue organizzazioni.
    /// </summary>
    public static readonly string[] Views = ["issues", "unassigned"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ExtraSnapshot> _cache = new();

    private sealed record AccountResult(List<DashboardItem> Items, List<DashboardError> Errors);

    public void Invalidate()
    {
        lock (_cache) _cache.Clear();
    }

    public async Task<ExtraSnapshot> GetAsync(string view, bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var maxAge = force ? ForcedRefreshMinAge : CacheTtl;
            lock (_cache)
            {
                if (_cache.TryGetValue(view, out var cached) && DateTimeOffset.UtcNow - cached.GeneratedAt < maxAge)
                    return cached;
            }

            var built = await BuildAsync(view, ct);
            lock (_cache) _cache[view] = built;
            return built;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExtraSnapshot> BuildAsync(string view, CancellationToken ct)
    {
        var tokens = await store.LoadAllAsync(ct);
        var perAccount = await Task.WhenAll(tokens.Select(pair => LoadAccountAsync(view, pair.Key, pair.Value, ct)));

        // Lo stesso elemento può arrivare da più account o da più ricerche: se ne tiene uno solo.
        var unique = new Dictionary<string, DashboardItem>();
        foreach (var item in perAccount.SelectMany(a => a.Items))
            unique.TryAdd(item.Url, item);

        return new ExtraSnapshot(
            DateTimeOffset.UtcNow,
            unique.Values.OrderByDescending(i => i.UpdatedAt).ToList(),
            perAccount.SelectMany(a => a.Errors).ToList());
    }

    private async Task<AccountResult> LoadAccountAsync(string view, string login, string token, CancellationToken ct)
    {
        var items = new List<DashboardItem>();
        var errors = new List<DashboardError>();

        var queries = view == "issues"
            ? new List<string>
            {
                $"is:issue is:open author:{login} archived:false",
                $"is:issue is:open assignee:{login} archived:false",
            }
            : await UnassignedQueriesAsync(login, token, errors, ct);

        // Le ricerche di un account vanno una dopo l'altra, per non superare il limite di GitHub.
        foreach (var query in queries)
        {
            var (found, error) = await SearchAllAsync(login, token, view, query, ct);
            items.AddRange(found);
            if (error is not null) errors.Add(new DashboardError(login, view, error));
        }

        return new AccountResult(items, errors);
    }

    /// <summary>Una ricerca per il proprio utente e una per ogni organizzazione a cui l'account appartiene.</summary>
    private async Task<List<string>> UnassignedQueriesAsync(
        string login, string token, List<DashboardError> errors, CancellationToken ct)
    {
        var queries = new List<string> { $"is:issue is:open no:assignee archived:false user:{login}" };

        var orgs = await api.GetAsync<List<GhOrg>>(token, "user/orgs?per_page=100", ct);
        if (orgs.Ok)
            queries.AddRange((orgs.Data ?? new List<GhOrg>()).Select(o => $"is:issue is:open no:assignee archived:false org:{o.Login}"));
        else
            errors.Add(new DashboardError(login, "unassigned", orgs.Error ?? new ErrorInfo("unknown")));

        return queries;
    }

    /// <summary>Tutte le pagine di una ricerca. Se una pagina fallisce si tiene quanto già scaricato e si riporta l'errore.</summary>
    private async Task<(List<DashboardItem> Items, ErrorInfo? Error)> SearchAllAsync(
        string login, string token, string section, string query, CancellationToken ct)
    {
        var items = new List<DashboardItem>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"search/issues?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page={PageSize}&page={page}";
            var response = await api.GetAsync<GhSearchResult>(token, path, ct);
            if (!response.Ok)
                return (items, response.Error ?? new ErrorInfo("unknown"));

            var batch = response.Data?.Items ?? new List<GhIssue>();
            items.AddRange(batch.Select(issue => DashboardService.ToItem(login, section, issue)));

            if (batch.Count < PageSize || items.Count >= (response.Data?.TotalCount ?? 0)) break;
        }

        return (items, null);
    }
}
