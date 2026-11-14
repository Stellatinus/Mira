using Mira.Server.GitHub;

namespace Mira.Server.Models;

/// <summary>
/// Stato di un account collegato. State: ok | invalid | error.
/// Warnings sono codici tradotti dal frontend: "fine-grained" oppure "missing-scope:&lt;scope&gt;".
/// RateLimits: quante richieste restano all'account per ogni risorsa di GitHub (core, search, graphql…), come risultano dall'ultima risposta.
/// </summary>
public sealed record AccountStatus(
    string Login,
    string? Name,
    string? AvatarUrl,
    string State,
    DateTimeOffset? TokenExpiresAt,
    string[] Scopes,
    string[] Warnings,
    IReadOnlyList<RateLimit>? RateLimits = null);

/// <summary>Una review su una PR: chi, con che esito (APPROVED, CHANGES_REQUESTED, COMMENTED, DISMISSED, PENDING) e quando.</summary>
public sealed record PrReview(string? Login, string State, DateTimeOffset? At);

/// <summary>Un commento su una PR: chi e quando.</summary>
public sealed record PrComment(string? Login, DateTimeOffset At);

/// <summary>
/// A che punto è una PR dell'utente. Decision è quella di GitHub (APPROVED, CHANGES_REQUESTED, REVIEW_REQUIRED) e
/// manca quando il repository non chiede review. Reviews e Comments sono gli ultimi (i più recenti per ultimi);
/// CommentCount è il totale dei commenti della conversazione.
/// </summary>
public sealed record ReviewInfo(
    string? Decision,
    IReadOnlyList<PrReview> Reviews,
    IReadOnlyList<PrComment> Comments,
    int CommentCount);

/// <summary>
/// Un elemento della dashboard.
/// Section: review | assigned | mine-prs | mine-issues | mentions | notifications | repos | starred.
/// Type: issue | pr | repo | release | commit | discussion | other.
/// Description, Language, Stars, Private, OpenIssues e OpenPrs (aperte nel repository), LogoUrl (immagine del repository), Fork, ForkOf (repository da cui deriva) UnassignedIssues (issue aperte senza assegnatario) Assegnatari (logins degli assegnatari di una issue o PR) e Review (stato di review di una PR dell'utente) sono valorizzati solo dove hanno senso.
/// </summary>
public sealed record DashboardItem(
    string Id,
    string Account,
    string Section,
    string Type,
    string Title,
    string Repo,
    string Url,
    DateTimeOffset UpdatedAt,
    string? Author,
    string? Reason,
    bool Draft,
    int Comments,
    string? Description = null,
    string? Language = null,
    int Stars = 0,
    bool Private = false,
    int OpenIssues = 0,
    int OpenPrs = 0,
    string? LogoUrl = null,
    bool Fork = false,
    string? ForkOf = null,
    int UnassignedIssues = 0,
    string[]? Assignees = null,
    ReviewInfo? Review = null);

/// <summary>
/// Quanti elementi ci sono davvero in una sezione per un account, anche oltre quelli scaricati.
/// Total è null quando GitHub non lo dice (notifiche oltre la prima pagina).
/// </summary>
public sealed record SectionTotal(string Account, string Section, int? Total);

/// <summary>Una lista di elementi caricata a parte (viste «Issue» e «Non assegnate»), con gli errori avvenuti.</summary>
public sealed record ExtraSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DashboardItem> Items,
    IReadOnlyList<DashboardError> Errors);

public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AccountStatus> Accounts,
    IReadOnlyList<DashboardItem> Items,
    IReadOnlyList<SectionTotal> Totals,
    IReadOnlyList<DashboardError> Errors);
