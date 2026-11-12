using System.Text.Json;

namespace Mira.Server.GitHub;

// Forma delle risposte di GitHub (snake_case), limitata ai campi che ci servono.

public sealed record GhUser(string Login, string? Name, string? AvatarUrl, string? HtmlUrl);

public sealed record GhUserRef(string Login);

public sealed record GhSearchResult(int TotalCount, List<GhIssue>? Items);

public sealed record GhIssue(
    long Id,
    int Number,
    string Title,
    string HtmlUrl,
    string State,
    bool? Draft,
    int Comments,
    DateTimeOffset UpdatedAt,
    string? RepositoryUrl,
    GhUserRef? User,
    JsonElement? PullRequest,
    string? Body);

// --- GraphQL (nomi camelCase) ---

public sealed record GqlResponse<T>(T? Data, List<GqlError>? Errors);

public sealed record GqlError(string? Type, string? Message);

public sealed record GqlData(GqlViewer? Viewer);

public sealed record GqlViewer(GqlRepoList? Repos, GqlRepoList? Starred);

public sealed record GqlRepoList(List<GqlRepo?>? Nodes);

public sealed record GqlRepo(
    long DatabaseId,
    string Name,
    string NameWithOwner,
    string? Description,
    string Url,
    bool IsPrivate,
    int StargazerCount,
    DateTimeOffset? PushedAt,
    DateTimeOffset? UpdatedAt,
    GqlLanguage? PrimaryLanguage,
    GqlCount Issues,
    GqlCount PullRequests,
    string? OpenGraphImageUrl,
    bool UsesCustomOpenGraphImage,
    GqlOwner? Owner);

public sealed record GqlOwner(string? AvatarUrl);

public sealed record GqlLanguage(string Name);

public sealed record GqlCount(int TotalCount);

public sealed record GhNotification(
    string Id,
    bool Unread,
    string Reason,
    DateTimeOffset UpdatedAt,
    GhNotificationSubject Subject,
    GhNotificationRepo Repository);

public sealed record GhNotificationSubject(string Title, string? Url, string Type);

public sealed record GhNotificationRepo(string FullName, string? HtmlUrl);
