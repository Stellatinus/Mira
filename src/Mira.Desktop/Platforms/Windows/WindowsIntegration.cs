using Microsoft.Win32;
using Mira.Server;

namespace Mira.Desktop.Integration;

/// <summary>
/// L'avvio all'accesso su Windows: un valore nella chiave «Run» dell'utente (nessun permesso di amministratore).
/// Mira parte con <c>--minimized</c>, quindi resta nella tray senza aprire la finestra.
/// </summary>
public sealed class WindowsIntegration : IDesktopIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mira";

    private static string Command => $"\"{Environment.ProcessPath}\" {WindowsShell.MinimizedFlag}";

    public bool AutostartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Se l'avvio all'accesso è attivo ma punta a un'altra cartella (Mira è stata spostata), lo riallinea.</summary>
    public void RefreshAutostartPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && current != Command)
            key.SetValue(ValueName, Command);
    }
}
