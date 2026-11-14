using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>Una pagina in più di una sezione per un account: gli elementi, il totale di GitHub (se lo dice) e se ce ne sono altri.</summary>
public sealed record SectionPage(IReadOnlyList<DashboardItem> Items, int? Total, bool HasMore);

/// <summary>
/// Le pagine di una sezione oltre la prima. La dashboard scarica solo i 100 elementi più recenti (le notifiche 50);
/// qui si legge la pagina successiva, per un solo account e una sola sezione, quando l'utente la chiede.
/// La ricerca di GitHub arriva al massimo a 1000 risultati, cioè a 10 pagine. Ogni pagina costa una richiesta
/// (la ricerca ne concede 30 al minuto) e resta in cache per un minuto.
/// </summary>
public sealed class SectionService(ISecretStore store, GitHubApi api)
{
    /// <summary>La pagina 1 è quella della dashboard: da qui si chiedono le successive.</summary>
    public const int MaxPage = 10;

    public static readonly string[] Sections = ["review", "assigned", "mine-prs", "mine-issues", "mentions", "notifications"];

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly Dictionary<string, (DateTimeOffset At, SectionPage Page)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsValidRequest(string? section, int page) =>
        section is not null && Sections.Contains(section) && page is >= 2 and <= MaxPage;

    public void Invalidate()
    {
        lock (_lock) _cache.Clear();
    }

    /// <summary>La pagina richiesta, oppure l'errore: <c>account-unknown</c> o quello di GitHub.</summary>
    public async Task<(SectionPage? Page, ErrorInfo? Error)> GetAsync(string account, string section, int page, CancellationToken ct)
    {
        var key = $"{account}|{section}|{page}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.At < CacheTtl)
                return (hit.Page, null);
        }

        var tokens = await store.LoadAllAsync(ct);
        if (!tokens.TryGetValue(account, out var token))
            return (null, new ErrorInfo("account-unknown"));

        var result = section == "notifications"
            ? await NotificationsAsync(account, token, page, ct)
            : await SearchAsync(account, token, section, page, ct);

        if (result.Page is not null)
        {
            lock (_lock) _cache[key] = (DateTimeOffset.UtcNow, result.Page);
        }

        return result;
    }

    private async Task<(SectionPage? Page, ErrorInfo? Error)> SearchAsync(string login, string token, string section, int page, CancellationToken ct)
    {
        // La stessa ricerca della dashboard, solo con un'altra pagina.
        var query = DashboardService.SearchDefinitions(login).First(d => d.Section == section).Query;
        var path = $"search/issues?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page={DashboardService.SearchPageSize}&page={page}";
        var response = await api.GetAsync<GhSearchResult>(token, path, ct);
        if (!response.Ok)
            return (null, response.Error ?? new ErrorInfo("unknown"));

        var items = (response.Data?.Items ?? new List<GhIssue>()).Select(issue => DashboardService.ToItem(login, section, issue)).ToList();
        var total = response.Data?.TotalCount;
        var loaded = (page - 1) * DashboardService.SearchPageSize + items.Count;
        return (new SectionPage(items, total, items.Count == DashboardService.SearchPageSize && (total is null || loaded < total)), null);
    }

    private async Task<(SectionPage? Page, ErrorInfo? Error)> NotificationsAsync(string login, string token, int page, CancellationToken ct)
    {
        var path = $"notifications?per_page={DashboardService.NotificationsPageSize}&page={page}";
        var response = await api.GetAsync<List<GhNotification>>(token, path, ct);
        if (!response.Ok)
            return (null, response.Error ?? new ErrorInfo("unknown"));

        var items = (response.Data ?? new List<GhNotification>()).Select(n => DashboardService.ToNotificationItem(login, n)).ToList();
        // GitHub non dà il totale delle notifiche: una pagina piena vuol dire che potrebbero essercene altre.
        return (new SectionPage(items, null, items.Count == DashboardService.NotificationsPageSize), null);
    }
}
