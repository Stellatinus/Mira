using Mira.Server.Models;
using Mira.Server.Services;
using Mira.Server.Storage;

namespace Mira.Server.Api;

public sealed record AddAccountRequest(string? Token);

public sealed record SeenRequest(string? Url, DateTimeOffset? UpdatedAt, DateTimeOffset? SnoozeUntil);

public static class Endpoints
{
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
