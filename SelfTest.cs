namespace EZMonitor;

/// <summary>Run with --selftest; exit code 0 = pass.</summary>
static class SelfTest
{
    public static int Run()
    {
        try
        {
            var s = new Series(3);
            foreach (var v in new float[] { 1, 2, 3, 4 }) s.Add(v);
            Check(s.Count == 3 && s[0] == 2 && s.Last == 4, "ring buffer wraps, oldest first");

            var t = new TraceView { A = s, B = s, Format = v => $"{v:0}", NameA = "read", NameB = "write" };
            Check(t.Describe(0) == "2 read, 2 write" && t.Describe(9) == "–", "readout joins both series");
            Check(TraceView.Ago(0) == "now" && TraceView.Ago(42) == "42 s ago" && TraceView.Ago(75) == "1 min 15 s ago", "time ago format");

            Check(Thermal.Of(20) == Thermal.Of(40) && Thermal.Of(100) == Thermal.Of(85), "thermal clamps at ends");
            Check(Thermal.Of(52.5f) != Thermal.Of(40) && Thermal.Of(52.5f) != Thermal.Of(65), "thermal blends between stops");
            Check(Thermal.Of(float.NaN) == Thermal.Unknown, "no reading is grey");

            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ezmonitor-selftest.bin");
            var now = DateTime.UtcNow;
            History.Save(file, new Dictionary<string, float[]> { ["a"] = [1, 2], ["b"] = [3] }, now.AddSeconds(-10));
            var (ha, hb, hc) = (new Series(100), new Series(100), new Series(100));
            History.Load(file, new Dictionary<string, Series> { ["a"] = ha, ["b"] = hb, ["c"] = hc }, now);
            System.IO.File.Delete(file);
            Check(ha.Count == 11 && hb.Count == 11 && hc.Count == 11, "history keeps series aligned and pads closed time");
            Check(ha[1] == 2 && float.IsNaN(hb[0]) && hb[1] == 3 && float.IsNaN(ha.Last), "history restores values");

            var pan = new TraceView { A = ha, Window = 5, Offset = 2 };
            pan.Follow(1);
            Check(pan.Offset == 3, "panned view stays on the same moment");

            var sampler = new AppSampler();
            var first = sampler.Read();
            var self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            Check(first.Cpu.Count == 0 && first.Ram.Count == AppHistory.Top, "first app read: memory only, top 10");
            Check(first.Ram.Zip(first.Ram.Skip(1)).All(p => p.First.Value >= p.Second.Value), "apps ranked highest first");
            for (var spin = DateTime.UtcNow.AddMilliseconds(300); DateTime.UtcNow < spin;) { } // burn CPU so this app ranks
            var second = sampler.Read();
            Check(second.Cpu.Any(a => a.Name == self) && second.Cpu.All(a => a.Value is > 0 and <= 100), "app CPU share measured");
            Check(AppSampler.Rank([new("a", 1), new("b", 0), new("c", 3)]).Select(a => a.Name).SequenceEqual(["c", "a"]), "rank drops zeros");

            var apps = new AppHistory(3);
            apps.Add(new([new("x", 5)], [new("y", 1e9f)]));
            apps.AddGap(1);
            apps.Add(new([new("z", 7), new("x", 2)], []));
            Check(apps.At(0, false).Count == 2 && apps.At(0, false)[0] == new AppUse("z", 7) && apps.At(1, false).Count == 0, "app history keeps rankings per second");
            Check(apps.At(2, true)[0] == new AppUse("y", 1e9f) && apps.At(3, false).Count == 0 && apps.Added == 3, "app history ages");
            apps.Add(null);
            Check(apps.Count == 3 && apps.At(2, false).Count == 0 && apps.At(1, false)[1].Name == "x", "app history wraps");
            AppHistory.Save(file, apps.Copy(), now.AddSeconds(-5));
            var back = new AppHistory(10);
            back.Load(file, now);
            System.IO.File.Delete(file);
            Check(back.Count == 7 && back.At(5, false)[0] == new AppUse("z", 7) && back.At(0, true).Count == 0, "app history saves and pads closed time");
            Check(MainWindow.Share(3.25f) == "3.3%" && MainWindow.Share(42f) == "42%" && MainWindow.Bytes(512 << 20) == "512 MB", "app value format");

            Check(MainWindow.AppRam(4f * (1 << 30), 32) == "4.0 GB · 13%" && MainWindow.AppRam(1 << 20, float.NaN) == "1 MB", "app memory share");
            MainWindow.WriteLive(file, 8, 32, [new("qemu", 4f * (1 << 30))]);
            var live = System.IO.File.ReadAllLines(file);
            System.IO.File.Delete(file);
            Check(live.Length == 3 && live[0].StartsWith("time\t") && live[1] == "ram\t8.0 GB of 32.0 GB · 25% used" && live[2] == "app\tqemu\t4.0 GB · 13%", "live file for the remote");

            Check(MainWindow.Rate(float.NaN) == "–" && MainWindow.Rate(512_000) == "512 KB/s" && MainWindow.Rate(12_340_000) == "12.3 MB/s", "rate format");
            Check(MainWindow.TempText(54.4f) == "54°", "temp format");
            return 0;
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "selftest.log"), ex.Message);
            return 1;
        }
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL: " + what);
    }
}
