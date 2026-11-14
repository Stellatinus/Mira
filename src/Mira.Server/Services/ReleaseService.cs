using System.Text.RegularExpressions;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>
/// Le release dei repository che l'utente segue: i preferiti (di tutti gli account) e quelli aggiunti a mano, meno quelli
/// tolti. Per ogni account una richiesta GraphQL legge l'ultima release di tutti i preferiti (<c>latestRelease</c>, che
/// salta bozze e pre-release) e una seconda quella dei repository aggiunti a mano. Si mostrano quelle uscite negli ultimi
/// <see cref="Window"/> giorni, senza doppioni; il risultato resta in cache per 15 minuti.
/// </summary>
public sealed partial class ReleaseService(ISecretStore store, GitHubApi api, FollowStore follow)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ForcedRefreshMinAge = TimeSpan.FromSeconds(30);
    private const int StarredPageSize = 100;
    private const int DescriptionMaxLength = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExtraSnapshot? _cache;

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex NamePattern();

    public void Invalidate() => _cache = null;

    public async Task<ExtraSnapshot> GetAsync(bool force, CancellationToken ct)
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

    private const string ReleaseFields = "nameWithOwner isPrivate latestRelease { name tagName url publishedAt description }";

    private const string StarredQuery = "query($first: Int!) { viewer { starredRepositories(first: $first, orderBy: {field: STARRED_AT, direction: DESC}) { nodes { " + ReleaseFields + " } } } }";

    /// <summary>La richiesta per i repository aggiunti a mano: uno per alias (r0, r1…), perché ognuno può non esistere o non essere visibile.</summary>
    internal static string ExtraQuery(IReadOnlyList<string> repos) =>
        "query { " + string.Join(" ", repos.Select((repo, i) =>
        {
            var (owner, name) = Split(repo);
            return $"r{i}: repository(owner: \"{owner}\", name: \"{name}\") {{ {ReleaseFields} }}";
        })) + " }";

    private static (string Owner, string Name) Split(string repo)
    {
        var parts = repo.Split('/');
        return (parts[0], parts[1]);
    }

    private async Task<ExtraSnapshot> BuildAsync(CancellationToken ct)
    {
        var tokens = await store.LoadAllAsync(ct);
        var list = await follow.GetAsync(ct);
        var muted = list.Muted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = list.Added.Where(r => FollowStore.IsValidRepo(r) && NamePattern().IsMatch(Split(r).Owner)).ToList();

        var perAccount = await Task.WhenAll(tokens.Select(pair => LoadAccountAsync(pair.Key, pair.Value, added, ct)));

        var cutoff = DateTimeOffset.UtcNow - Window;
        var unique = new Dictionary<string, DashboardItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in perAccount.SelectMany(a => a.Items))
        {
            if (muted.Contains(item.Repo) || item.UpdatedAt < cutoff) continue;
            unique.TryAdd(item.Url, item);
        }

        return new ExtraSnapshot(
            DateTimeOffset.UtcNow,
            unique.Values.OrderByDescending(i => i.UpdatedAt).ToList(),
            perAccount.SelectMany(a => a.Errors).ToList());
    }

    private async Task<(List<DashboardItem> Items, List<DashboardError> Errors)> LoadAccountAsync(
        string login, string token, IReadOnlyList<string> added, CancellationToken ct)
    {
        var items = new List<DashboardItem>();
        var errors = new List<DashboardError>();

        var starred = await api.QueryAsync<GqlReleaseData>(token, StarredQuery, new { first = StarredPageSize }, ct);
        var nodes = starred.Data?.Data?.Viewer?.StarredRepositories?.Nodes;
        if (nodes is null)
            errors.Add(new DashboardError(login, "releases", (!starred.Ok ? starred.Error : GraphQlError(starred.Data?.Errors)) ?? new ErrorInfo("unknown")));
        else
            items.AddRange(nodes.OfType<GqlReleaseRepo>().Select(r => ToItem(login, r)).OfType<DashboardItem>());

        if (added.Count > 0)
        {
            // Un repository che questo account non vede (privato di altri, inesistente) risponde null e GraphQL aggiunge un errore:
            // basta che un altro account lo veda, quindi qui non è un problema.
            var extra = await api.QueryAsync<Dictionary<string, GqlReleaseRepo?>>(token, ExtraQuery(added), null, ct);
            if (extra.Data?.Data is { } byAlias)
                items.AddRange(byAlias.Values.OfType<GqlReleaseRepo>().Select(r => ToItem(login, r)).OfType<DashboardItem>());
            else if (!extra.Ok)
                errors.Add(new DashboardError(login, "releases", extra.Error ?? new ErrorInfo("unknown")));
        }

        return (items, errors);
    }

    private static ErrorInfo GraphQlError(List<GqlError>? errors)
    {
        var first = errors?.FirstOrDefault();
        return first?.Type == "RATE_LIMITED" ? new ErrorInfo("rate-limited") : new ErrorInfo("github-graphql", first?.Message);
    }

    /// <summary>La release come elemento della lista; i repository senza release o senza data non danno nulla.</summary>
    internal static DashboardItem? ToItem(string login, GqlReleaseRepo repo)
    {
        if (repo.LatestRelease is not { PublishedAt: { } published } release) return null;

        return new DashboardItem(
            $"{login}:releases:{repo.NameWithOwner}:{release.TagName}",
            login,
            "releases",
            "release",
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            repo.NameWithOwner,
            release.Url,
            published,
            null,
            null,
            false,
            0,
            Shorten(release.Description),
            Private: repo.IsPrivate);
    }

    private static string? Shorten(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= DescriptionMaxLength ? text : text[..DescriptionMaxLength].TrimEnd() + "…";
    }
}
