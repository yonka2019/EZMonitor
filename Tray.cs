using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace EZMonitor;

/// <summary>Tray icon that shows the hottest of CPU/GPU as a number in its heat colour.</summary>
public sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Icon _appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!;
    IntPtr _hicon;
    int _shown = int.MinValue;

    public Tray(Action open, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Icon = _appIcon, Text = "EZMonitor", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void Update(float temp, string tip)
    {
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
        var t = float.IsNaN(temp) ? int.MinValue : (int)Math.Round(temp);
        if (t == _shown) return;
        _shown = t;
        if (t == int.MinValue) { SetIcon(_appIcon, IntPtr.Zero); return; }

        int size = Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var c = Thermal.Of(temp);
            using var fill = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
            g.FillRectangle(fill, 0, 0, size, size);
            using var font = new Font("Bahnschrift", size * (t >= 100 ? 0.5f : 0.68f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(t.ToString(), font, Brushes.White, new RectangleF(0, 1, size, size), fmt);
        }
        var h = bmp.GetHicon();
        SetIcon(Icon.FromHandle(h), h);
    }

    void SetIcon(Icon icon, IntPtr handle)
    {
        _icon.Icon = icon;
        if (_hicon != IntPtr.Zero) DestroyIcon(_hicon);
        _hicon = handle;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_hicon != IntPtr.Zero) DestroyIcon(_hicon);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr handle);
}
