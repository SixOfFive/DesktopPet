using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Neko;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Dictionary<string, ToolStripMenuItem> _modelItems = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? ModelChangeRequested;

    public TrayIcon(IEnumerable<(string Name, string Path)> models, string? currentPath)
    {
        var menu = new ContextMenuStrip();

        foreach (var (name, path) in models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ToolStripMenuItem(name) { Checked = string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase) };
            var capturedPath = path;
            item.Click += (_, _) => OnModelChosen(capturedPath);
            _modelItems[path] = item;
            menu.Items.Add(item);
        }

        if (_modelItems.Count > 0)
            menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Neko",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    private void OnModelChosen(string path)
    {
        foreach (var kv in _modelItems)
            kv.Value.Checked = string.Equals(kv.Key, path, StringComparison.OrdinalIgnoreCase);
        ModelChangeRequested?.Invoke(this, path);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
