using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EZMonitor;

/// <summary>One app's share: CPU in % of the whole CPU, or private memory in bytes.</summary>
public readonly record struct AppUse(string Name, float Value);

/// <summary>Top apps of one second, by CPU and by memory.</summary>
public sealed record AppTop(List<AppUse> Cpu, List<AppUse> Ram);

/// <summary>
/// Per-app CPU and private memory (Task Manager's "Memory") from one system call, no process handles.
/// Processes with the same name are summed. Not thread-safe; used only on the poll thread.
/// </summary>
public sealed class AppSampler
{
    const int ProcessInformation = 5, LengthMismatch = unchecked((int)0xC0000004);

    byte[] _buf = GC.AllocateArray<byte>(1 << 20, pinned: true); // pinned: entries point into it
    Dictionary<(long Pid, long Created), long> _cpu = []; // total CPU time last read, 100 ns
    long _last;

    [DllImport("ntdll.dll")]
    static extern int NtQuerySystemInformation(int infoClass, byte[] info, int length, out int needed);

    public AppTop Read()
    {
        int status;
        while ((status = NtQuerySystemInformation(ProcessInformation, _buf, _buf.Length, out var needed)) == LengthMismatch)
            _buf = GC.AllocateArray<byte>(needed + (64 << 10), pinned: true);
        if (status != 0) throw new IOException($"NtQuerySystemInformation 0x{status:X8}");

        var now = Stopwatch.GetTimestamp();
        // Wall time since the last read in 100 ns, times every core; 0 on the first read (no CPU yet)
        double capacity = _last == 0 ? 0 : (now - _last) * 1e7 / Stopwatch.Frequency * Environment.ProcessorCount;
        _last = now;

        long origin = Marshal.UnsafeAddrOfPinnedArrayElement(_buf, 0);
        var cpu = new Dictionary<(long, long), long>(_cpu.Count);
        var apps = new Dictionary<string, (double Cpu, double Ram)>(StringComparer.OrdinalIgnoreCase);
        for (int at = 0; ;)
        {
            // SYSTEM_PROCESS_INFORMATION, x64 layout
            var e = _buf.AsSpan(at);
            long pid = MemoryMarshal.Read<long>(e[80..]);
            int nameLength = MemoryMarshal.Read<ushort>(e[56..]);
            if (pid != 0 && nameLength > 0) // pid 0 = idle
            {
                var name = Encoding.Unicode.GetString(_buf, (int)(MemoryMarshal.Read<long>(e[64..]) - origin), nameLength);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                var key = (pid, MemoryMarshal.Read<long>(e[32..]));
                long total = MemoryMarshal.Read<long>(e[40..]) + MemoryMarshal.Read<long>(e[48..]);
                cpu[key] = total;
                // A process missing last time started since then: all its time is new
                double used = capacity > 0 ? total - _cpu.GetValueOrDefault(key) : 0;
                var (c, r) = apps.GetValueOrDefault(name);
                apps[name] = (c + used, r + MemoryMarshal.Read<long>(e[8..]));
            }
            int next = MemoryMarshal.Read<int>(e);
            if (next == 0) break;
            at += next;
        }
        _cpu = cpu;

        return new(
            capacity > 0 ? Rank(apps.Select(a => new AppUse(a.Key, (float)Math.Min(100, 100 * a.Value.Cpu / capacity)))) : [],
            Rank(apps.Select(a => new AppUse(a.Key, (float)a.Value.Ram))));
    }

    /// <summary>Highest first, at most AppHistory.Top, zeros left out.</summary>
    public static List<AppUse> Rank(IEnumerable<AppUse> apps) =>
        apps.Where(a => a.Value > 0).OrderByDescending(a => a.Value).Take(AppHistory.Top).ToList();
}

/// <summary>
/// Top apps for each second of the last 24 h, newest last, on the same clock as the series.
/// App names are stored once and referenced by number, so a second costs 120 bytes.
/// </summary>
public sealed class AppHistory(int capacity)
{
    public const int Top = 10;
    const int Slots = 2 * Top; // CPU ranking, then memory ranking
    const ushort None = ushort.MaxValue;
    const int Version = 1;

    public static readonly string Store = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZMonitor", "apps.bin");

    readonly ushort[] _ids = new ushort[capacity * Slots];
    readonly float[] _values = new float[capacity * Slots];
    readonly List<string> _names = [];
    readonly Dictionary<string, ushort> _lookup = [];
    int _start, _count;

