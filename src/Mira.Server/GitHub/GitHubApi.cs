using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
/// Quante richieste restano a un account per una risorsa di GitHub (<c>core</c>, <c>search</c>, <c>graphql</c>…) e quando
/// la finestra si azzera. GitHub lo dice in ogni risposta (X-RateLimit-*); la ricerca, per esempio, concede 30 richieste al minuto.
/// </summary>
public sealed record RateLimit(string Resource, int Limit, int Remaining, DateTimeOffset ResetsAt);

/// <summary>
/// Client minimale dell'API REST di GitHub. Il token viene passato a ogni chiamata e non è mai
/// conservato qui. Da ogni risposta legge la scadenza del token e gli scope (solo token classic).
/// </summary>
public sealed class GitHubApi(IHttpClientFactory httpClientFactory, ILogger<GitHubApi> logger)
{
    private const string ExpirationHeader = "github-authentication-token-expiration";
    private const string ScopesHeader = "x-oauth-scopes";

    /// <summary>L'ultima risposta di una lettura, con il suo ETag: se GitHub dice «non è cambiato» (304) si riusa senza riscaricarla.</summary>
    private sealed record CachedResponse(string ETag, string Body, DateTimeOffset? Expires, string[]? Scopes);

    /// <summary>Quante risposte si tengono in memoria (le più vecchie escono per prime) e quanto può pesare una sola.</summary>
    public const int MaxCachedResponses = 200;
    private const int MaxCachedBodyBytes = 1_000_000;

    private readonly object _etagLock = new();
    private readonly Dictionary<string, CachedResponse> _etags = new();
    private readonly Queue<string> _etagOrder = new();

    private CachedResponse? CachedFor(string key)
    {
        lock (_etagLock) return _etags.GetValueOrDefault(key);
    }

    private void Remember(string key, CachedResponse response)
    {
        if (response.Body.Length > MaxCachedBodyBytes) return;
        lock (_etagLock)
        {
            if (!_etags.ContainsKey(key)) _etagOrder.Enqueue(key);
            _etags[key] = response;
            while (_etags.Count > MaxCachedResponses && _etagOrder.TryDequeue(out var oldest))
                _etags.Remove(oldest);
        }
    }

    // Per ogni token (se ne tiene solo l'impronta) l'ultimo limite visto per ciascuna risorsa.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RateLimit>> _limits = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    // GraphQL usa nomi camelCase.
    private static readonly JsonSerializerOptions GraphQlOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// I limiti di richieste dell'account di questo token, come risultano dall'ultima risposta di ogni risorsa. Una finestra
    /// il cui azzeramento è già passato conta come piena: GitHub l'ha ripristinata.
    /// </summary>
    public IReadOnlyList<RateLimit> RateLimitsFor(string token)
    {
        if (!_limits.TryGetValue(KeyOf(token), out var byResource)) return [];

        var now = DateTimeOffset.UtcNow;
        return byResource.Values
            .Select(limit => limit.ResetsAt <= now ? limit with { Remaining = limit.Limit } : limit)
            .OrderBy(limit => limit.Resource, StringComparer.Ordinal)
            .ToList();
    }

    private static string KeyOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private void Track(string token, HttpResponseMessage response)
    {
        var limit = ParseRateLimit(response);
        if (limit is not null)
            _limits.GetOrAdd(KeyOf(token), _ => new ConcurrentDictionary<string, RateLimit>())[limit.Resource] = limit;
    }

    private static RateLimit? ParseRateLimit(HttpResponseMessage response)
    {
        if (!TryGetInt(response, "x-ratelimit-limit", out var limit) ||
            !TryGetInt(response, "x-ratelimit-remaining", out var remaining) ||
            !TryGetInt(response, "x-ratelimit-reset", out var reset))
            return null;

        var resource = response.Headers.TryGetValues("x-ratelimit-resource", out var values) ? values.FirstOrDefault() : null;
        return new RateLimit(string.IsNullOrWhiteSpace(resource) ? "core" : resource.Trim(), limit, remaining, DateTimeOffset.FromUnixTimeSeconds(reset));
    }

