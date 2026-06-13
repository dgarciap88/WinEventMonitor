using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace WinEventMonitor.Service.Services;

/// <summary>
/// Singleton: refresca métricas de sistema cada 2 s en background.
/// Expone GetLatest() con CPU total, RAM, disco y top procesos.
/// </summary>
public sealed class SystemHealthService : IDisposable
{
    private readonly int _coreCount = Environment.ProcessorCount;
    private readonly Timer _timer;
    private Dictionary<int, (TimeSpan cpu, DateTime at)> _prevCpu = new();
    private Dictionary<int, (ulong read, ulong write, DateTime at)> _prevIo = new();
    private volatile SystemSnapshot _latest = new();

    // Historial circular de hasta 60 puntos (≈2 min a 2 s/muestra)
    private const int HistoryCapacity = 60;
    private readonly Queue<HistoryPoint> _history = new();
    private readonly object _historyLock = new();

    // ── P/Invoke para IO counters ──────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters lpIoCounters);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    public SystemHealthService()
    {
        // Primera muestra con 1 s de delay para que el delta CPU arranque cuanto antes.
        _timer = new Timer(Sample, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    public SystemSnapshot GetLatest() => _latest;

    /// <summary>Últimas N muestras de CPU/RAM para sparklines (orden cronológico).</summary>
    public IReadOnlyList<HistoryPoint> GetHistory()
    {
        lock (_historyLock) return _history.ToList();
    }

    /// <summary>Devuelve un dict pid → métricas de la última muestra, para enriquecer LiveProcess.</summary>
    public IReadOnlyDictionary<int, ProcessMetric> GetMetricByPid() =>
        _latest.Processes.ToDictionary(p => p.Pid);

    // -------------------------------------------------------------------------
    private void Sample(object? _)
    {
        try
        {
            var cpu   = SampleCpuWmi();
            var ram   = SampleRamWmi();
            var disks = SampleDisks();
            var procs = SampleProcesses();

            _latest = new SystemSnapshot
            {
                Cpu        = new CpuInfo { TotalPercent = Math.Round(cpu, 1), CoreCount = _coreCount },
                Ram        = ram,
                Disk       = disks,
                Processes  = procs,
                GeneratedAt = DateTime.UtcNow,
            };

            // Acumular punto de historial
            var ramPct = ram.TotalMb > 0
                ? Math.Round((double)ram.UsedMb / ram.TotalMb * 100, 1)
                : 0;
            lock (_historyLock)
            {
                if (_history.Count >= HistoryCapacity) _history.Dequeue();
                _history.Enqueue(new HistoryPoint
                {
                    At     = DateTime.UtcNow,
                    CpuPct = Math.Round(cpu, 1),
                    RamPct = ramPct,
                });
            }
        }
        catch { /* swallow — tarea de fondo no crítica */ }
    }

    private static double SampleCpuWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LoadPercentage FROM Win32_Processor");
            double sum = 0; int count = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                sum += Convert.ToDouble(obj["LoadPercentage"]);
                count++;
            }
            return count == 0 ? 0 : sum / count;
        }
        catch { return 0; }
    }

    private static RamInfo SampleRamWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var totalKb = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                var freeKb  = Convert.ToInt64(obj["FreePhysicalMemory"]);
                return new RamInfo
                {
                    TotalMb = totalKb / 1024,
                    FreeMb  = freeKb / 1024,
                    UsedMb  = (totalKb - freeKb) / 1024,
                };
            }
        }
        catch { }
        return new RamInfo();
    }

    private static List<DiskInfo> SampleDisks() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => new DiskInfo
            {
                Name    = d.Name,
                TotalGb = Math.Round((double)d.TotalSize / (1024 * 1024 * 1024), 1),
                FreeGb  = Math.Round((double)d.AvailableFreeSpace / (1024 * 1024 * 1024), 1),
            })
            .ToList();

    private List<ProcessMetric> SampleProcesses()
    {
        var now     = DateTime.UtcNow;
        var nextCpu = new Dictionary<int, (TimeSpan cpu, DateTime at)>();
        var nextIo  = new Dictionary<int, (ulong read, ulong write, DateTime at)>();
        var metrics = new List<ProcessMetric>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var ws = p.WorkingSet64;

                // ── CPU delta ──────────────────────────────────────────────
                TimeSpan cpuTime;
                try { cpuTime = p.TotalProcessorTime; }
                catch { cpuTime = TimeSpan.Zero; }

                double cpuPct = 0;
                if (_prevCpu.TryGetValue(p.Id, out var prevCpu) && cpuTime >= prevCpu.cpu)
                {
                    var elapsed = (now - prevCpu.at).TotalSeconds;
                    if (elapsed > 0)
                        cpuPct = Math.Min(100,
                            (cpuTime - prevCpu.cpu).TotalSeconds / (elapsed * _coreCount) * 100);
                }
                nextCpu[p.Id] = (cpuTime, now);

                // ── IO delta ───────────────────────────────────────────────
                long ioReadSec = 0, ioWriteSec = 0;
                try
                {
                    if (GetProcessIoCounters(p.Handle, out var io))
                    {
                        nextIo[p.Id] = (io.ReadTransferCount, io.WriteTransferCount, now);
                        if (_prevIo.TryGetValue(p.Id, out var prevIo))
                        {
                            var elapsed = (now - prevIo.at).TotalSeconds;
                            if (elapsed > 0 && io.ReadTransferCount >= prevIo.read)
                            {
                                ioReadSec  = (long)((io.ReadTransferCount  - prevIo.read)  / elapsed);
                                ioWriteSec = (long)((io.WriteTransferCount - prevIo.write) / elapsed);
                            }
                        }
                    }
                }
                catch { /* sin acceso al handle */ }

                metrics.Add(new ProcessMetric
                {
                    Pid            = p.Id,
                    Name           = p.ProcessName,
                    CpuPercent     = Math.Round(cpuPct, 1),
                    WorkingSetMb   = ws / (1024 * 1024),
                    IoReadBytesSec = ioReadSec,
                    IoWriteBytesSec = ioWriteSec,
                });
            }
            catch { /* proceso ya terminado o sin permisos */ }
            finally { p.Dispose(); }
        }

        _prevCpu = nextCpu;
        _prevIo  = nextIo;
        // Devolver top 60 por CPU para el panel Sistema; el resto se usa vía GetMetricByPid()
        return metrics.OrderByDescending(m => m.CpuPercent).Take(60).ToList();
    }

    public void Dispose() => _timer.Dispose();
}

