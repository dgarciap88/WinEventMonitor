using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WinEventMonitor.Service.Models;

/// <summary>
/// Alerta generada por el motor de detección. Persistida en SQLite.
/// </summary>
public class AlertEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Severity { get; set; } = "Low";   // "High" | "Medium" | "Low"
    public string Rule { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Pid { get; set; }
    public string? ProcessName { get; set; }
    public string? Details { get; set; }
    public string? MitreTechnique { get; set; }
}
