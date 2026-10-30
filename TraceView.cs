using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EZMonitor;

/// <summary>
/// Strip-chart trace: newest sample at the right edge, filled under the line.
/// Hover shows the value under the mouse. Small view: click or Enter raises Expand.
/// Interactive view: drag pans, wheel zooms; arrows, +/- and End do the same from the keyboard.
/// </summary>
public sealed class TraceView : FrameworkElement
{
    public Series? A { get; set; }
    /// <summary>Optional second series, drawn as a lighter line (upload, writes).</summary>
    public Series? B { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Words after each value in the readout ("read", "write").</summary>
    public string NameA { get; set; } = "";
    public string NameB { get; set; } = "";
    public Func<float, string> Format { get; set; } = v => float.IsNaN(v) ? "–" : $"{v:0}";
    public double Min { get; set; }
    /// <summary>Top of the scale. 0 = auto from the visible peak.</summary>
    public double Max { get; set; }
    /// <summary>Lowest auto scale, so idle noise stays flat.</summary>
    public double Floor { get; set; } = 1;
    public Color Ink { get; set; } = Color.FromRgb(0x1F, 0x4E, 0x79);
    /// <summary>Colour the trace by the latest temperature.</summary>
    public bool Thermal { get; set; }
    /// <summary>Scale and clock labels (expanded view).</summary>
    public bool ShowScale { get; set; }
    public bool Interactive { get; set; }
    /// <summary>Seconds shown across the width.</summary>
    public int Window { get; set; } = DefaultWindow;
    /// <summary>Seconds between the right edge and the newest sample. 0 = live.</summary>
    public int Offset { get; set; }

    public const int DefaultWindow = 600, MinWindow = 60;
    const double Gutter = 22;

    public event Action<TraceView>? Expand;
    /// <summary>Hover, pan or zoom moved FocusAge.</summary>
    public event Action? Changed;

    static readonly Typeface Face = new("Bahnschrift");
    static readonly Brush Paper = Frozen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xEA)));
    static readonly Brush InkSoft = Frozen(new SolidColorBrush(Color.FromRgb(0x4E, 0x5D, 0x6B)));
    static readonly Brush Box = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x20, 0x2B)));
    Point? _hover;
    double? _dragX;
    int _dragOffset;

    public TraceView()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public void CopyFrom(TraceView s) =>
        (A, B, Title, NameA, NameB, Format, Min, Max, Floor, Ink, Thermal) =
        (s.A, s.B, s.Title, s.NameA, s.NameB, s.Format, s.Min, s.Max, s.Floor, s.Ink, s.Thermal);

    /// <summary>Values of sample i (0 = oldest), e.g. "12.3 MB/s read, 0.4 MB/s write".</summary>
    public string Describe(int i)
    {
        if (A is null || i < 0 || i >= A.Count) return "–";
        var text = $"{Format(A[i])} {NameA}".TrimEnd();
        // B fills in step with A, so the same index is the same second
        if (B is not null && i < B.Count) text += $", {Format(B[i])} {NameB}".TrimEnd();
        return text;
    }

    public static string Ago(int s) =>
        s <= 0 ? "now" : s < 60 ? $"{s} s ago" : s < 3600 ? $"{s / 60} min {s % 60} s ago" : $"{s / 3600} h {s / 60 % 60} min ago";

    double PlotHeight => ShowScale ? Math.Max(0, ActualHeight - Gutter) : ActualHeight;
    double Step => ActualWidth / (Window - 1);
    double XOf(int age) => ActualWidth - (age - Offset) * Step;
    int AgeAt(double x) => Offset + (int)Math.Round((ActualWidth - x) / Step);
    int MaxOffset => Math.Max(0, (A?.Count ?? 0) - Window);

    /// <summary>Second under the mouse, else the right edge.</summary>
    public int FocusAge => _hover is Point p && p.Y <= PlotHeight ? HoverAge(p.X) : Offset;

    /// <summary>Second under x. Zoomed out a pixel column holds many: the one with A's peak, as drawn.</summary>
    int HoverAge(double x)
    {
        int age = AgeAt(x), col = (int)Math.Floor(XOf(age)), best = age;
        float V(int a) => A is not null && a >= 0 && a < A.Count ? A[A.Count - 1 - a] : float.NaN;
        for (int dir = -1; dir <= 1; dir += 2)
            for (int a = age + dir; a >= Offset && (int)Math.Floor(XOf(a)) == col; a += dir)
                if (V(a) > V(best) || float.IsNaN(V(best))) best = a;
        return best;
    }

    public void Live()
    {
        (Window, Offset) = (DefaultWindow, 0);
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>New samples arrived: a panned view stays on the same moment, a live one keeps up.</summary>
    public void Follow(int added)
    {
        if (Offset == 0) return;
        Offset = Math.Min(Offset + added, MaxOffset);
        if (_dragX is not null) _dragOffset += added;
    }

    /// <summary>Scrolls back in time (negative = towards now).</summary>
    public void Pan(int seconds)
    {
        Offset = Math.Clamp(Offset + seconds, 0, MaxOffset);
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>factor &lt; 1 zooms in. Keeps the moment under atX in place.</summary>
    public void Zoom(double factor, double atX)
    {
        var fromRight = ActualWidth > 0 ? (ActualWidth - atX) / ActualWidth : 0;
        var anchor = Offset + fromRight * Window;
        Window = (int)Math.Clamp(Math.Round(Window * factor), MinWindow, Math.Max(MinWindow, A?.Capacity ?? Window));
        Offset = Math.Clamp((int)Math.Round(anchor - fromRight * Window), 0, MaxOffset);
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        _hover = p;
        if (_dragX is double x0 && IsMouseCaptured)
            Offset = Math.Clamp(_dragOffset + (int)Math.Round((p.X - x0) / Step), 0, MaxOffset);
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Interactive) return;
        (_dragX, _dragOffset) = (e.GetPosition(this).X, Offset);
        CaptureMouse();
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!Interactive) { Expand?.Invoke(this); return; }
        _dragX = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!Interactive) return;
        Zoom(e.Delta > 0 ? 0.8 : 1.25, e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!Interactive)
        {
            if (e.Key is Key.Enter or Key.Space) { Expand?.Invoke(this); e.Handled = true; }
            return;
        }
        switch (e.Key)
        {
            case Key.Left: Pan(Math.Max(1, Window / 10)); break;
            case Key.Right: Pan(-Math.Max(1, Window / 10)); break;
            case Key.Add or Key.OemPlus: Zoom(0.8, ActualWidth); break;
            case Key.Subtract or Key.OemMinus: Zoom(1.25, ActualWidth); break;
            case Key.End: Pan(-Offset); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = PlotHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight)); // hit-testable everywhere, not just on the line
        if (A is null || w < 2 || h < 2) return;
        var max = Max > 0 ? Max : Math.Max(Floor, Math.Max(Peak(A), B is null ? 0 : Peak(B)) * 1.15);
        var ink = Thermal ? EZMonitor.Thermal.Of(A.Last) : Ink;
        double Y(float v) => h - 1 - Math.Clamp((v - Min) / (max - Min), 0, 1) * (h - 2);

        if (B is not null) Draw(dc, B, Y, Color.FromArgb(0xB0, ink.R, ink.G, ink.B), fill: false);
        Draw(dc, A, Y, ink, fill: true);

        if (ShowScale)
        {
            dc.DrawText(Text(Format((float)max), InkSoft), new Point(8, 6));
            var bottom = Text(Format((float)Min), InkSoft);
            dc.DrawText(bottom, new Point(8, h - bottom.Height - 4));
            var left = Text(Clock(Offset + Window - 1), InkSoft);
            var mid = Text(Clock(Offset + Window / 2), InkSoft);
            var right = Text(Offset == 0 ? "now" : Clock(Offset), InkSoft);
            var y = h + (Gutter - left.Height) / 2;
            dc.DrawText(left, new Point(0, y));
            dc.DrawText(mid, new Point((w - mid.Width) / 2, y));
            dc.DrawText(right, new Point(w - right.Width, y));
        }

        if (_hover is not Point p || p.Y > h) return;
        int age = HoverAge(p.X), i = A.Count - 1 - age;
        if (i < 0 || i >= A.Count) return; // nothing recorded that far back
        var x = XOf(age);
        dc.DrawRectangle(Box, null, new Rect(x - 0.5, 0, 1, h));
        foreach (var (s, alpha) in new[] { (A, (byte)0xFF), (B, (byte)0xB0) })
            if (s is not null && i < s.Count && !float.IsNaN(s[i]))
                dc.DrawEllipse(Frozen(new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B))), null, new Point(x, Y(s[i])), 3.5, 3.5);

        var label = Text($"{Describe(i)}, {Clock(age)} ({Ago(age)})", Paper);
        double bw = label.Width + 16, bh = label.Height + 6;
        var bx = x + 10 + bw > w ? Math.Max(0, x - 10 - bw) : x + 10;
        var by = Math.Clamp(p.Y - bh - 8, 0, Math.Max(0, h - bh));
        dc.DrawRectangle(Box, null, new Rect(bx, by, bw, bh));
        dc.DrawText(label, new Point(bx + 8, by + 3));
    }

    // The newest sample is taken about now, one per second before it
    static string Clock(int age) => DateTime.Now.AddSeconds(-age).ToString("HH:mm:ss");

    FormattedText Text(string s, Brush brush) =>
        new(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>One point per pixel column (the column's peak), so 24 h draws as fast as 10 min.</summary>
    void Draw(DrawingContext dc, Series s, Func<float, double> y, Color ink, bool fill)
    {
        double h = PlotHeight;
        var runs = new List<List<Point>>();
        List<Point>? run = null;
        int col = int.MinValue;
        float peak = float.NaN;
        double colX = 0;

        void Emit()
        {
            if (col == int.MinValue) return;
            if (float.IsNaN(peak)) { run = null; return; }
            if (run is null) runs.Add(run = []);
            run.Add(new Point(colX, y(peak)));
        }

        for (int age = Math.Min(Offset + Window - 1, s.Count - 1); age >= Offset; age--)
        {
            var v = s[s.Count - 1 - age];
            var x = XOf(age);
            if ((int)Math.Floor(x) != col) { Emit(); col = (int)Math.Floor(x); peak = float.NaN; }
            if (!float.IsNaN(v) && (float.IsNaN(peak) || v > peak)) peak = v;
            colX = x;
        }
        Emit();

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (StreamGeometryContext l = line.Open(), a = area.Open())
        {
            foreach (var r in runs.Where(r => r.Count > 1))
            {
                l.BeginFigure(r[0], false, false);
                l.PolyLineTo(r.Skip(1).ToList(), true, true);
                a.BeginFigure(new Point(r[0].X, h), true, true);
                a.PolyLineTo([.. r, new Point(r[^1].X, h)], false, false);
            }
        }
        if (fill) dc.DrawGeometry(Frozen(new SolidColorBrush(Color.FromArgb(0x2E, ink.R, ink.G, ink.B))), null, area);
        dc.DrawGeometry(null, new Pen(Frozen(new SolidColorBrush(ink)), 1.6) { LineJoin = PenLineJoin.Round }, line);
    }

    /// <summary>Highest value in view.</summary>
    double Peak(Series s)
    {
        double p = 0;
        for (int age = Math.Min(Offset + Window - 1, s.Count - 1); age >= Offset; age--)
            if (s[s.Count - 1 - age] > p) p = s[s.Count - 1 - age];
        return p;
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
