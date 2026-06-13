namespace WinEventMonitor.Service.Models;

/// <summary>
/// Proceso enriquecido: puede venir de WMI (live) o de la BD (histórico).
/// ParentPid == 0 significa sin padre conocido.
/// </summary>
public record LiveProcess(
    int Pid,
    int ParentPid,
    string Name,
    string? CommandLine,
    string? ExecutablePath,
    string? UserName,
    bool IsElevated,
    string? IntegrityLevel,
    string? Sha256,
    string Source,          // "live" | "db"
    double? CpuPercent   = null,  // solo en modo live, desde SystemHealthService
    long?   WorkingSetMb = null   // solo en modo live
);
