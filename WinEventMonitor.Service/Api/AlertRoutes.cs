using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class AlertRoutes
{
    private static readonly string[] ValidStatuses = ["New", "Reviewed", "Dismissed", "Trusted"];

    public static void MapAlertRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/alerts");

        // GET /api/alerts?page=1&pageSize=50&severity=High&status=New&from=...&to=...
        group.MapGet("", async (
            EventDbContext db,
            int page = 1, int pageSize = 50,
            string? severity = null,
            string? status = null,
            DateTime? from = null, DateTime? to = null) =>
        {
            pageSize = Math.Min(pageSize, 200);
            var q = db.AlertEvents.AsNoTracking();

            if (!string.IsNullOrEmpty(severity)) q = q.Where(a => a.Severity == severity);
            if (!string.IsNullOrEmpty(status))   q = q.Where(a => a.Status == status);
            if (from.HasValue) q = q.Where(a => a.Timestamp >= from.Value.ToUniversalTime());
            if (to.HasValue)   q = q.Where(a => a.Timestamp <= to.Value.ToUniversalTime());

            var total = await q.CountAsync();
            var data  = await q.OrderByDescending(a => a.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Results.Ok(new { data, total, page, pageSize });
        });

        // PATCH /api/alerts/{id} — cambia el estado de una alerta (New/Reviewed/Dismissed/Trusted).
        // "Trusted" ademas crea una AlertException para Rule+ProcessName: no se volvera a
        // generar esa alerta para ese proceso (o para ningun proceso si ProcessName es null).
        group.MapPatch("/{id:guid}", async (Guid id, AlertStatusPatch body, EventDbContext db) =>
        {
            if (!ValidStatuses.Contains(body.Status))
                return Results.BadRequest(new { detail = $"Status debe ser: {string.Join(", ", ValidStatuses)}." });

            var alert = await db.AlertEvents.FindAsync(id);
            if (alert is null) return Results.NotFound();

            alert.Status = body.Status;

            if (body.Status == "Trusted")
            {
                var exists = await db.AlertExceptions.AnyAsync(x =>
                    x.Rule == alert.Rule && x.ProcessName == alert.ProcessName);
                if (!exists)
                    db.AlertExceptions.Add(new AlertException
                    {
                        Rule = alert.Rule,
                        ProcessName = alert.ProcessName,
                    });
            }

            await db.SaveChangesAsync();
            return Results.Ok(alert);
        });

        // GET /api/alerts/count
        group.MapGet("/count", async (EventDbContext db) =>
        {
            var count = await db.AlertEvents.CountAsync();
            return Results.Ok(new { count });
        });

        // GET /api/alerts/pending-summary — recuento por severidad de alertas
        // sin revisar (Status=New). Usado por el resumen en lenguaje llano del Dashboard.
        group.MapGet("/pending-summary", async (EventDbContext db) =>
        {
            var counts = await db.AlertEvents.AsNoTracking()
                .Where(a => a.Status == "New")
                .GroupBy(a => a.Severity)
                .Select(g => new { severity = g.Key, count = g.Count() })
                .ToListAsync();

            int Of(string sev) => counts.FirstOrDefault(c => c.severity == sev)?.count ?? 0;
            return Results.Ok(new { high = Of("High"), medium = Of("Medium"), low = Of("Low") });
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

        // ── Excepciones ("no vuelvas a avisarme de esto") ─────────────────────

        // GET /api/alerts/exceptions
        group.MapGet("/exceptions", async (EventDbContext db) =>
            Results.Ok(await db.AlertExceptions.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt).ToListAsync()));

        // DELETE /api/alerts/exceptions/{id} — vuelve a activar la regla para ese proceso
        group.MapDelete("/exceptions/{id:guid}", async (Guid id, EventDbContext db) =>
        {
            var deleted = await db.AlertExceptions.Where(x => x.Id == id).ExecuteDeleteAsync();
            return deleted > 0 ? Results.Ok() : Results.NotFound();
        });
    }

    private sealed record AlertStatusPatch(string Status);
}
