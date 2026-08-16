using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Services;

/// <summary>
/// Gestiona las alertas de detección: persiste en SQLite y mantiene
/// un buffer en memoria para respuesta rápida al badge de la UI.
/// </summary>
public class AlertService
{
    private readonly IDbContextFactory<EventDbContext> _dbFactory;
    private readonly ILogger<AlertService> _logger;

    // Buffer en memoria para el badge (recuento rápido sin hit a BD)
    private int _memCount = 0;
    private readonly object _lock = new();

    public AlertService(IDbContextFactory<EventDbContext> dbFactory, ILogger<AlertService> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    public async Task AddAsync(AlertEvent alert)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            // Excepcion: regla+proceso marcados como "confio en esto" no se vuelven
            // a persistir. ProcessName null en la excepcion = silencia la regla entera.
            var suppressed = await db.AlertExceptions.AsNoTracking().AnyAsync(x =>
                x.Rule == alert.Rule && (x.ProcessName == null || x.ProcessName == alert.ProcessName));
            if (suppressed) return;

            db.AlertEvents.Add(alert);
            await db.SaveChangesAsync();
            lock (_lock) _memCount++;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting alert {Rule}", alert.Rule);
        }
    }

    /// <summary>Recuento rápido (en memoria desde arranque). No incluye alertas de sesiones anteriores.</summary>
    public int Count() { lock (_lock) return _memCount; }
}
