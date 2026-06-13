using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class AlertRoutes
{
    public static void MapAlertRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/alerts");

        // GET /api/alerts?page=1&pageSize=50&severity=High&from=...&to=...
        group.MapGet("", async (
            EventDbContext db,
            int page = 1, int pageSize = 50,
            string? severity = null,
            DateTime? from = null, DateTime? to = null) =>
        {
            pageSize = Math.Min(pageSize, 200);
            var q = db.AlertEvents.AsNoTracking();

            if (!string.IsNullOrEmpty(severity)) q = q.Where(a => a.Severity == severity);
            if (from.HasValue) q = q.Where(a => a.Timestamp >= from.Value.ToUniversalTime());
            if (to.HasValue)   q = q.Where(a => a.Timestamp <= to.Value.ToUniversalTime());

            var total = await q.CountAsync();
            var data  = await q.OrderByDescending(a => a.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Results.Ok(new { data, total, page, pageSize });
        });

        // GET /api/alerts/count
        group.MapGet("/count", async (EventDbContext db) =>
        {
            var count = await db.AlertEvents.CountAsync();
            return Results.Ok(new { count });
        });

        // DELETE /api/alerts  — limpiar historial
        group.MapDelete("", async (EventDbContext db) =>
        {
            await db.AlertEvents.ExecuteDeleteAsync();
            return Results.Ok(new { message = "Historial de alertas eliminado" });
        });

        // GET /api/alerts/pids — mapa { pid: count } para badge en árbol de procesos
        group.MapGet("/pids", async (EventDbContext db) =>
        {
            var map = await db.AlertEvents.AsNoTracking()
                .Where(a => a.Pid.HasValue)
                .GroupBy(a => a.Pid!.Value)
                .Select(g => new { pid = g.Key, count = g.Count() })
                .ToListAsync();
            return Results.Ok(map.ToDictionary(x => x.pid, x => x.count));
        });
    }
}