// ─── DTOs (serializado directamente en JSON desde el endpoint) ─────────────

public record SystemSnapshot
{
    public CpuInfo        Cpu         { get; init; } = new();
    public RamInfo        Ram         { get; init; } = new();
    public List<DiskInfo> Disk        { get; init; } = [];
    public List<ProcessMetric> Processes { get; init; } = [];
    public DateTime       GeneratedAt  { get; init; } = DateTime.UtcNow;
}

public record CpuInfo
{
    public double TotalPercent { get; init; }
    public int    CoreCount    { get; init; }
}

public record RamInfo
{
    public long TotalMb { get; init; }
    public long UsedMb  { get; init; }
    public long FreeMb  { get; init; }
}

public record DiskInfo
{
    public string Name    { get; init; } = string.Empty;
    public double TotalGb { get; init; }
    public double FreeGb  { get; init; }
}

public record ProcessMetric
{
    public int    Pid             { get; init; }
    public string Name            { get; init; } = string.Empty;
    public double CpuPercent      { get; init; }
    public long   WorkingSetMb    { get; init; }
    public long   IoReadBytesSec  { get; init; }
    public long   IoWriteBytesSec { get; init; }
}

public record HistoryPoint
{
    public DateTime At     { get; init; }
    public double   CpuPct { get; init; }
    public double   RamPct { get; init; }
}
