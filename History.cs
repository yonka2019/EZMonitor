using System.IO;
using System.Runtime.InteropServices;

namespace EZMonitor;

/// <summary>Keeps the 24 h of samples on disk so a restart doesn't lose them.</summary>
public static class History
{
    const int Version = 1;

    public static readonly string Store = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZMonitor", "history.bin");

    /// <summary>Writes to a temp file first, so a crash mid-save keeps the previous file.</summary>
    public static void Save(string path, IReadOnlyDictionary<string, float[]> series, DateTime newestUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(Version);
            w.Write(newestUtc.Ticks);
            w.Write(series.Count);
            foreach (var (name, data) in series)
            {
                w.Write(name);
                w.Write(data.Length);
                w.Write(MemoryMarshal.AsBytes(data.AsSpan()));
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Fills the series from disk, then pads the time the app was closed with gaps.
    /// Missing or unreadable file = start empty.
    /// </summary>
    public static void Load(string path, IReadOnlyDictionary<string, Series> into, DateTime nowUtc)
    {
        DateTime newest;
        var read = new Dictionary<string, float[]>();
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            if (r.ReadInt32() != Version) return;
            newest = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            for (int k = r.ReadInt32(); k > 0; k--)
            {
                var name = r.ReadString();
                read[name] = MemoryMarshal.Cast<byte, float>(r.ReadBytes(r.ReadInt32() * sizeof(float))).ToArray();
            }
        }
        // Read fully before touching the series, so a damaged file leaves them empty and aligned
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return; }

        int longest = read.Values.Select(d => d.Length).DefaultIfEmpty().Max();
        foreach (var (name, s) in into)
        {
            // Keep every series the same length so index i is the same second in all of them
            if (read.TryGetValue(name, out var data)) { s.AddGap(longest - data.Length); foreach (var v in data) s.Add(v); }
            else s.AddGap(longest);
        }
        var closed = (int)Math.Clamp((nowUtc - newest).TotalSeconds - 1, 0, int.MaxValue);
        foreach (var s in into.Values) s.AddGap(closed);
    }
}
