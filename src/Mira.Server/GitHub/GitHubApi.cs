using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mira.Server.Models;

namespace Mira.Server.GitHub;

public sealed record ApiResponse<T>(
    HttpStatusCode Status,
    T? Data,
    DateTimeOffset? TokenExpiresAt,
    string[]? Scopes,
    ErrorInfo? Error)
{
    public bool Ok => (int)Status is >= 200 and < 300;
}

/// <summary>
/// Client minimale dell'API REST di GitHub. Il token viene passato a ogni chiamata e non è mai
/// conservato qui. Da ogni risposta legge la scadenza del token e gli scope (solo token classic).
/// </summary>
public sealed class GitHubApi(IHttpClientFactory httpClientFactory, ILogger<GitHubApi> logger)
{
    private const string ExpirationHeader = "github-authentication-token-expiration";
    private const string ScopesHeader = "x-oauth-scopes";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    // GraphQL usa nomi camelCase.
    private static readonly JsonSerializerOptions GraphQlOptions = new(JsonSerializerDefaults.Web);

    public Task<ApiResponse<T>> GetAsync<T>(string token, string path, CancellationToken ct) =>
        SendAsync<T>(token, () => new HttpRequestMessage(HttpMethod.Get, path), JsonOptions, ct);

    /// <summary>
    /// Esegue una query GraphQL. GraphQL risponde 200 anche con errori parziali (es. un'organizzazione
    /// con SAML SSO non autorizzata): i dati disponibili restano in `Data`, gli errori in `Errors`.
    /// </summary>
    public Task<ApiResponse<GqlResponse<T>>> QueryAsync<T>(
        string token, string query, object? variables, CancellationToken ct) =>
        SendAsync<GqlResponse<T>>(
            token,
            () => new HttpRequestMessage(HttpMethod.Post, "graphql") { Content = JsonContent.Create(new { query, variables }) },
            GraphQlOptions,
            ct);

    private async Task<ApiResponse<T>> SendAsync<T>(
        string token, Func<HttpRequestMessage> build, JsonSerializerOptions options, CancellationToken ct)
    {
        try
        {
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClientFactory.CreateClient("github").SendAsync(request, ct);
            var expires = ParseExpiration(response, logger);
            var scopes = ParseScopes(response);

            if (!response.IsSuccessStatusCode)
                return new ApiResponse<T>(response.StatusCode, default, expires, scopes, Describe(response.StatusCode));

            var data = await response.Content.ReadFromJsonAsync<T>(options, ct);
            return new ApiResponse<T>(response.StatusCode, data, expires, scopes, null);
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<T>(0, default, null, null, new ErrorInfo("network", ex.Message));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ApiResponse<T>(0, default, null, null, new ErrorInfo("timeout"));
        }
    }

    private static ErrorInfo Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new ErrorInfo("unauthorized"),
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => new ErrorInfo("rate-limited"),
        _ => new ErrorInfo("github-http", ((int)status).ToString()),
    };

    // Esempi di valore: "2026-12-01 10:00:00 UTC" oppure "2026-12-01 10:00:00 +0100".
    private static readonly string[] ExpirationFormats = ["yyyy-MM-dd HH:mm:ss 'UTC'", "yyyy-MM-dd HH:mm:ss zzz"];

    private static DateTimeOffset? ParseExpiration(HttpResponseMessage response, ILogger logger)
    {
        if (!response.Headers.TryGetValues(ExpirationHeader, out var values))
            return null;

        var text = values.FirstOrDefault()?.Trim();
        // "zzz" vuole "+01:00": normalizza "+0100" aggiungendo i due punti.
        if (text is { Length: > 5 } && text[^5] is '+' or '-' && char.IsDigit(text[^1]))
            text = text[..^2] + ":" + text[^2..];

        if (DateTimeOffset.TryParseExact(
            text, ExpirationFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;

        logger.LogWarning("Formato di scadenza del token non riconosciuto: {Value}", values.FirstOrDefault());
        return null;
    }

    // Presente solo per i token classic; assente per i fine-grained.
    private static string[]? ParseScopes(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(ScopesHeader, out var values))
            return null;

        return string.Join(",", values)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
