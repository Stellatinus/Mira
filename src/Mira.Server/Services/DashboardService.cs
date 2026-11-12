using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>
/// Raccoglie, per ogni account collegato, ciò che richiede attenzione e lo unisce in un'unica vista.
/// Il risultato resta in cache per pochi secondi per non esaurire i limiti dell'API di GitHub.
/// </summary>
public sealed class DashboardService(ISecretStore store, GitHubApi api)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ForcedRefreshMinAge = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DashboardSnapshot? _cache;

    // La Search API restituisce al massimo 100 risultati per pagina; ne chiediamo una sola
    // (i più recenti) e mostriamo a parte il totale reale. Le notifiche arrivano al massimo a 50.
    private const int SearchPageSize = 100;
    private const int NotificationsPageSize = 50;
    private const int ReposPageSize = 100;
    private const int DescriptionMaxLength = 500;

    private sealed record Part(List<DashboardItem> Items, SectionTotal? Total, DashboardError? Error);
    private sealed record AccountData(
        AccountStatus Status, List<DashboardItem> Items, List<SectionTotal> Totals, List<DashboardError> Errors);

    public void Invalidate() => _cache = null;

    public async Task<DashboardSnapshot> GetAsync(bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var maxAge = force ? ForcedRefreshMinAge : CacheTtl;
            if (_cache is not null && DateTimeOffset.UtcNow - _cache.GeneratedAt < maxAge)
                return _cache;

            _cache = await BuildAsync(ct);
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DashboardSnapshot> BuildAsync(CancellationToken ct)
    {
        var tokens = await store.LoadAllAsync(ct);
        var perAccount = await Task.WhenAll(tokens.Select(pair => LoadAccountAsync(pair.Key, pair.Value, ct)));

        return new DashboardSnapshot(
            DateTimeOffset.UtcNow,
            perAccount.Select(a => a.Status).OrderBy(s => s.Login, StringComparer.OrdinalIgnoreCase).ToList(),
            perAccount.SelectMany(a => a.Items).OrderByDescending(i => i.UpdatedAt).ToList(),
            perAccount.SelectMany(a => a.Totals).ToList(),
            perAccount.SelectMany(a => a.Errors).ToList());
    }

    private async Task<AccountData> LoadAccountAsync(string login, string token, CancellationToken ct)
    {
        var user = await api.GetAsync<GhUser>(token, "user", ct);
        var status = AccountService.BuildStatus(login, user);

        if (!user.Ok)
        {
            var errors = new List<DashboardError>();
            // Un token scaduto è già segnalato dallo stato dell'account: non è un errore a parte.
            if (user.Status != HttpStatusCode.Unauthorized)
                errors.Add(new DashboardError(login, "account", user.Error ?? new ErrorInfo("unknown")));
            return new AccountData(status, new List<DashboardItem>(), new List<SectionTotal>(), errors);
        }

        var tasks = SearchDefinitions(login)
            .Select(d => SearchAsync(login, token, d.Section, d.Query, ct))
            .ToList();
        tasks.Add(NotificationsAsync(login, token, ct));
        var repositories = RepositoriesAsync(login, token, ct);

        var parts = (await Task.WhenAll(tasks)).Concat(await repositories).ToArray();

        return new AccountData(
            status,
            parts.SelectMany(p => p.Items).ToList(),
            parts.Where(p => p.Total is not null).Select(p => p.Total!).ToList(),
            parts.Where(p => p.Error is not null).Select(p => p.Error!).ToList());
    }

    private static (string Section, string Query)[] SearchDefinitions(string login) =>
    [
        ("review", $"is:pr is:open review-requested:{login} archived:false"),
        ("assigned", $"is:open assignee:{login} archived:false"),
        ("mine-prs", $"is:pr is:open author:{login} archived:false"),
        ("mine-issues", $"is:issue is:open author:{login} archived:false"),
        ("mentions", $"is:open mentions:{login} -author:{login} archived:false"),
    ];

    private async Task<Part> SearchAsync(string login, string token, string section, string query, CancellationToken ct)
    {
        var path = $"search/issues?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page={SearchPageSize}";
        var response = await api.GetAsync<GhSearchResult>(token, path, ct);

        if (!response.Ok)
            return new Part(new List<DashboardItem>(), null, new DashboardError(login, section, response.Error ?? new ErrorInfo("unknown")));

        var items = (response.Data?.Items ?? new List<GhIssue>())
            .Select(issue => ToItem(login, section, issue))
            .ToList();
        var total = Math.Max(response.Data?.TotalCount ?? 0, items.Count);
        return new Part(items, new SectionTotal(login, section, total), null);
    }

    // Una sola richiesta GraphQL per account dà repository e preferiti, con quante PR e issue
    // aperte hanno ("pullRequests" e "issues" separati; la REST darebbe solo la somma).
    private const string RepositoriesQuery = """
        query($first: Int!) {
          viewer {
            repos: repositories(first: $first, orderBy: {field: PUSHED_AT, direction: DESC}, affiliations: [OWNER, COLLABORATOR, ORGANIZATION_MEMBER], ownerAffiliations: [OWNER, COLLABORATOR, ORGANIZATION_MEMBER]) { nodes { ...R } }
            starred: starredRepositories(first: $first, orderBy: {field: STARRED_AT, direction: DESC}) { nodes { ...R } }
          }
        }
        fragment R on Repository {
          databaseId name nameWithOwner description url isPrivate stargazerCount pushedAt updatedAt
          primaryLanguage { name }
          issues(states: OPEN) { totalCount }
          pullRequests(states: OPEN) { totalCount }
          openGraphImageUrl usesCustomOpenGraphImage
          owner { avatarUrl(size: 128) }
        }
        """;

    private async Task<Part[]> RepositoriesAsync(string login, string token, CancellationToken ct)
    {
        var response = await api.QueryAsync<GqlData>(token, RepositoriesQuery, new { first = ReposPageSize }, ct);
        var viewer = response.Data?.Data?.Viewer;

        if (viewer is null)
        {
            var error = !response.Ok ? response.Error : GraphQlError(response.Data?.Errors);
            return
            [
                new Part(new List<DashboardItem>(), null, new DashboardError(login, "repos", error ?? new ErrorInfo("unknown"))),
                new Part(new List<DashboardItem>(), null, new DashboardError(login, "starred", error ?? new ErrorInfo("unknown"))),
            ];
        }

        return [RepoPart(login, "repos", viewer.Repos), RepoPart(login, "starred", viewer.Starred)];
    }

    private static ErrorInfo GraphQlError(List<GqlError>? errors)
    {
        var first = errors?.FirstOrDefault();
        return first?.Type == "RATE_LIMITED"
            ? new ErrorInfo("rate-limited")
            : new ErrorInfo("github-graphql", first?.Message);
    }

    /// <summary>
    /// Il logo di un repository: la sua immagine di anteprima sociale se ne ha una personalizzata e
    /// è pubblica; altrimenti l'avatar di chi lo possiede (per i repository privati l'anteprima
    /// non è raggiungibile dal browser senza accesso).
    /// </summary>
    private static string? LogoOf(GqlRepo repo) =>
        repo.UsesCustomOpenGraphImage && !repo.IsPrivate && !string.IsNullOrEmpty(repo.OpenGraphImageUrl)
            ? repo.OpenGraphImageUrl
            : repo.Owner?.AvatarUrl;

    private static Part RepoPart(string login, string section, GqlRepoList? list)
    {
        var nodes = list?.Nodes ?? new List<GqlRepo?>();
        var items = nodes
            .OfType<GqlRepo>()
            .Select(r => new DashboardItem(
                $"{login}:{section}:{r.DatabaseId}",
                login,
                section,
                "repo",
                r.Name,
                r.NameWithOwner,
                r.Url,
                r.PushedAt ?? r.UpdatedAt ?? DateTimeOffset.MinValue,
                null,
                null,
                false,
                0,
                Shorten(r.Description),
                r.PrimaryLanguage?.Name,
                r.StargazerCount,
                r.IsPrivate,
                r.Issues.TotalCount,
                r.PullRequests.TotalCount,
                LogoOf(r)))
            .ToList();
        // Una pagina piena significa che potrebbero essercene altri: il totale non è noto.
        int? total = nodes.Count < ReposPageSize ? items.Count : null;
        return new Part(items, new SectionTotal(login, section, total), null);
    }

    private async Task<Part> NotificationsAsync(string login, string token, CancellationToken ct)
    {
        var response = await api.GetAsync<List<GhNotification>>(token, $"notifications?per_page={NotificationsPageSize}", ct);

        if (!response.Ok)
            return new Part(new List<DashboardItem>(), null, new DashboardError(login, "notifications", response.Error ?? new ErrorInfo("unknown")));

        var items = (response.Data ?? new List<GhNotification>())
            .Select(n => new DashboardItem(
                $"{login}:notifications:{n.Id}",
                login,
                "notifications",
                NotificationType(n.Subject.Type),
                n.Subject.Title,
                n.Repository.FullName,
                NotificationUrl(n),
                n.UpdatedAt,
                null,
                n.Reason,
                false,
                0))
            .ToList();
        // Una pagina piena significa che potrebbero essercene altre: il totale non è noto.
        int? total = items.Count < NotificationsPageSize ? items.Count : null;
        return new Part(items, new SectionTotal(login, "notifications", total), null);
    }

    private static DashboardItem ToItem(string login, string section, GhIssue issue) => new(
        $"{login}:{section}:{issue.Id}",
        login,
        section,
        issue.PullRequest is not null ? "pr" : "issue",
        issue.Title,
        RepoFromApiUrl(issue.RepositoryUrl),
        issue.HtmlUrl,
        issue.UpdatedAt,
        issue.User?.Login,
        null,
        issue.Draft == true,
        issue.Comments,
        Shorten(issue.Body));

    private static string? Shorten(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= DescriptionMaxLength ? text : text[..DescriptionMaxLength].TrimEnd() + "…";
    }

    // "https://api.github.com/repos/owner/repo" -> "owner/repo"
    private static string RepoFromApiUrl(string? url)
    {
        const string marker = "/repos/";
        if (url is null) return "";
        var index = url.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? url : url[(index + marker.Length)..];
    }

    private static string NotificationType(string type) => type switch
    {
        "PullRequest" => "pr",
        "Issue" => "issue",
        "Release" => "release",
        "Commit" => "commit",
        "Discussion" => "discussion",
        _ => "other",
    };

    // L'API restituisce URL dell'API; per l'utente servono quelli della pagina web.
    private static string NotificationUrl(GhNotification n)
    {
        var repoUrl = n.Repository.HtmlUrl ?? $"https://github.com/{n.Repository.FullName}";
        if (n.Subject.Type == "Release") return repoUrl + "/releases";
        if (n.Subject.Url is null) return repoUrl;

        return n.Subject.Url
            .Replace("https://api.github.com/repos/", "https://github.com/")
            .Replace("/pulls/", "/pull/")
            .Replace("/commits/", "/commit/");
    }
}
