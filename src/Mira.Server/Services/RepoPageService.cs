using System.Text.RegularExpressions;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>Una milestone: State è "open" o "closed"; Progress è la percentuale di issue chiuse (0-100).</summary>
public sealed record RepoMilestone(
    int Number,
    string Title,
    string? Description,
    DateTimeOffset? DueOn,
    string State,
    string Url,
    double Progress,
    int OpenIssues,
    int ClosedIssues);

/// <summary>Una PR o issue aperta, nella pagina di un repository. Milestone è il numero della milestone, se c'è.</summary>
public sealed record RepoPageItem(
    int Number,
    string Title,
    string Url,
    DateTimeOffset UpdatedAt,
    string? Author,
    bool Draft,
    int Comments,
    string[] Assignees,
    int? Milestone);

/// <summary>
/// Tutto ciò che serve alla pagina di un repository. PrTotal e IssueTotal sono i totali veri di GitHub,
/// anche oltre gli elementi scaricati (i 50 aggiornati più di recente).
/// </summary>
public sealed record RepoPage(
    DateTimeOffset GeneratedAt,
    string Repo,
    string Url,
    string? Description,
    bool Private,
    IReadOnlyList<RepoMilestone> Milestones,
    IReadOnlyList<RepoPageItem> Prs,
    int PrTotal,
    IReadOnlyList<RepoPageItem> Issues,
    int IssueTotal);

/// <summary>
/// La pagina personalizzata di un repository: milestone (per la «timeline»), PR aperte e tutte le issue aperte,
/// di chiunque siano. Una sola richiesta GraphQL, con il primo account che vede il repository; il risultato resta
/// in cache per due minuti.
/// </summary>
public sealed partial class RepoPageService(ISecretStore store, GitHubApi api)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ForcedRefreshMinAge = TimeSpan.FromSeconds(10);
    private const int PageSize = 50;
    private const int MilestonePageSize = 30;

    private readonly Dictionary<string, RepoPage> _cache = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex NamePattern();

    public static bool IsValidRepoPart(string? value) => value is not null && NamePattern().IsMatch(value);

    public void Invalidate()
    {
        lock (_cache) _cache.Clear();
    }

    private const string Query = """
        query($owner: String!, $name: String!, $first: Int!, $milestones: Int!) {
          repository(owner: $owner, name: $name) {
            nameWithOwner url description isPrivate
            milestones(first: $milestones, orderBy: {field: DUE_DATE, direction: ASC}, states: [OPEN, CLOSED]) {
              nodes {
                number title description dueOn state url progressPercentage
                open: issues(states: OPEN) { totalCount }
                closed: issues(states: CLOSED) { totalCount }
              }
            }
            pullRequests(first: $first, states: OPEN, orderBy: {field: UPDATED_AT, direction: DESC}) {
              totalCount
              nodes { number title url updatedAt isDraft author { login } comments { totalCount } milestone { number } }
            }
            issues(first: $first, states: OPEN, orderBy: {field: UPDATED_AT, direction: DESC}) {
              totalCount
              nodes { number title url updatedAt author { login } comments { totalCount } assignees(first: 5) { nodes { login } } milestone { number } }
            }
          }
        }
        """;

    /// <summary>
    /// Legge la pagina del repository. `preferred` è l'account da provare per primo (quello della card);
    /// se non vede il repository si provano gli altri. Errore "repo-unavailable" se nessuno lo vede.
    /// </summary>
    public async Task<(RepoPage? Page, ErrorInfo? Error)> GetAsync(
        string owner, string name, string? preferred, bool force, CancellationToken ct)
    {
        var key = $"{owner}/{name}";
        var maxAge = force ? ForcedRefreshMinAge : CacheTtl;
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.GeneratedAt < maxAge)
                return (cached, null);
        }

        var tokens = await store.LoadAllAsync(ct);
        if (tokens.Count == 0)
            return (null, new ErrorInfo("account-unknown"));

        var ordered = tokens
            .OrderByDescending(pair => string.Equals(pair.Key, preferred, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ErrorInfo? lastError = null;
        foreach (var (_, token) in ordered)
        {
            var response = await api.QueryAsync<GqlRepoPageData>(
                token, Query, new { owner, name, first = PageSize, milestones = MilestonePageSize }, ct);

            var repo = response.Data?.Data?.Repository;
            if (repo is not null)
            {
                var page = Build(repo);
                lock (_cache) _cache[key] = page;
                return (page, null);
            }

            // Un repository che questo account non vede (NOT_FOUND) si prova con il prossimo.
            lastError = !response.Ok ? response.Error : null;
        }

        return (null, lastError is { Code: not "unauthorized" } ? lastError : new ErrorInfo("repo-unavailable"));
    }

    internal static RepoPage Build(GqlRepoPageRepo repo)
    {
        var milestones = (repo.Milestones?.Nodes ?? [])
            .OfType<GqlMilestone>()
            .Select(m => new RepoMilestone(
                m.Number,
                m.Title,
                string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim(),
                m.DueOn,
                string.Equals(m.State, "CLOSED", StringComparison.OrdinalIgnoreCase) ? "closed" : "open",
                m.Url,
                m.ProgressPercentage,
                m.Open?.TotalCount ?? 0,
                m.Closed?.TotalCount ?? 0))
            .ToList();

        return new RepoPage(
            DateTimeOffset.UtcNow,
            repo.NameWithOwner,
            repo.Url,
            string.IsNullOrWhiteSpace(repo.Description) ? null : repo.Description.Trim(),
            repo.IsPrivate,
            milestones,
            (repo.PullRequests?.Nodes ?? []).OfType<GqlPageItem>().Select(ToItem).ToList(),
            repo.PullRequests?.TotalCount ?? 0,
            (repo.Issues?.Nodes ?? []).OfType<GqlPageItem>().Select(ToItem).ToList(),
            repo.Issues?.TotalCount ?? 0);
    }

    private static RepoPageItem ToItem(GqlPageItem n) => new(
        n.Number,
        n.Title,
        n.Url,
        n.UpdatedAt,
        n.Author?.Login,
        n.IsDraft == true,
        n.Comments?.TotalCount ?? 0,
        (n.Assignees?.Nodes ?? []).OfType<GqlActor>().Select(a => a.Login ?? "").Where(l => l.Length > 0).ToArray(),
        n.Milestone?.Number);
}
