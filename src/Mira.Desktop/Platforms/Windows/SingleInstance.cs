namespace Mira.Desktop.Integration;

/// <summary>
/// Un'unica istanza di Mira per utente: la seconda, invece di aprirsi, chiede alla prima di mostrare la finestra
/// (un evento con nome) ed esce.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _show;

    /// <summary>La seconda istanza ha chiesto di mostrare la finestra (arriva da un altro thread).</summary>
    public static event Action? ShowRequested;

    /// <summary>true se questa è la prima istanza; altrimenti avvisa la prima e dà false.</summary>
    public static bool TryBecomeFirst()
    {
        _mutex = new Mutex(true, @"Local\Mira.Desktop.Instance", out var first);
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Mira.Desktop.Show");

        if (!first)
        {
            _show.Set();
            return false;
        }

        new Thread(() =>
        {
            while (true)
            {
                _show.WaitOne();
                ShowRequested?.Invoke();
            }
        })
        { IsBackground = true, Name = "Mira istanza unica" }.Start();
        return true;
    }
}
