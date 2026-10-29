using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;

namespace EZMonitor;

public sealed record Reading(string Name, string Value);

/// <summary>One second of readings. NaN = sensor not available.</summary>
public sealed record Snapshot(
    float CpuTemp, float CpuLoad, float CpuClock, float CpuPower,
    float GpuTemp, float GpuLoad, float GpuClock, float GpuPower,
    float RamUsed, float RamTotal, float VramUsed, float VramTotal,
    float DiskRead, float DiskWrite, float NetDown, float NetUp,
    IReadOnlyList<Reading> Drives, IReadOnlyList<Reading> Fans);

/// <summary>Reads hardware sensors. Not thread-safe: call from one thread.</summary>
public sealed class Sensors : IDisposable
{
    readonly Computer _pc = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
        IsMotherboardEnabled = true, IsStorageEnabled = true,
    };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    long _rx = -1, _tx = -1;
    double _lastT;

    public IHardware? Cpu { get; }
    public IHardware? Gpu { get; }

    public Sensors()
    {
        _pc.Open();
        Cpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Prefer the discrete card over integrated graphics
        Gpu = _pc.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .OrderBy(h => h.HardwareType == HardwareType.GpuIntel).FirstOrDefault();
    }

    public static bool DriverInstalled => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;

    public Snapshot Read()
    {
        foreach (var h in _pc.Hardware) Update(h);
        var all = _pc.Hardware.SelectMany(Flatten).ToList();
        var ram = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual"));
        var storage = all.Where(h => h.HardwareType == HardwareType.Storage).ToList();
        var (down, up) = Network();
        float ramUsed = Get(ram, SensorType.Data, "Memory Used");

        var cpuCores = Cpu?.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core #") && s.Value > 0).ToList();
        return new Snapshot(
            CpuTemp: Get(Cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max"),
            CpuLoad: Get(Cpu, SensorType.Load, "CPU Total"),
            CpuClock: cpuCores is { Count: > 0 } ? cpuCores.Average(s => s.Value!.Value) : float.NaN,
            CpuPower: Positive(Get(Cpu, SensorType.Power, "CPU Package", "Package")),
            GpuTemp: Get(Gpu, SensorType.Temperature, "GPU Core"),
            GpuLoad: Get(Gpu, SensorType.Load, "GPU Core"),
            GpuClock: Get(Gpu, SensorType.Clock, "GPU Core"),
            GpuPower: Positive(Get(Gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Core")),
            RamUsed: ramUsed,
            RamTotal: ramUsed + Get(ram, SensorType.Data, "Memory Available"),
            VramUsed: Get(Gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used") / 1024,
            VramTotal: Get(Gpu, SensorType.SmallData, "GPU Memory Total") / 1024,
            DiskRead: Sum(storage, SensorType.Throughput, "Read Rate"),
            DiskWrite: Sum(storage, SensorType.Throughput, "Write Rate"),
            NetDown: down, NetUp: up,
            Drives: Drives(),
            Fans: all.SelectMany(h => h.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value is not null)
                .Select(s => new Reading(Label(h, s), $"{s.Value:0} rpm"))).ToList());
    }

    static string Label(IHardware h, ISensor s) => s.Name.StartsWith("Temperature") || s.Name.StartsWith("Composite")
        ? h.Name + s.Name.Replace("Temperature", "").Replace("Composite", "")
        : h.HardwareType is HardwareType.SuperIO or HardwareType.Motherboard ? $"Board {s.Name}" : s.Name;

    static List<Reading> Drives() => DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0)
        .Select(d => new Reading($"{d.Name.TrimEnd('\\')} {d.TotalFreeSpace / 1e9:0} GB free",
            $"{100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize:0}% used"))
        .ToList();

    /// <summary>Bytes/s over interfaces that reach a gateway (skips loopback and idle virtual adapters).</summary>
    (float Down, float Up) Network()
    {
        long rx = 0, tx = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (n.GetIPProperties().GatewayAddresses.Count == 0) continue;
            var st = n.GetIPStatistics();
            rx += st.BytesReceived;
            tx += st.BytesSent;
        }
        var t = _clock.Elapsed.TotalSeconds;
        var dt = t - _lastT;
        var result = _rx < 0 || dt <= 0 ? (float.NaN, float.NaN) : ((float)(Math.Max(0, rx - _rx) / dt), (float)(Math.Max(0, tx - _tx) / dt));
        (_rx, _tx, _lastT) = (rx, tx, t);
        return result;
    }

    static void Update(IHardware h)
    {
        h.Update();
        foreach (var sub in h.SubHardware) Update(sub);
    }

    static IEnumerable<IHardware> Flatten(IHardware h) => h.SubHardware.SelectMany(Flatten).Prepend(h);

    /// <summary>First named sensor with a value, in order of preference.</summary>
    static float Get(IHardware? h, SensorType type, params string[] names)
    {
        if (h is null) return float.NaN;
        foreach (var name in names)
            if (h.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value is float v) return v;
        return float.NaN;
    }

    static float Sum(List<IHardware> hw, SensorType type, string name)
    {
        var vals = hw.Select(h => Get(h, type, name)).Where(v => !float.IsNaN(v)).ToList();
        return vals.Count == 0 ? float.NaN : vals.Sum();
    }

    // Without the driver some power sensors report 0 instead of nothing
    static float Positive(float v) => v > 0 ? v : float.NaN;

    public void Dispose() => _pc.Close();
}
