using System.Text.Json;
using System.Text.Json.Serialization;

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
    string? Body,
    List<GhUserRef>? Assignees);

public sealed record GhOrg(string Login);

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
    GqlOwner? Owner,
    bool IsFork,
    GqlParent? Parent,
    GqlCount Unassigned);

public sealed record GqlOwner(string? AvatarUrl);

public sealed record GqlParent(string NameWithOwner);

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

// --- GraphQL: stato delle PR dell'utente ---

public sealed record GqlPrData(GqlPrViewer? Viewer);

public sealed record GqlPrViewer(GqlPrList? PullRequests);

public sealed record GqlPrList(List<GqlPr?>? Nodes);

public sealed record GqlPr(
    string Url,
    string? ReviewDecision,
    GqlReviewList? Reviews,
    GqlCommentList? Comments,
    GqlCommitList? Commits = null);

public sealed record GqlActor(string? Login);

public sealed record GqlReviewList(List<GqlReview?>? Nodes);

public sealed record GqlReview(string State, DateTimeOffset? SubmittedAt, GqlActor? Author);

public sealed record GqlCommentList(int TotalCount, List<GqlComment?>? Nodes);

public sealed record GqlComment(DateTimeOffset CreatedAt, GqlActor? Author);

// --- GraphQL: controlli (CI) dell'ultimo commit di una PR ---

public sealed record GqlCommitList(List<GqlCommitNode?>? Nodes);

public sealed record GqlCommitNode(GqlCommit? Commit);

public sealed record GqlCommit(GqlRollup? StatusCheckRollup);

public sealed record GqlRollup(string State, GqlContextList? Contexts);

public sealed record GqlContextList(List<GqlContext?>? Nodes);

/// <summary>Un controllo: o una «check run» (Name, Conclusion, Status, DetailsUrl) o uno «status» (Context, State, TargetUrl).</summary>
public sealed record GqlContext(
    [property: JsonPropertyName("__typename")] string? TypeName,
    string? Name,
    string? Conclusion,
    string? Status,
    string? DetailsUrl,
    string? Context,
    string? State,
    string? TargetUrl);

/// <summary>L'ultima release pubblicata di un repository (solo i campi che servono).</summary>
public sealed record GhRelease(string TagName, string? Name, string HtmlUrl, bool Prerelease, bool Draft, DateTimeOffset? PublishedAt, List<GhAsset>? Assets = null);

/// <summary>Un file allegato a una release (pacchetto, somme di controllo, firma).</summary>
public sealed record GhAsset(string Name, long Size, string BrowserDownloadUrl);

// --- GraphQL: ultima release dei repository seguiti ---

public sealed record GqlReleaseData(GqlReleaseViewer? Viewer);

public sealed record GqlReleaseViewer(GqlReleaseRepoList? StarredRepositories);

public sealed record GqlReleaseRepoList(List<GqlReleaseRepo?>? Nodes);

public sealed record GqlReleaseRepo(string NameWithOwner, bool IsPrivate, GqlLatestRelease? LatestRelease);

public sealed record GqlLatestRelease(string? Name, string TagName, string Url, DateTimeOffset? PublishedAt, string? Description);

// --- GraphQL: pagina di un repository (milestone, PR e issue aperte) ---

public sealed record GqlRepoPageData(GqlRepoPageRepo? Repository);

public sealed record GqlRepoPageRepo(
    string NameWithOwner,
    string Url,
    string? Description,
    bool IsPrivate,
    GqlMilestoneList? Milestones,
    GqlPagedItems? PullRequests,
    GqlPagedItems? Issues);

public sealed record GqlMilestoneList(List<GqlMilestone?>? Nodes);

public sealed record GqlMilestone(
    int Number,
    string Title,
    string? Description,
    DateTimeOffset? DueOn,
    string State,
    string Url,
    double ProgressPercentage,
    GqlCount? Open,
    GqlCount? Closed);

public sealed record GqlPagedItems(int TotalCount, List<GqlPageItem?>? Nodes);

public sealed record GqlPageItem(
    int Number,
    string Title,
    string Url,
    DateTimeOffset UpdatedAt,
    bool? IsDraft,
    GqlActor? Author,
    GqlCount? Comments,
    GqlActorList? Assignees,
    GqlMilestoneRef? Milestone);

public sealed record GqlActorList(List<GqlActor?>? Nodes);

public sealed record GqlMilestoneRef(int Number);
