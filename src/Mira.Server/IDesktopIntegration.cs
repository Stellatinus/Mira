namespace Mira.Server;

/// <summary>
/// Ciò che il guscio desktop (<c>Mira.Desktop</c>) offre al server. Nel programma autonomo non è registrato:
/// l'interfaccia web lo scopre da <c>GET /api/desktop</c> e mostra le opzioni solo se c'è.
/// </summary>
public interface IDesktopIntegration
{
    /// <summary>Mira parte da sola quando l'utente accede al computer.</summary>
    bool AutostartEnabled { get; }

    /// <summary>Attiva o disattiva l'avvio all'accesso. Può lanciare se il sistema lo rifiuta.</summary>
    void SetAutostart(bool enabled);
}
