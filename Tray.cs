using System.Drawing;
using Forms = System.Windows.Forms;

namespace EZMonitor;

/// <summary>Tray icon with the app icon; hover shows the CPU/GPU temperatures.</summary>
public sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon _icon;

    public Tray(Action open, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Text = "EZMonitor", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void Update(string tip) => _icon.Text = tip.Length > 127 ? tip[..127] : tip;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
