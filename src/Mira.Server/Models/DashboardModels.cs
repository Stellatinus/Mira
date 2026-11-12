namespace Mira.Server.Models;

/// <summary>Stato di un account collegato. State: ok | invalid | error.</summary>
public sealed record AccountStatus(
    string Login,
    string? Name,
    string? AvatarUrl,
    string State,
    DateTimeOffset? TokenExpiresAt,
    string[] Scopes,
    string[] Warnings);

/// <summary>
/// Un elemento della dashboard. Section: review | assigned | mine-prs | mine-issues | mentions | notifications.
/// Type: issue | pr | release | commit | discussion | other.
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
    int Comments);

/// <summary>
/// Quanti elementi ci sono davvero in una sezione per un account, anche oltre quelli scaricati.
/// Total è null quando GitHub non lo dice (notifiche oltre la prima pagina).
/// </summary>
public sealed record SectionTotal(string Account, string Section, int? Total);

public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AccountStatus> Accounts,
    IReadOnlyList<DashboardItem> Items,
    IReadOnlyList<SectionTotal> Totals,
    IReadOnlyList<string> Errors);
