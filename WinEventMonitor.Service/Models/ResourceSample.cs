namespace WinEventMonitor.Service.Models;

/// <summary>
/// Muestra periódica de recursos del sistema (CPU/RAM/disco/red), tomada por
/// ResourceHistoryWorker cada pocos minutos y conservada varios días. Distinta
/// del buffer en memoria de SystemHealthService (2 min, se pierde al reiniciar).
/// </summary>
public class ResourceSample
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double CpuPct { get; set; }
    public double RamPct { get; set; }
    public long RamUsedMb { get; set; }
    public long RamTotalMb { get; set; }
    public double DiskUsedPct { get; set; }
    public long NetSentBytesSec { get; set; }
    public long NetRecvBytesSec { get; set; }
}
