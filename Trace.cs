using System.Windows;
using System.Windows.Media;

namespace EZMonitor;

/// <summary>Fixed-size ring buffer of samples, oldest first. NaN = no reading.</summary>
public sealed class Series(int capacity)
{
    readonly float[] _buf = new float[capacity];
    int _start, _count;

    public int Capacity => _buf.Length;
    public int Count => _count;
    public float this[int i] => _buf[(_start + i) % _buf.Length];
    public float Last => _count == 0 ? float.NaN : this[_count - 1];

    public void Add(float v)
    {
        if (_count < _buf.Length) { _buf[(_start + _count++) % _buf.Length] = v; return; }
        _buf[_start] = v;
        _start = (_start + 1) % _buf.Length;
    }

    /// <summary>n seconds with no reading (app was closed).</summary>
    public void AddGap(int n)
    {
        for (int k = Math.Min(n, _buf.Length); k > 0; k--) Add(float.NaN);
    }

    public float[] ToArray()
    {
        var a = new float[_count];
        for (int i = 0; i < _count; i++) a[i] = this[i];
        return a;
    }
}

/// <summary>Temperature → colour: steel blue when cool, ochre when warm, red when hot.</summary>
public static class Thermal
{
    static readonly (float At, Color C)[] Stops =
    [
        (40, Color.FromRgb(0x2F, 0x7F, 0xA8)),
        (65, Color.FromRgb(0xC9, 0x8A, 0x12)),
        (85, Color.FromRgb(0xC2, 0x3B, 0x22)),
    ];

    public static readonly Color Unknown = Color.FromRgb(0x4E, 0x5D, 0x6B);

    public static Color Of(float c)
    {
        if (float.IsNaN(c)) return Unknown;
        if (c <= Stops[0].At) return Stops[0].C;
        for (int i = 1; i < Stops.Length; i++)
        {
            if (c > Stops[i].At) continue;
            var (a, b) = (Stops[i - 1], Stops[i]);
            var t = (c - a.At) / (b.At - a.At);
            return Color.FromRgb(Mix(a.C.R, b.C.R, t), Mix(a.C.G, b.C.G, t), Mix(a.C.B, b.C.B, t));
        }
        return Stops[^1].C;
    }

    public static Brush Brush(float c)
    {
        var b = new SolidColorBrush(Of(c));
        b.Freeze();
        return b;
    }

    static byte Mix(byte a, byte b, float t) => (byte)Math.Round(a + (b - a) * t);
}
