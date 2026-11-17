namespace Mira.Desktop.Tray;

/// <summary>L'icona di Mira nella tray: clic sinistro e «Apri» mostrano la finestra, «Esci» chiude Mira.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(string iconPath, string openText, string quitText, Action open, Action quit)
    {
        var menu = new ContextMenuStrip();
        var openItem = menu.Items.Add(openText);
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => open();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitText).Click += (_, _) => quit();

        _icon = new NotifyIcon
        {
            Text = "Mira",
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                open();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
