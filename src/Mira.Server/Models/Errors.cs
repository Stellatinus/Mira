namespace Mira.Server.Models;

/// <summary>
/// Errore comprensibile all'utente. Il backend non scrive frasi: manda un codice stabile
/// (es. "token-invalid") e il frontend lo traduce nella lingua scelta.
/// Detail è un dato libero (es. il messaggio di rete); Cause è l'errore di GitHub che l'ha provocato.
/// </summary>
public sealed record ErrorInfo(string Code, string? Detail = null, ErrorInfo? Cause = null);

/// <summary>Un errore avvenuto leggendo una sezione di un account. Section può essere "account".</summary>
public sealed record DashboardError(string Account, string Section, ErrorInfo Error);

/// <summary>Eccezione il cui codice può essere mostrato all'utente (tradotto) al posto di un errore generico.</summary>
public sealed class MiraException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
