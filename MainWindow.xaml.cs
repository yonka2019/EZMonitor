using System.ComponentModel;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EZMonitor;

public partial class MainWindow : Window
{
    const int HistorySeconds = 24 * 3600; // one sample per second
    const int SaveEvery = 5 * 60; // samples between history saves; the file is ≈3.5 MB

    readonly Settings _settings = Settings.Load();
    readonly Series _cpuTemp, _gpuTemp, _cpuLoad, _gpuLoad, _ram, _vram, _diskRead, _diskWrite, _netDown, _netUp;
    readonly Dictionary<string, Series> _history;
    readonly TraceView[] _traces;
    readonly Tray _tray;
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;
    Task _saving = Task.CompletedTask;
    DateTime _lastSample;
    int _unsaved;
    bool _exiting;
    TraceView? _expanded;

    public MainWindow()
    {
        InitializeComponent();

        const int n = HistorySeconds;
        (_cpuTemp, _gpuTemp, _cpuLoad, _gpuLoad, _ram, _vram) = (new(n), new(n), new(n), new(n), new(n), new(n));
        (_diskRead, _diskWrite, _netDown, _netUp) = (new(n), new(n), new(n), new(n));
        _history = new()
        {
            ["cpuTemp"] = _cpuTemp, ["gpuTemp"] = _gpuTemp, ["cpuLoad"] = _cpuLoad, ["gpuLoad"] = _gpuLoad, ["ram"] = _ram,
            ["vram"] = _vram, ["diskRead"] = _diskRead, ["diskWrite"] = _diskWrite, ["netDown"] = _netDown, ["netUp"] = _netUp,
        };
        History.Load(History.Store, _history, DateTime.UtcNow);
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
        LiveExpanded.Click += (_, _) => { Expanded.Pan(-Expanded.Offset); Expanded.Focus(); };
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
        _saving.Wait(TimeSpan.FromSeconds(5));
        try { SaveHistory(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    static void Setup(TraceView t, string title, Series a, Func<float, string> format, string nameA = "", Series? b = null, string nameB = "") =>
        (t.Title, t.A, t.Format, t.NameA, t.B, t.NameB) = (title, a, format, nameA, b, nameB);

    void ExpandTrace(TraceView t)
    {
        _expanded = t;
        ExpandedTitle.Text = t.Title;
        Expanded.Live();
        ShowExpanded();
        Overlay.Visibility = Visibility.Visible;
        Expanded.Focus(); // arrows, +/- and End work straight away
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

    /// <summary>Copies on the UI thread, writes in the background.</summary>
    void SaveHistory(bool background = false)
    {
        var data = _history.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        var newest = _lastSample == default ? DateTime.UtcNow : _lastSample;
        if (!background) { History.Save(History.Store, data, newest); return; }
        if (!_saving.IsCompleted) return;
        _saving = Task.Run(() =>
        {
            try { History.Save(History.Store, data, newest); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // next save retries
        });
    }

    void Render(Snapshot s)
    {
        // Sleep or a stalled sensor read: pad with gaps so each index stays one second
        var now = DateTime.UtcNow;
        var missed = _lastSample == default ? 0 : Math.Max(0, (int)((now - _lastSample).TotalSeconds - 1.5));
        foreach (var series in _history.Values) series.AddGap(missed);
        _lastSample = now;

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
        CpuStats.Text = Stats(float.IsNaN(s.CpuClock) ? "" : $"{s.CpuClock / 1000:0.0} GHz", Watts(s.CpuPower));
        GpuStats.Text = Stats(float.IsNaN(s.GpuClock) ? "" : $"{s.GpuClock:0} MHz", Watts(s.GpuPower));

        CpuLoad.Text = Percent(s.CpuLoad);
        GpuLoad.Text = Percent(s.GpuLoad);
        Ram.Text = Gb(s.RamUsed);
        RamSub.Text = Of(s.RamUsed, s.RamTotal);
        RamTrace.Max = s.RamTotal;
        Vram.Text = Gb(s.VramUsed);
        VramSub.Text = Of(s.VramUsed, s.VramTotal);
        VramTrace.Max = s.VramTotal;
        DiskRead.Text = Rate(s.DiskRead);
        DiskWrite.Text = Rate(s.DiskWrite) + " write";
        NetDown.Text = Rate(s.NetDown);
        NetUp.Text = Rate(s.NetUp) + " up";

        Drives.ItemsSource = s.Drives;
        Fans.ItemsSource = s.Fans.Count > 0 ? s.Fans : [new Reading("", "None reported")];

        foreach (var t in _traces) t.InvalidateVisual();
        Expanded.Follow(missed + 1);
        ShowExpanded();
        if (++_unsaved >= SaveEvery) { _unsaved = 0; SaveHistory(background: true); }

        _tray.Update($"EZMonitor\nCPU {TempText(s.CpuTemp)}  GPU {TempText(s.GpuTemp)}");
    }

    static void Temp(TextBlock block, float c)
    {
        block.Text = TempText(c);
        block.Foreground = Thermal.Brush(c);
    }

    public static string TempText(float c) => float.IsNaN(c) ? "–" : $"{c:0}°";
    static string Gb(float v) => float.IsNaN(v) ? "–" : $"{v:0.0} GB";
    static string Percent(float v) => float.IsNaN(v) ? "–" : $"{v:0}%";
    static string Watts(float w) => float.IsNaN(w) ? "" : $"{w:0} W";
    static string Stats(params string[] parts) => string.Join(" · ", parts.Where(p => p != ""));
    /// <summary>"of 23.9 GB · 71% used"</summary>
    static string Of(float used, float total) => float.IsNaN(used) || !(total > 0) ? "" : $"of {total:0.0} GB · {100 * used / total:0}% used";

    public static string Rate(float bytesPerSec) => bytesPerSec switch
    {
        float.NaN => "–",
        >= 1e9f => $"{bytesPerSec / 1e9:0.00} GB/s",
        >= 1e6f => $"{bytesPerSec / 1e6:0.0} MB/s",
        _ => $"{bytesPerSec / 1e3:0} KB/s",
    };
}
