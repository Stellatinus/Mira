using Mira.Server.Models;
using Mira.Server.Services;
using Mira.Server.Storage;

namespace Mira.Server.Api;

public sealed record AddAccountRequest(string? Token);

public sealed record SeenRequest(string? Url, DateTimeOffset? UpdatedAt, DateTimeOffset? SnoozeUntil);

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
            AddAccountRequest request, AccountService accounts, DashboardService dashboard, IssueService issues,
            CancellationToken ct) =>
        {
            var result = await accounts.AddAsync(request.Token, ct);
            if (result.Error is not null)
                return Results.BadRequest(new { error = result.Error });

            dashboard.Invalidate();
            issues.Invalidate();
            return Results.Ok(result.Account);
        });

        api.MapDelete("/accounts/{login}", async (
            string login, AccountService accounts, DashboardService dashboard, IssueService issues, CancellationToken ct) =>
        {
            await accounts.RemoveAsync(login, ct);
            dashboard.Invalidate();
            issues.Invalidate();
            return Results.NoContent();
        });

        // Liste complete di issue, scaricate alla richiesta quando si apre la vista (non fanno parte della dashboard).
        api.MapGet("/issues/{view}", async (string view, bool? refresh, IssueService issues, CancellationToken ct) =>
            IssueService.Views.Contains(view)
                ? Results.Ok(await issues.GetAsync(view, refresh == true, ct))
                : Results.NotFound());

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

            await seen.SetAsync(request.Url!, request.UpdatedAt.Value, request.SnoozeUntil, ct);
            return Results.NoContent();
        });

        // Con `url` toglie una voce (torna da vedere); senza, le toglie tutte.
        api.MapDelete("/seen", async (string? url, SeenStore seen, CancellationToken ct) =>
        {
            await seen.RemoveAsync(url, ct);
            return Results.NoContent();
        });

        api.MapGet("/version", () => Results.Ok(new { version = AppVersion.Current }));

        api.MapGet("/dashboard", async (bool? refresh, DashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetAsync(refresh == true, ct)));
    }
}
