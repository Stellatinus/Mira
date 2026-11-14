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
    internal const int SearchPageSize = 100;
    internal const int NotificationsPageSize = 50;
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
            return new AccountData(status with { RateLimits = api.RateLimitsFor(token) }, new List<DashboardItem>(), new List<SectionTotal>(), errors);
        }

        var tasks = SearchDefinitions(login)
            .Select(d => SearchAsync(login, token, d.Section, d.Query, ct))
            .ToList();
        tasks.Add(NotificationsAsync(login, token, ct));
        var repositories = RepositoriesAsync(login, token, ct);
        var reviews = ReviewsAsync(login, token, ct);

        var parts = (await Task.WhenAll(tasks)).Concat(await repositories).ToArray();
        var (reviewInfo, reviewError) = await reviews;

        // Lo stato di review si attacca alle PR dell'utente (sezione «mine-prs»), riconosciute dall'indirizzo.
        foreach (var part in parts)
        {
            for (var i = 0; i < part.Items.Count; i++)
            {
                if (part.Items[i] is { Section: "mine-prs" } item && reviewInfo.TryGetValue(item.Url, out var info))
                    part.Items[i] = item with { Review = info.Review, Checks = info.Checks };
            }
        }

        var errorList = parts.Where(p => p.Error is not null).Select(p => p.Error!).ToList();
        if (reviewError is not null) errorList.Add(reviewError);

        return new AccountData(
            // I limiti si leggono alla fine: le ricerche e la richiesta GraphQL li hanno appena aggiornati.
            status with { RateLimits = api.RateLimitsFor(token) },
            parts.SelectMany(p => p.Items).ToList(),
            parts.Where(p => p.Total is not null).Select(p => p.Total!).ToList(),
            errorList);
    }

    internal static (string Section, string Query)[] SearchDefinitions(string login) =>
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
          isFork
          parent { nameWithOwner }
          unassigned: issues(states: OPEN, filterBy: {assignee: null}) { totalCount }
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

    // Lo stato di review e l'esito della CI (controlli dell'ultimo commit) di tutte le PR aperte dell'utente in una sola
    // richiesta GraphQL (la REST ne vorrebbe una per PR).
    // Le liste annidate sono corte (le ultime 10): il costo resta basso.
    private const string ReviewsQuery = """
        query($first: Int!) {
          viewer {
            pullRequests(first: $first, states: OPEN, orderBy: {field: UPDATED_AT, direction: DESC}) {
              nodes {
                url
                reviewDecision
                reviews(last: 10) { nodes { state submittedAt author { login } } }
                comments(last: 10) { totalCount nodes { createdAt author { login } } }
                commits(last: 1) {
                  nodes {
                    commit {
                      statusCheckRollup {
                        state
                        contexts(first: 50) {
                          nodes {
                            __typename
                            ... on CheckRun { name conclusion status detailsUrl }
                            ... on StatusContext { context state targetUrl }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>Se la richiesta fallisce le PR si vedono lo stesso, senza stato di review, e l'errore compare tra i problemi.</summary>
    private async Task<(Dictionary<string, (ReviewInfo Review, ChecksInfo? Checks)> Info, DashboardError? Error)> ReviewsAsync(string login, string token, CancellationToken ct)
    {
        var info = new Dictionary<string, (ReviewInfo Review, ChecksInfo? Checks)>(StringComparer.OrdinalIgnoreCase);
        var response = await api.QueryAsync<GqlPrData>(token, ReviewsQuery, new { first = SearchPageSize }, ct);
        var list = response.Data?.Data?.Viewer?.PullRequests;

        if (list is null)
        {
            var error = !response.Ok ? response.Error : GraphQlError(response.Data?.Errors);
            return (info, new DashboardError(login, "reviews", error ?? new ErrorInfo("unknown")));
        }

        foreach (var pr in list.Nodes?.OfType<GqlPr>() ?? [])
        {
            var review = new ReviewInfo(
                pr.ReviewDecision,
                (pr.Reviews?.Nodes ?? []).OfType<GqlReview>().Select(r => new PrReview(r.Author?.Login, r.State, r.SubmittedAt)).ToList(),
                (pr.Comments?.Nodes ?? []).OfType<GqlComment>().Select(c => new PrComment(c.Author?.Login, c.CreatedAt)).ToList(),
                pr.Comments?.TotalCount ?? 0);
            var rollup = pr.Commits?.Nodes?.OfType<GqlCommitNode>().LastOrDefault()?.Commit?.StatusCheckRollup;
            info[pr.Url] = (review, ToChecks(rollup));
        }

        return (info, null);
    }

    // Un controllo è fallito se la conclusione è negativa; è in corso se non è ancora finito.
    private static readonly HashSet<string> FailedConclusions = ["FAILURE", "TIMED_OUT", "STARTUP_FAILURE", "CANCELLED"];

    /// <summary>L'esito della CI: lo stato complessivo di GitHub, quanti controlli sono in corso e quali sono falliti. Senza controlli, null.</summary>
    internal static ChecksInfo? ToChecks(GqlRollup? rollup)
    {
        if (rollup is null) return null;

        var contexts = rollup.Contexts?.Nodes?.OfType<GqlContext>().ToList() ?? [];
        var failed = new List<CheckRef>();
        var pending = 0;
        foreach (var context in contexts)
        {
            if (context.TypeName == "CheckRun")
            {
                if (context.Status != "COMPLETED") pending++;
                else if (context.Conclusion is not null && FailedConclusions.Contains(context.Conclusion))
                    failed.Add(new CheckRef(context.Name ?? "?", context.DetailsUrl));
            }
            else
            {
                if (context.State is "FAILURE" or "ERROR") failed.Add(new CheckRef(context.Context ?? "?", context.TargetUrl));
                else if (context.State is "PENDING" or "EXPECTED") pending++;
            }
        }

        return new ChecksInfo(rollup.State, contexts.Count, pending, failed);
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
                LogoOf(r),
                r.IsFork,
                r.Parent?.NameWithOwner,
                r.Unassigned.TotalCount))
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
            .Select(n => ToNotificationItem(login, n))
            .ToList();
        // Una pagina piena significa che potrebbero essercene altre: il totale non è noto.
        int? total = items.Count < NotificationsPageSize ? items.Count : null;
        return new Part(items, new SectionTotal(login, "notifications", total), null);
    }

    internal static DashboardItem ToNotificationItem(string login, GhNotification n) => new(
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
        0);

    internal static DashboardItem ToItem(string login, string section, GhIssue issue) => new(
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
        Shorten(issue.Body),
        Assignees: issue.Assignees?.Select(a => a.Login).ToArray());

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
