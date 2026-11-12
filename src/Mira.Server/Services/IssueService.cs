using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>
/// La lista completa delle issue per la vista «Issue»: a differenza della dashboard, che si ferma alle 100 più
/// recenti, qui si scaricano tutte le pagine dei risultati (la ricerca di GitHub arriva a 1000 per ricerca).
/// Si fa alla richiesta, quando si apre la vista, e il risultato resta in cache per qualche minuto.
///
/// L'insieme è questo, senza doppioni:
///  - **tutte le issue aperte** nei repository dei tuoi account e delle organizzazioni a cui appartengono, di
///    chiunque siano e comunque siano assegnate;
///  - più quelle **aperte da te** e quelle **assegnate a te** anche altrove (repository di altri).
/// In questo modo «assegnate a te», «assegnate ad altri» e «non assegnate» (che sono filtri della vista) si
/// dividono l'insieme senza resto.
/// </summary>
public sealed class IssueService(ISecretStore store, GitHubApi api)
{
    private const int PageSize = 100;
    private const int MaxPages = 10;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ForcedRefreshMinAge = TimeSpan.FromSeconds(30);

    public const string IssuesView = "issues";
    public static readonly string[] Views = [IssuesView];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ExtraSnapshot> _cache = new();

    private sealed record AccountPlan(string Login, string Token, List<string> Queries);
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

            var built = await BuildAsync(ct);
            lock (_cache) _cache[view] = built;
            return built;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExtraSnapshot> BuildAsync(CancellationToken ct)
    {
        var tokens = await store.LoadAllAsync(ct);
        var errors = new List<DashboardError>();

        // Le organizzazioni di ogni account, in parallelo.
        var orgsPerAccount = await Task.WhenAll(tokens.Select(async pair =>
            (Login: pair.Key, Token: pair.Value, Orgs: await api.GetAsync<List<GhOrg>>(pair.Value, "user/orgs?per_page=100", ct))));

        // Ogni proprietario (un utente o un'organizzazione) si cerca una volta sola, con il primo account che lo vede:
        // gli account che condividono un'organizzazione non la scaricano due volte.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<AccountPlan>();
        foreach (var (login, token, orgs) in orgsPerAccount)
        {
            var queries = new List<string>();

            void AddOwner(string qualifier, string name)
            {
                if (claimed.Add(name))
                    queries.Add($"is:issue is:open archived:false {qualifier}:{name}");
            }

            AddOwner("user", login);
            if (orgs.Ok)
            {
                foreach (var org in orgs.Data ?? new List<GhOrg>())
                    AddOwner("org", org.Login);
            }
            else
            {
                errors.Add(new DashboardError(login, "issues", orgs.Error ?? new ErrorInfo("unknown")));
            }

            // Quelle che ti riguardano anche fuori dai tuoi repository.
            queries.Add($"is:issue is:open archived:false author:{login}");
            queries.Add($"is:issue is:open archived:false assignee:{login}");
            plans.Add(new AccountPlan(login, token, queries));
        }

        // Gli account vanno in parallelo, le ricerche di uno stesso account una dopo l'altra, per non superare il limite di GitHub.
        var results = await Task.WhenAll(plans.Select(plan => RunAsync(plan, ct)));

        // Lo stesso elemento può arrivare da più ricerche o da più account: se ne tiene uno solo.
        var unique = new Dictionary<string, DashboardItem>();
        foreach (var item in results.SelectMany(r => r.Items))
            unique.TryAdd(item.Url, item);
        errors.AddRange(results.SelectMany(r => r.Errors));

        return new ExtraSnapshot(
            DateTimeOffset.UtcNow,
            unique.Values.OrderByDescending(i => i.UpdatedAt).ToList(),
            errors);
    }

    private async Task<AccountResult> RunAsync(AccountPlan plan, CancellationToken ct)
    {
        var items = new List<DashboardItem>();
        var errors = new List<DashboardError>();

        foreach (var query in plan.Queries)
        {
            var (found, error) = await SearchAllAsync(plan.Login, plan.Token, IssuesView, query, ct);
            items.AddRange(found);
            if (error is not null) errors.Add(new DashboardError(plan.Login, IssuesView, error));
        }

        return new AccountResult(items, errors);
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
