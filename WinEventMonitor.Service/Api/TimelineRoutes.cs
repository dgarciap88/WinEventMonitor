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
    }
}
