using System;
using System.Drawing;
using System.Windows.Forms;

namespace Neko;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Neko",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
