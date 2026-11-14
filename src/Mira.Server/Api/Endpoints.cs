using Mira.Server.Models;
using Mira.Server.Services;
using Mira.Server.Storage;

namespace Mira.Server.Api;

public sealed record AddAccountRequest(string? Token);

public sealed record SeenRequest(string? Url, DateTimeOffset? UpdatedAt, DateTimeOffset? SnoozeUntil, int? Comments = null, bool? Hidden = null);

public sealed record BaselineRequest(List<SeenBaseline>? Items);

public sealed record FollowRequest(string? Repo, bool? Follow);

public static class Endpoints
{
    /// <summary>Esegue un'azione sulle notifiche: 400 se gli identificativi non sono validi, 404 se l'account non esiste, 502 se GitHub rifiuta.</summary>
    private static async Task<IResult> NotificationAction(bool valid, Func<Task<ErrorInfo?>> action, DashboardService dashboard)
    {
        if (!valid)
            return Results.BadRequest(new { error = new ErrorInfo("notification-invalid") });

        var error = await action();
        if (error is null)
        {
            // Le liste in cache mostrerebbero ancora la notifica: al prossimo aggiornamento si rileggono.
            dashboard.Invalidate();
            return Results.NoContent();
        }

        return Results.Json(new { error }, statusCode: error.Code == "account-unknown" ? StatusCodes.Status404NotFound : StatusCodes.Status502BadGateway);
    }

