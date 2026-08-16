using System.ComponentModel.DataAnnotations;

namespace WinEventMonitor.Service.Models;

/// <summary>
/// Excepción de alerta: "no vuelvas a avisarme de esta regla" (ProcessName null)
/// o "no vuelvas a avisarme de esta regla para este proceso concreto".
/// Consultada por AlertService.AddAsync antes de persistir cada alerta nueva.
/// </summary>
public class AlertException
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Rule { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
