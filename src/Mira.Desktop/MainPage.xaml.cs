using System.Globalization;

namespace Mira.Desktop;

/// <summary>La finestra: una WebView sull'indirizzo del server interno di Mira.</summary>
public partial class MainPage : ContentPage
{
    private static readonly bool Italian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";

    private Uri? _origin;

    public MainPage()
    {
        InitializeComponent();
        StatusLabel.Text = Italian ? "Avvio di Mira…" : "Starting Mira…";
        QuitButton.Text = Italian ? "Esci" : "Quit";
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
#if WINDOWS
            _origin = await MiraServer.StartAsync(Integration.WindowsShell.Integration);
#else
            throw new PlatformNotSupportedException();
#endif
        }
        catch (Exception ex)
        {
            // Tipicamente la porta è occupata (un'altra Mira o un altro programma) o config.json non è valido.
            StatusLabel.Text = (Italian ? "Mira non è riuscita ad avviarsi: " : "Mira could not start: ") + ex.Message
                + (Italian ? "\n\nCambia la porta con la variabile MIRA_URLS o nel file config.json." : "\n\nChange the port with the MIRA_URLS variable or in config.json.");
            QuitButton.IsVisible = true;
            return;
        }

        Web.Source = _origin!.ToString();
        Web.IsVisible = true;
        StatusPanel.IsVisible = false;
    }

    /// <summary>Solo l'interfaccia di Mira si apre qui dentro; ogni altro indirizzo (GitHub…) va nel browser.</summary>
    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (_origin is null || !Uri.TryCreate(e.Url, UriKind.Absolute, out var target))
            return;
        if (target.Scheme is not ("http" or "https") || (target.Host == _origin.Host && target.Port == _origin.Port))
            return;

        e.Cancel = true;
        _ = Launcher.OpenAsync(target);
    }

    private void OnQuitClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        // Application.Quit chiuderebbe la finestra, che però va nella tray: si esce dalla via vera.
        Integration.WindowsShell.Quit();
#else
        Application.Current?.Quit();
#endif
    }
}
