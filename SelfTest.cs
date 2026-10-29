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
