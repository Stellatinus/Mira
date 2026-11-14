using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

public sealed record AddAccountResult(AccountStatus? Account, ErrorInfo? Error);

public sealed class AccountService(ISecretStore store, GitHubApi api)
{
    private static readonly string[] RequiredScopes = ["notifications", "repo"];

    public async Task<IReadOnlyList<AccountStatus>> GetStatusesAsync(CancellationToken ct)
    {
        var tokens = await store.LoadAllAsync(ct);
        var statuses = await Task.WhenAll(tokens.Select(async pair =>
        {
            var user = await api.GetAsync<GhUser>(pair.Value, "user", ct);
            return BuildStatus(pair.Key, user) with { RateLimits = api.RateLimitsFor(pair.Value) };
        }));
        return statuses.OrderBy(s => s.Login, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<AddAccountResult> AddAsync(string? token, CancellationToken ct)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token))
            return new AddAccountResult(null, new ErrorInfo("token-empty"));

        var user = await api.GetAsync<GhUser>(token, "user", ct);
        if (user.Status == HttpStatusCode.Unauthorized)
            return new AddAccountResult(null, new ErrorInfo("token-invalid"));
        if (!user.Ok || user.Data is null)
            return new AddAccountResult(null, new ErrorInfo("github-unreachable", null, user.Error));

        await store.SaveAsync(user.Data.Login, token, ct);
        return new AddAccountResult(BuildStatus(user.Data.Login, user) with { RateLimits = api.RateLimitsFor(token) }, null);
    }

    /// <summary>
    /// Sostituisce il token di un account già collegato (per esempio perché sta per scadere). Il nuovo token deve
    /// essere dello <b>stesso account</b>: se appartiene a un altro non si salva nulla. L'account resta lo stesso, quindi
    /// niente di ciò che Mira ricorda (stato «visto», impostazioni) va perso.
    /// </summary>
    public async Task<AddAccountResult> RenewAsync(string login, string? token, CancellationToken ct)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token))
            return new AddAccountResult(null, new ErrorInfo("token-empty"));

        if (!(await store.LoadAllAsync(ct)).ContainsKey(login))
            return new AddAccountResult(null, new ErrorInfo("account-unknown"));

        var user = await api.GetAsync<GhUser>(token, "user", ct);
        if (user.Status == HttpStatusCode.Unauthorized)
            return new AddAccountResult(null, new ErrorInfo("token-invalid"));
        if (!user.Ok || user.Data is null)
            return new AddAccountResult(null, new ErrorInfo("github-unreachable", null, user.Error));
        if (!string.Equals(user.Data.Login, login, StringComparison.OrdinalIgnoreCase))
            return new AddAccountResult(null, new ErrorInfo("token-other-account", user.Data.Login));

        await store.SaveAsync(login, token, ct);
        return new AddAccountResult(BuildStatus(login, user) with { RateLimits = api.RateLimitsFor(token) }, null);
    }

    public Task RemoveAsync(string login, CancellationToken ct) => store.RemoveAsync(login, ct);

    public static AccountStatus BuildStatus(string login, ApiResponse<GhUser> user)
    {
        var state = user.Ok ? "ok" : user.Status == HttpStatusCode.Unauthorized ? "invalid" : "error";
        var warnings = new List<string>();

        if (user.Ok)
        {
            if (user.Scopes is null)
            {
                warnings.Add("fine-grained");
            }
            else
            {
                foreach (var scope in RequiredScopes)
                {
                    if (!user.Scopes.Contains(scope))
                        warnings.Add($"missing-scope:{scope}");
                }
            }
        }

        return new AccountStatus(
            user.Data?.Login ?? login,
            user.Data?.Name,
            user.Data?.AvatarUrl,
            state,
            user.TokenExpiresAt,
            user.Scopes ?? Array.Empty<string>(),
            warnings.ToArray());
    }
}
