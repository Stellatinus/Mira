namespace Mira.Server.Models;

/// <summary>
/// Stato di un account collegato. State: ok | invalid | error.
/// Warnings sono codici tradotti dal frontend: "fine-grained" oppure "missing-scope:&lt;scope&gt;".
/// </summary>
public sealed record AccountStatus(
    string Login,
    string? Name,
    string? AvatarUrl,
    string State,
    DateTimeOffset? TokenExpiresAt,
    string[] Scopes,
    string[] Warnings);

/// <summary>
/// Un elemento della dashboard.
/// Section: review | assigned | mine-prs | mine-issues | mentions | notifications | repos | starred.
/// Type: issue | pr | repo | release | commit | discussion | other.
/// Description, Language, Stars, Private, OpenIssues e OpenPrs (aperte nel repository), LogoUrl (immagine del repository), Fork, ForkOf (repository da cui deriva) UnassignedIssues (issue aperte senza assegnatario) e Assegnatari (logins degli assegnatari di una issue o PR) sono valorizzati solo dove hanno senso.
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
    string[]? Assignees = null);

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