    public static void MapMiraApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/accounts", async (AccountService accounts, CancellationToken ct) =>
            Results.Ok(await accounts.GetStatusesAsync(ct)));

        api.MapPost("/accounts", async (
            AddAccountRequest request, AccountService accounts, DashboardService dashboard, IssueService issues, SectionService sections, ReleaseService releases,
            CancellationToken ct) =>
        {
            var result = await accounts.AddAsync(request.Token, ct);
            if (result.Error is not null)
                return Results.BadRequest(new { error = result.Error });

            dashboard.Invalidate();
            issues.Invalidate();
            sections.Invalidate();
            releases.Invalidate();
            return Results.Ok(result.Account);
        });

        // Sostituisce il token di un account già collegato; il nuovo deve essere dello stesso account.
        api.MapPost("/accounts/{login}/renew", async (
            string login, AddAccountRequest request, AccountService accounts, DashboardService dashboard, IssueService issues, SectionService sections,
            CancellationToken ct) =>
        {
            var result = await accounts.RenewAsync(login, request.Token, ct);
            if (result.Error is not null)
                return Results.Json(new { error = result.Error }, statusCode: result.Error.Code == "account-unknown" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);

            dashboard.Invalidate();
            issues.Invalidate();
            sections.Invalidate();
            return Results.Ok(result.Account);
        });

        api.MapDelete("/accounts/{login}", async (
            string login, AccountService accounts, DashboardService dashboard, IssueService issues, SectionService sections, ReleaseService releases,
            CancellationToken ct) =>
        {
            await accounts.RemoveAsync(login, ct);
            dashboard.Invalidate();
            issues.Invalidate();
            sections.Invalidate();
            releases.Invalidate();
            return Results.NoContent();
        });

        // Liste complete di issue, scaricate alla richiesta quando si apre la vista (non fanno parte della dashboard).
        api.MapGet("/issues/{view}", async (string view, bool? refresh, IssueService issues, CancellationToken ct) =>
            IssueService.Views.Contains(view)
                ? Results.Ok(await issues.GetAsync(view, refresh == true, ct))
                : Results.NotFound());

        // Una pagina in più di una sezione per un account («Carica altri»): la dashboard ne scarica solo la prima.
        api.MapGet("/section", async (string account, string section, int page, SectionService sections, CancellationToken ct) =>
        {
            if (!SectionService.IsValidRequest(section, page))
                return Results.BadRequest(new { error = new ErrorInfo("section-invalid") });

            var (result, error) = await sections.GetAsync(account, section, page, ct);
            if (result is not null)
                return Results.Ok(result);

            return Results.Json(new { error }, statusCode: error?.Code == "account-unknown" ? StatusCodes.Status404NotFound : StatusCodes.Status502BadGateway);
        });

        // Le release dei repository seguiti (preferiti più quelli aggiunti a mano, meno quelli tolti).
        api.MapGet("/releases", async (bool? refresh, ReleaseService releases, CancellationToken ct) =>
            Results.Ok(await releases.GetAsync(refresh == true, ct)));

        api.MapGet("/follow", async (FollowStore follow, CancellationToken ct) => Results.Ok(await follow.GetAsync(ct)));

        api.MapPut("/follow", async (FollowRequest request, FollowStore follow, ReleaseService releases, CancellationToken ct) =>
        {
            if (!FollowStore.IsValidRepo(request.Repo) || request.Follow is null)
                return Results.BadRequest(new { error = new ErrorInfo("follow-invalid") });

            await follow.SetAsync(request.Repo!, request.Follow.Value, ct);
            releases.Invalidate();
            return Results.NoContent();
        });

        // Azioni sulle notifiche di GitHub, con il token dell'account a cui appartengono.
        var notifications = api.MapGroup("/notifications/{account}");

        notifications.MapPost("/threads/{threadId}/read", (string account, string threadId, NotificationService service, DashboardService dashboard, CancellationToken ct) =>
            NotificationAction(NotificationService.IsValidThreadId(threadId), () => service.MarkReadAsync(account, threadId, ct), dashboard));

        notifications.MapPost("/threads/{threadId}/unsubscribe", (string account, string threadId, NotificationService service, DashboardService dashboard, CancellationToken ct) =>
            NotificationAction(NotificationService.IsValidThreadId(threadId), () => service.UnsubscribeAsync(account, threadId, ct), dashboard));

        notifications.MapPost("/repos/{owner}/{repo}/read", (string account, string owner, string repo, NotificationService service, DashboardService dashboard, CancellationToken ct) =>
            NotificationAction(
                NotificationService.IsValidRepoPart(owner) && NotificationService.IsValidRepoPart(repo),
                () => service.MarkRepositoryReadAsync(account, owner, repo, ct),
                dashboard));

        // Stato «visto» locale: non tocca GitHub, sta in un file della cartella di configurazione.
        api.MapGet("/seen", async (SeenStore seen, CancellationToken ct) => Results.Ok(await seen.GetAllAsync(ct)));

        api.MapPut("/seen", async (SeenRequest request, SeenStore seen, CancellationToken ct) =>
        {
            if (!SeenStore.IsValidUrl(request.Url) || request.UpdatedAt is null)
                return Results.BadRequest(new { error = new ErrorInfo("seen-invalid") });

            await seen.SetAsync(request.Url!, request.UpdatedAt.Value, request.SnoozeUntil, request.Comments, request.Hidden ?? true, ct);
            return Results.NoContent();
        });

        // Il punto di partenza per contare le novità: aggiunge solo gli elementi senza voce, senza nasconderli.
        api.MapPost("/seen/baseline", async (BaselineRequest request, SeenStore seen, CancellationToken ct) =>
        {
            await seen.AddBaselinesAsync(request.Items ?? [], ct);
            return Results.NoContent();
        });

        // Con `url` toglie una voce (torna da vedere); senza, le toglie tutte.
        api.MapDelete("/seen", async (string? url, SeenStore seen, CancellationToken ct) =>
        {
            await seen.RemoveAsync(url, ct);
            return Results.NoContent();
        });

        // Esiste una release più nuova? Si legge solo alla richiesta e non scarica nulla.
        api.MapGet("/update", async (bool? refresh, UpdateService updates, CancellationToken ct) =>
            Results.Ok(await updates.GetAsync(refresh == true, ct)));

        api.MapGet("/version", () => Results.Ok(new { version = AppVersion.Current }));

        api.MapGet("/dashboard", async (bool? refresh, DashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetAsync(refresh == true, ct)));
    }
}
