using Mira.Server.Services;

namespace Mira.Server.Api;

public sealed record AddAccountRequest(string? Token);

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

        api.MapGet("/dashboard", async (bool? refresh, DashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetAsync(refresh == true, ct)));
    }
}
