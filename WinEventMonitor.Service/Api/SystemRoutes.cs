using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class SystemRoutes
{
    public static void MapSystemRoutes(this WebApplication app)
    {
        app.MapGet("/api/system/health", (SystemHealthService sysHealth) =>
            Results.Ok(sysHealth.GetLatest()));

        app.MapGet("/api/system/history", (SystemHealthService sysHealth) =>
            Results.Ok(sysHealth.GetHistory()));

        // GET /api/system/history-long?range=1h|24h|7d
        // Historial persistido (ResourceSamples), para ver tendencias mas alla
        // de los 2 minutos que guarda SystemHealthService en memoria.
        app.MapGet("/api/system/history-long", async (EventDbContext db, string range = "24h") =>
        {
            var window = range switch
            {
                "1h"  => TimeSpan.FromHours(1),
                "7d"  => TimeSpan.FromDays(7),
                _     => TimeSpan.FromHours(24),
            };
            var cutoff = DateTime.UtcNow - window;

            var points = await db.ResourceSamples.AsNoTracking()
                .Where(r => r.Timestamp >= cutoff)
                .OrderBy(r => r.Timestamp)
                .ToListAsync();

            return Results.Ok(points);
        });
    }
}
