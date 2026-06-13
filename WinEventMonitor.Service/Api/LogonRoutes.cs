using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;

namespace WinEventMonitor.Service.Api;

public static class LogonRoutes
{
    public static void MapLogonRoutes(this WebApplication app)
    {
        var g = app.MapGroup("/api/logons");

        // GET /api/logons — paginado con filtros
        g.MapGet("/", async (
            EventDbContext db,
            int    page      = 1,
            int    pageSize  = 50,
            string? from     = null,
            string? to       = null,
            string? user     = null,
            string? sourceIp = null,
            int?    type     = null,   // LogonType
            bool?   success  = null,
            bool?   remoteOnly = null) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 200) pageSize = 50;

            var q = db.LogonEvents.AsNoTracking().AsQueryable();

            if (DateTime.TryParse(from, out var fromDt)) q = q.Where(e => e.Timestamp >= fromDt.ToUniversalTime());
            if (DateTime.TryParse(to,   out var toDt))   q = q.Where(e => e.Timestamp <= toDt.ToUniversalTime());
            if (!string.IsNullOrWhiteSpace(user))     q = q.Where(e => e.UserName != null && EF.Functions.Like(e.UserName, $"%{user}%"));
            if (!string.IsNullOrWhiteSpace(sourceIp)) q = q.Where(e => e.SourceIp != null && EF.Functions.Like(e.SourceIp, $"%{sourceIp}%"));
            if (type.HasValue)    q = q.Where(e => e.LogonType == type.Value);
            if (success.HasValue) q = q.Where(e => e.Success == success.Value);
            if (remoteOnly == true) q = q.Where(e => e.SourceIp != null);

            var total = await q.CountAsync();
            var data  = await q.OrderByDescending(e => e.Timestamp)
                                .Skip((page - 1) * pageSize)
                                .Take(pageSize)
                                .ToListAsync();

            return Results.Ok(new { data, total, page, pageSize });
        });

        // GET /api/logons/summary — resumen: activos, intentos fallidos, IPs únicas
        g.MapGet("/summary", async (EventDbContext db) =>
        {
            var since24h = DateTime.UtcNow.AddHours(-24);

            var failures = await db.LogonEvents
                .Where(e => !e.Success && e.Timestamp >= since24h)
                .CountAsync();

            var rdpSessions = await db.LogonEvents
                .Where(e => e.Success && e.LogonType == 10 && e.Timestamp >= since24h)
                .CountAsync();

            var networkLogons = await db.LogonEvents
                .Where(e => e.Success && e.LogonType == 3 && e.Timestamp >= since24h)
                .CountAsync();

            var uniqueSourceIps = await db.LogonEvents
                .Where(e => e.Timestamp >= since24h && e.SourceIp != null)
                .Select(e => e.SourceIp!)
                .Distinct()
                .CountAsync();

            var topAttackers = await db.LogonEvents
                .Where(e => !e.Success && e.SourceIp != null && e.Timestamp >= since24h)
                .GroupBy(e => e.SourceIp!)
                .Select(g => new { ip = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync();

            var recentRemote = await db.LogonEvents
                .Where(e => e.SourceIp != null && e.Timestamp >= since24h)
                .OrderByDescending(e => e.Timestamp)
                .Take(20)
                .ToListAsync();

            return Results.Ok(new
            {
                failures24h     = failures,
                rdpSessions24h  = rdpSessions,
                networkLogons24h= networkLogons,
                uniqueSourceIps,
                topAttackers,
                recentRemote,
            });
        });
    }
}