    public int Count => _count;
    /// <summary>Seconds ever added; with an age it names one second for good.</summary>
    public long Added { get; private set; }

    /// <summary>null = no reading this second.</summary>
    public void Add(AppTop? top)
    {
        int row = Next();
        Fill(row, 0, top?.Cpu ?? []);
        Fill(row, Top, top?.Ram ?? []);
    }

    /// <summary>n seconds with no reading.</summary>
    public void AddGap(int n)
    {
        for (int k = Math.Min(n, capacity); k > 0; k--) Add(null);
    }

    /// <summary>Ranking of the second age seconds before the newest; empty = nothing recorded.</summary>
    public List<AppUse> At(int age, bool ram)
    {
        var list = new List<AppUse>(Top);
        if (age < 0 || age >= _count) return list;
        int first = (_start + _count - 1 - age) % capacity * Slots + (ram ? Top : 0);
        for (int k = first; k < first + Top && _ids[k] != None; k++) list.Add(new(_names[_ids[k]], _values[k]));
        return list;
    }

    int Next()
    {
        Added++;
        if (_count < capacity) return (_start + _count++) % capacity;
        var row = _start;
        _start = (_start + 1) % capacity;
        return row;
    }

    void Fill(int row, int slot, List<AppUse> apps)
    {
        for (int k = 0, i = row * Slots + slot; k < Top; k++, i++)
            (_ids[i], _values[i]) = k < apps.Count ? (Id(apps[k].Name), apps[k].Value) : (None, 0);
    }

    ushort Id(string name)
    {
        if (_lookup.TryGetValue(name, out var id)) return id;
        if (_names.Count >= None) return None; // 65k different names: stop learning new ones
        _lookup[name] = id = (ushort)_names.Count;
        _names.Add(name);
        return id;
    }

    /// <summary>Oldest first. Made on the UI thread, written by Save on any thread.</summary>
    public (ushort[] Ids, float[] Values, string[] Names) Copy()
    {
        var (ids, values) = (new ushort[_count * Slots], new float[_count * Slots]);
        for (int r = 0; r < _count; r++)
        {
            int from = (_start + r) % capacity * Slots;
            Array.Copy(_ids, from, ids, r * Slots, Slots);
            Array.Copy(_values, from, values, r * Slots, Slots);
        }
        return (ids, values, [.. _names]);
    }

    /// <summary>Keeps only the names still in use. Temp file first, like History.</summary>
    public static void Save(string path, (ushort[] Ids, float[] Values, string[] Names) copy, DateTime newestUtc)
    {
        var (ids, values, names) = copy;
        var used = new Dictionary<ushort, ushort>();
        var kept = new List<string>();
        var packed = new ushort[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == None) { packed[i] = None; continue; }
            if (!used.TryGetValue(ids[i], out var id)) { used[ids[i]] = id = (ushort)kept.Count; kept.Add(names[ids[i]]); }
            packed[i] = id;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(Version);
            w.Write(newestUtc.Ticks);
            w.Write(kept.Count);
            foreach (var n in kept) w.Write(n);
            w.Write(ids.Length / Slots);
            w.Write(MemoryMarshal.AsBytes(packed.AsSpan()));
            w.Write(MemoryMarshal.AsBytes(values.AsSpan()));
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Fills from disk, then pads the time the app was closed. Missing or unreadable file = start empty.</summary>
    public void Load(string path, DateTime nowUtc)
    {
        DateTime newest;
        string[] names;
        ushort[] ids;
        float[] values;
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            if (r.ReadInt32() != Version) return;
            newest = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            names = new string[r.ReadInt32()];
            for (int k = 0; k < names.Length; k++) names[k] = r.ReadString();
            int n = r.ReadInt32() * Slots;
            ids = MemoryMarshal.Cast<byte, ushort>(r.ReadBytes(n * sizeof(ushort))).ToArray();
            values = MemoryMarshal.Cast<byte, float>(r.ReadBytes(n * sizeof(float))).ToArray();
            if (ids.Length != n || values.Length != n) return; // cut short
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException) { return; }

        for (int r = 0; r < ids.Length / Slots; r++)
        {
            int row = Next();
            for (int k = 0, i = r * Slots, j = row * Slots; k < Slots; k++, i++, j++)
                (_ids[j], _values[j]) = ids[i] < names.Length ? (Id(names[ids[i]]), values[i]) : (None, 0);
        }
        AddGap((int)Math.Clamp((nowUtc - newest).TotalSeconds - 1, 0, int.MaxValue));
    }
}
