using System.Diagnostics;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Windowing;
using Mira.Server;

namespace Mira.Desktop.Integration;

/// <summary>
/// La parte di Windows del guscio: icona e titolo della finestra, chiusura che manda nella tray, icona nella
/// tray (apri, esci), avvio nascosto e apertura dei link esterni nel browser.
/// </summary>
public static class WindowsShell
{
    public const string MinimizedFlag = "--minimized";

    private static Tray.TrayIcon? _tray;
    private static AppWindow? _appWindow;
    private static Microsoft.UI.Xaml.Window? _native;
    private static bool _quitting;

    public static readonly WindowsIntegration Integration = new();

    public static string WebViewDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mira", "WebView2");

    private static bool StartMinimized => Environment.GetCommandLineArgs().Contains(MinimizedFlag);

    private static bool Italian => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";

    /// <summary>I link che escono da Mira (target=_blank, GitHub…) si aprono nel browser, non in una finestra di WebView2.</summary>
    public static void ConfigureWebView()
    {
        WebViewHandler.Mapper.AppendToMapping("MiraLinkEsterni", (handler, _) =>
        {
            handler.PlatformView.CoreWebView2Initialized += (view, _) =>
            {
                view.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    OpenExternal(e.Uri);
                };
            };
        });
    }

    public static void OpenExternal(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Collega la finestra di MAUI al sistema: da chiamare quando la finestra nativa esiste.</summary>
    public static void Attach(Microsoft.Maui.Controls.Window window)
    {
        window.Created += (_, _) =>
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
                return;

            _native = native;
            _appWindow = native.AppWindow;
            _appWindow.Title = "Mira";
            var icon = Path.Combine(AppContext.BaseDirectory, "mira.ico");
            if (File.Exists(icon))
                _appWindow.SetIcon(icon);

            // La X manda nella tray: Mira resta in esecuzione. Si esce solo dal menu della tray.
            _appWindow.Closing += (_, e) =>
            {
                if (_quitting)
                    return;
                e.Cancel = true;
                _appWindow.Hide();
            };

            SingleInstance.ShowRequested += ShowWindow;
            _tray = new Tray.TrayIcon(icon, Italian ? "Apri Mira" : "Open Mira", Italian ? "Esci" : "Quit", ShowWindow, Quit);
            Integration.RefreshAutostartPath();

            // Avvio all'accesso: la finestra resta nascosta. Si nasconde alla prima attivazione, perché MAUI
            // la attiva dopo la creazione e mostrerebbe di nuovo ciò che si è nascosto troppo presto.
            if (StartMinimized)
            {
                var hidden = false;
                native.Activated += (_, _) =>
                {
                    if (hidden)
                        return;
                    hidden = true;
                    _appWindow.Hide();
                };
            }
        };
    }

    public static void ShowWindow()
    {
        var native = _native;
        if (native is null)
            return;

        native.DispatcherQueue.TryEnqueue(() =>
        {
            _appWindow?.Show();
            if (_appWindow?.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
                presenter.Restore();
            native.Activate();
        });
    }

    /// <summary>Uscita vera: toglie l'icona, ferma il server e chiude l'app.</summary>
    public static async void Quit()
    {
        _quitting = true;
        if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
        try
        {
            await MiraServer.StopAsync();
        }
        finally
        {
            Environment.Exit(0);
        }
    }
}
