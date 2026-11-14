using System.Text.RegularExpressions;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Storage;

namespace Mira.Server.Services;

/// <summary>
/// Le azioni sulle notifiche di GitHub: segnare come letta (una, o tutte quelle di un repository) e non seguire
/// più una conversazione rumorosa. Si fanno con il token dell'account a cui la notifica appartiene.
/// Ogni metodo restituisce l'errore, o null se è andata bene.
/// </summary>
public sealed partial class NotificationService(ISecretStore store, GitHubApi api)
{
    // Gli identificativi finiscono nell'indirizzo della richiesta: si accettano solo forme sicure.
    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex ThreadIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepoPartPattern();

    public static bool IsValidThreadId(string? id) => id is not null && ThreadIdPattern().IsMatch(id);

    public static bool IsValidRepoPart(string? part) =>
        part is not null && part is not "." and not ".." && RepoPartPattern().IsMatch(part);

    /// <summary>Segna come letta una notifica (<c>PATCH /notifications/threads/{id}</c>).</summary>
    public Task<ErrorInfo?> MarkReadAsync(string account, string threadId, CancellationToken ct) =>
        ActAsync(account, HttpMethod.Patch, $"notifications/threads/{threadId}", ct);

    /// <summary>Segna come lette tutte le notifiche di un repository (<c>PUT /repos/{owner}/{repo}/notifications</c>).</summary>
    public Task<ErrorInfo?> MarkRepositoryReadAsync(string account, string owner, string repo, CancellationToken ct) =>
        ActAsync(account, HttpMethod.Put, $"repos/{owner}/{repo}/notifications", ct);

    /// <summary>
    /// Smette di seguire la conversazione (<c>DELETE /notifications/threads/{id}/subscription</c>): GitHub non manda più
    /// notifiche finché non ci scrivi o non ti menzionano. Non la ignora per sempre.
    /// </summary>
    public Task<ErrorInfo?> UnsubscribeAsync(string account, string threadId, CancellationToken ct) =>
        ActAsync(account, HttpMethod.Delete, $"notifications/threads/{threadId}/subscription", ct);

    private async Task<ErrorInfo?> ActAsync(string account, HttpMethod method, string path, CancellationToken ct)
    {
        var tokens = await store.LoadAllAsync(ct);
        if (!tokens.TryGetValue(account, out var token))
            return new ErrorInfo("account-unknown");

        var response = await api.ActAsync(token, method, path, body: null, ct);
        return response.Ok ? null : response.Error ?? new ErrorInfo("unknown");
    }
}
