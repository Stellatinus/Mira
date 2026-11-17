using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mira.Server;

namespace Mira.Desktop;

/// <summary>
/// Il server di Mira dentro il processo del guscio: stesso codice del programma autonomo (<see cref="MiraHost"/>),
/// stessa porta (MIRA_URLS, poi config.json, poi 5080). La finestra mostra <c>http://127.0.0.1:porta</c>.
/// </summary>
public static class MiraServer
{
    private static WebApplication? _app;

    /// <summary>Avvia il server e dà l'indirizzo su cui risponde. Porta occupata o config.json non valido: eccezione con un messaggio chiaro.</summary>
    public static async Task<Uri> StartAsync(IDesktopIntegration integration)
    {
        _app = MiraHost.Build([], services => services.AddSingleton(integration));
        await _app.StartAsync();
        return MiraHost.ListeningUrl(_app);
    }

    /// <summary>Ferma il server (alla chiusura vera dell'app, non quando la finestra va nella tray).</summary>
    public static async Task StopAsync()
    {
        if (_app is null)
            return;
        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
