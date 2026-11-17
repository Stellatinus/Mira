using Mira.Desktop.Integration;

namespace Mira.Desktop.WinUI;

/// <summary>L'applicazione WinUI sotto a MAUI: qui si decide, prima di tutto, se questa è l'unica istanza.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        // WebView2 vuole una cartella scrivibile per i suoi dati: quella accanto all'eseguibile può stare in Program Files.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", WindowsShell.WebViewDataFolder);

        // Un'altra Mira è già aperta: la porta in primo piano ed esce senza avviare un secondo server.
        if (!SingleInstance.TryBecomeFirst())
            Environment.Exit(0);

        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
