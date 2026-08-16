using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;

namespace WinEventMonitor.Service.Api;

public static class TimelineRoutes
{
    public static void MapTimelineRoutes(this WebApplication app)
    {
        // GET /api/processes/{pid}/timeline
        // Todos los eventos históricos de un PID ordenados cronológicamente.
        // Útil para "blast radius": ver todo lo que hizo un proceso sospechoso.
        app.MapGet("/api/processes/{pid:int}/timeline", async (int pid, EventDbContext db) =>
        {
            var processes = await db.ProcessEvents.AsNoTracking()
                .Where(e => e.Pid == pid)
                .OrderBy(e => e.Timestamp)
                .ToListAsync();

            var network = await db.NetworkEvents.AsNoTracking()
                .Where(e => e.Pid == pid)
                .OrderBy(e => e.Timestamp)
                .Take(200)
                .ToListAsync();

            var dns = await db.DnsEvents.AsNoTracking()
                .Where(e => e.Pid == pid)
                .OrderBy(e => e.Timestamp)
                .Take(200)
                .ToListAsync();

            var alerts = await db.AlertEvents.AsNoTracking()
                .Where(e => e.Pid == pid)
                .OrderBy(e => e.Timestamp)
                .ToListAsync();

            var advanced = await db.SysmonAdvancedEvents.AsNoTracking()
                .Where(e => e.SourcePid == pid || e.TargetPid == pid)
                .OrderBy(e => e.Timestamp)
                .Take(100)
                .ToListAsync();

            var processName = processes.FirstOrDefault()?.ProcessName ?? "(desconocido)";

            return Results.Ok(new { pid, processName, processes, network, dns, alerts, advanced });
        });

        // GET /api/timeline/unified?range=1h|6h|24h
        // Vista cruzada de todas las fuentes (proceso, red, DNS, alertas, accesos)
        // en un mismo eje temporal, para ver "que paso" sin saltar entre pestañas.
        app.MapGet("/api/timeline/unified", async (EventDbContext db, string range = "6h") =>
        {
            var window = range switch
            {
                "1h"  => TimeSpan.FromHours(1),
                "24h" => TimeSpan.FromHours(24),
                _     => TimeSpan.FromHours(6),
            };
            var cutoff = DateTime.UtcNow - window;
            const int perCategoryCap = 150;
            const int totalCap = 400;

            var procs = await db.ProcessEvents.AsNoTracking()
                .Where(e => e.Timestamp >= cutoff && e.EventType == "Create")
                .OrderByDescending(e => e.Timestamp).Take(perCategoryCap)
                .Select(e => new TimelineItem("process", e.Timestamp, null,
                    e.ProcessName + " iniciado", e.Pid, e.ProcessName))
                .ToListAsync();

            var net = await db.NetworkEvents.AsNoTracking()
                .Where(e => e.Timestamp >= cutoff && e.Initiated)
                .OrderByDescending(e => e.Timestamp).Take(perCategoryCap)
                .Select(e => new TimelineItem("network", e.Timestamp, null,
                    e.ProcessName + " -> " + e.DestinationIp + ":" + e.DestinationPort, e.Pid, e.ProcessName))
                .ToListAsync();

            var dns = await db.DnsEvents.AsNoTracking()
                .Where(e => e.Timestamp >= cutoff)
                .OrderByDescending(e => e.Timestamp).Take(perCategoryCap)
                .Select(e => new TimelineItem("dns", e.Timestamp, null,
                    e.ProcessName + ": " + e.QueryName, e.Pid, e.ProcessName))
                .ToListAsync();

            var alerts = await db.AlertEvents.AsNoTracking()
                .Where(e => e.Timestamp >= cutoff)
                .OrderByDescending(e => e.Timestamp).Take(perCategoryCap)
                .Select(e => new TimelineItem("alert", e.Timestamp, e.Severity,
                    e.Description, e.Pid, e.ProcessName))
                .ToListAsync();

            var logons = await db.LogonEvents.AsNoTracking()
                .Where(e => e.Timestamp >= cutoff)
                .OrderByDescending(e => e.Timestamp).Take(perCategoryCap)
                .Select(e => new TimelineItem("logon", e.Timestamp, e.Success ? null : "High",
                    (e.Success ? "Inicio de sesion: " : "Fallo de inicio de sesion: ") +
                        e.UserName + " (" + e.LogonTypeName + ")",
                    null, e.UserName))
                .ToListAsync();

            var merged = procs.Concat(net).Concat(dns).Concat(alerts).Concat(logons)
                .OrderByDescending(x => x.Timestamp)
                .Take(totalCap)
                .ToList();

            return Results.Ok(merged);
        });
    }

    private sealed record TimelineItem(
        string Kind, DateTime Timestamp, string? Severity, string Summary, int? Pid, string? ProcessName);
}
