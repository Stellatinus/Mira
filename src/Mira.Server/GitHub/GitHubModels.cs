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

public sealed record GhRepo(
    long Id,
    string Name,
    string FullName,
    string? Description,
    string HtmlUrl,
    string? Language,
    int StargazersCount,
    bool Private,
    DateTimeOffset? PushedAt,
    DateTimeOffset? UpdatedAt);

public sealed record GhNotification(
    string Id,
    bool Unread,
    string Reason,
    DateTimeOffset UpdatedAt,
    GhNotificationSubject Subject,
    GhNotificationRepo Repository);

public sealed record GhNotificationSubject(string Title, string? Url, string Type);

public sealed record GhNotificationRepo(string FullName, string? HtmlUrl);
