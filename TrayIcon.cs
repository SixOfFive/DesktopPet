using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Neko;

internal sealed record TrayMenuEntry(string Label, string ModelPath, BehaviorKind Behavior);
internal sealed record TrayMenuGroup(string Label, IEnumerable<TrayMenuEntry> Entries);
internal sealed record PetSelection(string ModelPath, BehaviorKind Behavior);

internal enum SizeChange { Half, Regular, Double }

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly List<(TrayMenuEntry Entry, ToolStripMenuItem Item)> _entryItems = new();

    public event EventHandler? ExitRequested;
    public event EventHandler<PetSelection>? PetSelected;
    public event EventHandler<SizeChange>? SizeChangeRequested;

    public TrayIcon(IEnumerable<TrayMenuGroup> groups, PetSelection? initial)
    {
        var menu = new ContextMenuStrip();
        bool anyGroup = false;

        foreach (var group in groups)
        {
            var groupItem = new ToolStripMenuItem(group.Label);
            foreach (var entry in group.Entries.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ToolStripMenuItem(entry.Label)
                {
                    Checked = initial != null
                              && string.Equals(entry.ModelPath, initial.ModelPath, StringComparison.OrdinalIgnoreCase)
                              && entry.Behavior == initial.Behavior,
                };
                var captured = entry;
                item.Click += (_, _) => OnEntryClicked(captured);
                _entryItems.Add((entry, item));
                groupItem.DropDownItems.Add(item);
            }
            if (groupItem.DropDownItems.Count > 0)
            {
                menu.Items.Add(groupItem);
                anyGroup = true;
            }
        }

        if (anyGroup)
            menu.Items.Add(new ToolStripSeparator());

        var sizeMenu = new ToolStripMenuItem("Size");
        sizeMenu.DropDownItems.Add("Half size", null, (_, _) => SizeChangeRequested?.Invoke(this, SizeChange.Half));
        sizeMenu.DropDownItems.Add("Regular size", null, (_, _) => SizeChangeRequested?.Invoke(this, SizeChange.Regular));
        sizeMenu.DropDownItems.Add("Double size", null, (_, _) => SizeChangeRequested?.Invoke(this, SizeChange.Double));
        menu.Items.Add(sizeMenu);
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

    private void OnEntryClicked(TrayMenuEntry entry)
    {
        foreach (var (e, item) in _entryItems)
            item.Checked = ReferenceEquals(e, entry);
        PetSelected?.Invoke(this, new PetSelection(entry.ModelPath, entry.Behavior));
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
