using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EZMonitor;

public partial class MainWindow : Window
{
    const int HistorySeconds = 10 * 60; // one sample per second

    readonly Settings _settings = Settings.Load();
    readonly Series _cpuTemp, _gpuTemp, _cpuLoad, _gpuLoad, _ram, _vram, _diskRead, _diskWrite, _netDown, _netUp;
    readonly TraceView[] _traces;
    readonly Tray _tray;
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;
    bool _exiting;
    TraceView? _expanded;

    public MainWindow()
    {
        InitializeComponent();

        const int n = HistorySeconds;
        (_cpuTemp, _gpuTemp, _cpuLoad, _gpuLoad, _ram, _vram) = (new(n), new(n), new(n), new(n), new(n), new(n));
        (_diskRead, _diskWrite, _netDown, _netUp) = (new(n), new(n), new(n), new(n));
        Setup(CpuTempTrace, "CPU temperature", _cpuTemp, TempText);
        Setup(GpuTempTrace, "GPU temperature", _gpuTemp, TempText);
        Setup(CpuLoadTrace, "CPU load", _cpuLoad, Percent);
        Setup(GpuLoadTrace, "GPU load", _gpuLoad, Percent);
        Setup(RamTrace, "Memory", _ram, Gb);
        Setup(VramTrace, "Video memory", _vram, Gb);
        Setup(DiskTrace, "Disk", _diskRead, Rate, "read", _diskWrite, "write");
        Setup(NetTrace, "Network", _netDown, Rate, "down", _netUp, "up");
        _traces = [CpuTempTrace, GpuTempTrace, CpuLoadTrace, GpuLoadTrace, RamTrace, VramTrace, DiskTrace, NetTrace];
        foreach (var t in _traces) t.Expand += ExpandTrace;
        CloseExpanded.Click += (_, _) => CollapseTrace();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && _expanded is not null) { CollapseTrace(); e.Handled = true; } };

        CloseToTray.IsChecked = _settings.CloseToTray;
        CloseToTray.Click += (_, _) => { _settings.CloseToTray = CloseToTray.IsChecked == true; _settings.Save(); };
        StartWithWindows.IsChecked = Autostart.IsOn;
        StartWithWindows.Click += (_, _) =>
        {
            try { Autostart.Set(StartWithWindows.IsChecked == true); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "EZMonitor"); }
            StartWithWindows.IsChecked = Autostart.IsOn;
        };

        ShowNotice();
        _tray = new Tray(Open, Exit);
        _loop = Task.Run(Poll);
    }

    public void Open()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void Exit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _settings.CloseToTray) { e.Cancel = true; Hide(); return; }
        _stop.Cancel();
        _loop.Wait(TimeSpan.FromSeconds(2));
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    static void Setup(TraceView t, string title, Series a, Func<float, string> format, string nameA = "", Series? b = null, string nameB = "") =>
        (t.Title, t.A, t.Format, t.NameA, t.B, t.NameB) = (title, a, format, nameA, b, nameB);

    void ExpandTrace(TraceView t)
    {
        _expanded = t;
        ExpandedTitle.Text = t.Title;
        ShowExpanded();
        Overlay.Visibility = Visibility.Visible;
        CloseExpanded.Focus();
    }

    void CollapseTrace()
    {
        var t = _expanded;
        _expanded = null;
        Overlay.Visibility = Visibility.Collapsed;
        t?.Focus();
    }

    void ShowExpanded()
    {
        if (_expanded is null) return;
        Expanded.CopyFrom(_expanded); // copies each tick: memory scales change with the totals
        ExpandedValue.Text = _expanded.Describe(_expanded.A!.Count - 1);
        Expanded.InvalidateVisual();
    }

    void ShowNotice()
    {
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        NoticeText.Text = !admin ? "Run as administrator to read CPU, board and disk sensors."
            : !Sensors.DriverInstalled ? "CPU temperature, clock, power and board fans need the PawnIO driver. Install it from pawnio.eu, then restart EZMonitor."
            : "";
        Notice.Visibility = NoticeText.Text == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    async Task Poll()
    {
        using var sensors = new Sensors();
        await Dispatcher.InvokeAsync(() =>
        {
            CpuName.Text = sensors.Cpu?.Name ?? "Processor";
            GpuName.Text = sensors.Gpu?.Name ?? "Graphics";
        });
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                Snapshot s;
                try { s = sensors.Read(); }
                catch { continue; } // a sensor hiccup skips one sample, not the app
                await Dispatcher.InvokeAsync(() => Render(s));
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
    }

    void Render(Snapshot s)
    {
        _cpuTemp.Add(s.CpuTemp);
        _gpuTemp.Add(s.GpuTemp);
        _cpuLoad.Add(s.CpuLoad);
        _gpuLoad.Add(s.GpuLoad);
        _ram.Add(s.RamUsed);
        _vram.Add(s.VramUsed);
        _diskRead.Add(s.DiskRead);
        _diskWrite.Add(s.DiskWrite);
        _netDown.Add(s.NetDown);
        _netUp.Add(s.NetUp);

        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        MachineText.Text = $"{Environment.MachineName}, up {(int)up.TotalHours} h {up.Minutes} min";

        Temp(CpuTemp, s.CpuTemp);
        Temp(GpuTemp, s.GpuTemp);
        CpuClock.Text = float.IsNaN(s.CpuClock) ? "" : $"{s.CpuClock / 1000:0.0} GHz";
        CpuPower.Text = Watts(s.CpuPower);
        GpuClock.Text = float.IsNaN(s.GpuClock) ? "" : $"{s.GpuClock:0} MHz";
        GpuPower.Text = Watts(s.GpuPower);

        CpuLoad.Text = Percent(s.CpuLoad);
        GpuLoad.Text = Percent(s.GpuLoad);
        Ram.Text = Of(s.RamUsed, s.RamTotal);
        RamSub.Text = s.RamTotal > 0 ? Percent(100 * s.RamUsed / s.RamTotal) + " used" : "";
        RamTrace.Max = s.RamTotal;
        Vram.Text = Of(s.VramUsed, s.VramTotal);
        VramSub.Text = s.VramTotal > 0 ? Percent(100 * s.VramUsed / s.VramTotal) + " used" : "";
        VramTrace.Max = s.VramTotal;
        DiskRead.Text = Rate(s.DiskRead) + " read";
        DiskWrite.Text = Rate(s.DiskWrite) + " write";
        NetDown.Text = Rate(s.NetDown) + " down";
        NetUp.Text = Rate(s.NetUp) + " up";

        Drives.ItemsSource = View(s.Drives);
        Fans.ItemsSource = s.Fans.Count > 0 ? View(s.Fans) : View([new("", "None reported")]);
        Temps.ItemsSource = s.Temps.Count > 0 ? View(s.Temps) : View([new("", "None reported")]);

        foreach (var t in _traces) t.InvalidateVisual();
        ShowExpanded();

        var hottest = float.IsNaN(s.CpuTemp) ? s.GpuTemp : float.IsNaN(s.GpuTemp) ? s.CpuTemp : Math.Max(s.CpuTemp, s.GpuTemp);
        _tray.Update(hottest, $"EZMonitor\nCPU {TempText(s.CpuTemp)}  GPU {TempText(s.GpuTemp)}");
    }

    static void Temp(TextBlock block, float c)
    {
        block.Text = TempText(c);
        block.Foreground = Thermal.Brush(c);
    }

    static readonly Brush InkBrush = (Brush)Application.Current.FindResource("Ink");

    static List<object> View(IEnumerable<Reading> rows) =>
        rows.Select(r => (object)new { r.Name, r.Value, Brush = float.IsNaN(r.Celsius) ? InkBrush : Thermal.Brush(r.Celsius) }).ToList();

    public static string TempText(float c) => float.IsNaN(c) ? "–" : $"{c:0}°";
    static string Gb(float v) => float.IsNaN(v) ? "–" : $"{v:0.0} GB";
    static string Percent(float v) => float.IsNaN(v) ? "–" : $"{v:0}%";
    static string Watts(float w) => float.IsNaN(w) ? "" : $"{w:0} W";
    static string Of(float used, float total) => float.IsNaN(used) ? "–" : float.IsNaN(total) ? $"{used:0.0} GB" : $"{used:0.0} of {total:0.0} GB";

    public static string Rate(float bytesPerSec) => bytesPerSec switch
    {
        float.NaN => "–",
        >= 1e9f => $"{bytesPerSec / 1e9:0.00} GB/s",
        >= 1e6f => $"{bytesPerSec / 1e6:0.0} MB/s",
        _ => $"{bytesPerSec / 1e3:0} KB/s",
    };
}
