using System.ComponentModel;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;

namespace Cato;

public record StatsSample(double Cpu, double? Gpu, double RamPct, double? VramUsedGb, double? VramTotalGb, int? Fps, double? CpuTemp = null, double? GpuTemp = null);

/// CPU and GPU temperatures through LibreHardwareMonitor. GPU temperatures work as a normal user; the CPU's need
/// administrator rights plus the PawnIO driver (otherwise the library reports 0, which we treat as "not available").
sealed class TempReader : IDisposable
{
    readonly LibreHardwareMonitor.Hardware.Computer pc = new() { IsCpuEnabled = true, IsGpuEnabled = true };

    public static TempReader? TryOpen()
    {
        try { var r = new TempReader(); r.pc.Open(); return r; }
        catch (Exception ex) { Debug.WriteLine(ex); return null; } // the library throws a bit of everything when a driver is missing
    }

    public (double? Cpu, double? Gpu) Read()
    {
        double? cpu = null, gpu = null;
        try
        {
            foreach (var hw in pc.Hardware)
            {
                hw.Update();
                var temps = hw.Sensors.Where(s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature && s.Value is > 0).ToList();
                if (temps.Count == 0) continue;
                if (hw.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                    cpu = temps.FirstOrDefault(s => s.Name.Contains("Package") || s.Name.Contains("Tctl"))?.Value ?? temps.Max(s => s.Value);
                else // several GPUs (integrated + discrete): the hottest core is the one being used
                {
                    var t = temps.FirstOrDefault(s => s.Name == "GPU Core")?.Value ?? temps.Max(s => s.Value);
                    gpu = gpu == null ? t : Math.Max(gpu.Value, t!.Value);
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        return (cpu, gpu);
    }

    public void Dispose() { try { pc.Close(); } catch (Exception ex) { Debug.WriteLine(ex); } }
}

/// Samples system load for game mode. Call Sample() about once a second from a background thread.
sealed class SystemStats : IDisposable
{
    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    long lastIdle, lastKernel, lastUser;
    readonly PerformanceCounterCategory gpuCat = new("GPU Engine"), memCat = new("GPU Adapter Memory");
    readonly Dictionary<string, CounterSample> gpuPrev = new();
    readonly double? vramTotal = ReadVramTotal();
    readonly FpsCounter? fps = FpsCounter.TryStart();
    readonly Stopwatch since = Stopwatch.StartNew();

    TempReader? temps;
    bool tempsTried;

    /// `wantTemps`: whether the game bar shows a temperature right now; the sensor library only starts when it does.
    public StatsSample Sample(bool wantTemps)
    {
        var gpuLoad = GpuLoad();
        var vramGb = VramUsedGb();
        int? f = null;
        if (fps != null) { f = fps.Take(Native.ForegroundPid(), since.Elapsed.TotalSeconds); since.Restart(); }
        if (wantTemps && !tempsTried) { tempsTried = true; temps = TempReader.TryOpen(); }
        var (cpuT, gpuT) = wantTemps && temps != null ? temps.Read() : (null, null);
        return new(Cpu(), gpuLoad, Native.MemoryLoad(), vramGb, vramTotal, f, cpuT, gpuT);
    }

    double Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        long di = idle - lastIdle, total = kernel - lastKernel + user - lastUser; // kernel time includes idle
        (lastIdle, lastKernel, lastUser) = (idle, kernel, user);
        return total <= 0 ? 0 : Math.Clamp(100.0 * (total - di) / total, 0, 100);
    }

    /// 3D engine load: one bulk read of the whole category, rate computed against the previous read.
    double? GpuLoad()
    {
        try
        {
            var col = gpuCat.ReadCategory()["utilization percentage"];
            if (col == null) return null;
            double sum = 0;
            var seen = new HashSet<string>();
            foreach (InstanceData d in col.Values)
            {
                if (!d.InstanceName.EndsWith("engtype_3D")) continue;
                seen.Add(d.InstanceName);
                if (gpuPrev.TryGetValue(d.InstanceName, out var prev)) sum += CounterSample.Calculate(prev, d.Sample);
                gpuPrev[d.InstanceName] = d.Sample;
            }
            foreach (var gone in gpuPrev.Keys.Where(k => !seen.Contains(k)).ToList()) gpuPrev.Remove(gone); // process ended
            return Math.Min(100, sum);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            Debug.WriteLine(ex);
            return null; // no GPU counters on this system
        }
    }

    double? VramUsedGb()
    {
        try
        {
            var col = memCat.ReadCategory()["dedicated usage"];
            if (col == null) return null;
            long bytes = 0;
            foreach (InstanceData d in col.Values) bytes += d.RawValue;
            return bytes / 1073741824.0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    /// Dedicated VRAM of the biggest display adapter, from the driver's registry info.
    static double? ReadVramTotal()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return null;
            long max = 0;
            foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    var v = k?.GetValue("HardwareInformation.qwMemorySize");
                    long bytes = v switch { long l => l, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0), _ => 0 };
                    max = Math.Max(max, bytes);
                }
                catch (System.Security.SecurityException) { }
            }
            return max > 0 ? max / 1073741824.0 : null;
        }
        catch (Exception ex) { Debug.WriteLine(ex); return null; }
    }

    public void Dispose()
    {
        fps?.Dispose();
        temps?.Dispose();
    }
}

/// Counts DirectX Present calls per process with ETW. Windows only allows this as administrator.
sealed class FpsCounter : IDisposable
{
    static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"); // Microsoft-Windows-DXGI, Present_Start = 42
    static readonly Guid D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9"); // Microsoft-Windows-D3D9, Present_Start = 1
    readonly TraceEventSession session;
    readonly Dictionary<int, int> counts = new();
    readonly object gate = new();

    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static FpsCounter? TryStart()
    {
        if (!IsAdmin) return null;
        try { return new FpsCounter(); }
        catch (Exception ex) { Debug.WriteLine(ex); return null; }
    }

    FpsCounter()
    {
        session = new TraceEventSession("CatoFps") { StopOnDispose = true };
        session.EnableProvider(Dxgi);
        session.EnableProvider(D3d9);
        session.Source.AllEvents += e =>
        {
            if ((e.ProviderGuid == Dxgi && (int)e.ID == 42) || (e.ProviderGuid == D3d9 && (int)e.ID == 1))
                lock (gate) counts[e.ProcessID] = counts.GetValueOrDefault(e.ProcessID) + 1;
        };
        new Thread(() =>
        {
            try { session.Source.Process(); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }) { IsBackground = true, Name = "CatoFps" }.Start();
    }

    /// Frames per second presented by the given process since the last call.
    public int Take(int pid, double seconds)
    {
        lock (gate)
        {
            var n = counts.GetValueOrDefault(pid);
            counts.Clear();
            return seconds <= 0 ? 0 : (int)Math.Round(n / seconds);
        }
    }

    public void Dispose() => session.Dispose();
}