    private static bool TryGetInt(HttpResponseMessage response, string header, out int value)
    {
        value = 0;
        return response.Headers.TryGetValues(header, out var values) &&
               int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public Task<ApiResponse<T>> GetAsync<T>(string token, string path, CancellationToken ct) =>
        SendAsync<T>(token, () => new HttpRequestMessage(HttpMethod.Get, path), JsonOptions, ct, conditional: true);

    /// <summary>Una lettura senza token, per le informazioni pubbliche (per esempio l'ultima release di Mira): con il limite basso delle richieste anonime.</summary>
    public Task<ApiResponse<T>> GetAnonymousAsync<T>(string path, CancellationToken ct) =>
        SendAsync<T>(null, () => new HttpRequestMessage(HttpMethod.Get, path), JsonOptions, ct, conditional: true);

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

    /// <summary>
    /// Una richiesta che cambia qualcosa su GitHub e non restituisce dati (segna come letta, annulla la sottoscrizione…):
    /// risponde 204 o 205. `Data` è true se è andata a buon fine.
    /// </summary>
    public async Task<ApiResponse<bool>> ActAsync(string token, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClientFactory.CreateClient("github").SendAsync(request, ct);
            Track(token, response);
            return response.IsSuccessStatusCode
                ? new ApiResponse<bool>(response.StatusCode, true, null, null, null)
                : new ApiResponse<bool>(response.StatusCode, false, null, null, Describe(response));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<bool>(0, false, null, null, new ErrorInfo("network", ex.Message));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ApiResponse<bool>(0, false, null, null, new ErrorInfo("timeout"));
        }
    }

    /// <summary>
    /// Con <paramref name="conditional"/> la lettura è «condizionale»: se si ha già la risposta, si manda il suo ETag
    /// (<c>If-None-Match</c>) e se GitHub risponde 304 si riusa quella, e una 304 non conta per il limite di richieste.
    /// La chiave è l'impronta del token più l'indirizzo, quindi account diversi non si mescolano.
    /// </summary>
    private async Task<ApiResponse<T>> SendAsync<T>(
        string? token, Func<HttpRequestMessage> build, JsonSerializerOptions options, CancellationToken ct, bool conditional = false)
    {
        try
        {
            using var request = build();
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cacheKey = conditional ? (token is null ? "anonimo" : KeyOf(token)) + "|" + request.RequestUri : null;
            var cached = cacheKey is null ? null : CachedFor(cacheKey);
            if (cached is not null)
                request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);

            using var response = await httpClientFactory.CreateClient("github").SendAsync(request, ct);
            if (token is not null) Track(token, response);
            var expires = ParseExpiration(response, logger);
            var scopes = ParseScopes(response);

            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                // Non è cambiato: i dati sono quelli di prima; scadenza e scope, se la 304 non li ripete, anche.
                var reused = JsonSerializer.Deserialize<T>(cached.Body, options);
                return new ApiResponse<T>(HttpStatusCode.OK, reused, expires ?? cached.Expires, scopes ?? cached.Scopes, null);
            }

            if (!response.IsSuccessStatusCode)
                return new ApiResponse<T>(response.StatusCode, default, expires, scopes, Describe(response));

            if (cacheKey is not null && response.Headers.ETag is { } etag)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                Remember(cacheKey, new CachedResponse(etag.ToString(), body, expires, scopes));
                return new ApiResponse<T>(response.StatusCode, JsonSerializer.Deserialize<T>(body, options), expires, scopes, null);
            }

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

    private static ErrorInfo Describe(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => new ErrorInfo("unauthorized"),
        // Il dettaglio, quando c'è, è il momento (ISO 8601) in cui si potrà riprovare.
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => new ErrorInfo("rate-limited", RetryAt(response)),
        var status => new ErrorInfo("github-http", ((int)status).ToString()),
    };

    /// <summary>Quando riprovare: «Retry-After» (limite secondario) oppure l'azzeramento della finestra se le richieste sono finite.</summary>
    private static string? RetryAt(HttpResponseMessage response)
    {
        DateTimeOffset? at = null;
        if (response.Headers.RetryAfter is { } retry)
            at = retry.Date ?? (retry.Delta is { } delta ? DateTimeOffset.UtcNow + delta : null);
        else if (ParseRateLimit(response) is { Remaining: 0 } limit)
            at = limit.ResetsAt;

        return at?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

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
