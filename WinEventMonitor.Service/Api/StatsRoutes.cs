using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;

namespace WinEventMonitor.Service.Api;

public static class StatsRoutes
{
    public static void MapStatsRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/stats");

        // GET /api/stats — resumen general para el dashboard
        group.MapGet("", async (EventDbContext db) =>
        {
            var now   = DateTime.UtcNow;
            var h24   = now.AddHours(-24);
            var d7    = now.AddDays(-7);

            // ── Conteos globales ──────────────────────────────────────────────
            var totalProcesses = await db.ProcessEvents.CountAsync();
            var totalNetwork   = await db.NetworkEvents.CountAsync();
            var totalDns       = await db.DnsEvents.CountAsync();
            var totalAlerts    = await db.AlertEvents.CountAsync();

            // ── Últimas 24 h ─────────────────────────────────────────────────
            var proc24   = await db.ProcessEvents.CountAsync(e => e.Timestamp >= h24);
            var net24    = await db.NetworkEvents.CountAsync(e => e.Timestamp >= h24);
            var dns24    = await db.DnsEvents.CountAsync(e => e.Timestamp >= h24);
            var alerts24 = await db.AlertEvents.CountAsync(e => e.Timestamp >= h24);

            // ── Alertas por severidad ─────────────────────────────────────────
            var alertsBySeverity = await db.AlertEvents
                .GroupBy(a => a.Severity)
                .Select(g => new { Severity = g.Key, Count = g.Count() })
                .ToListAsync();

            // ── Top 5 IPs destino (últimas 24h) ──────────────────────────────
            var topIps = await db.NetworkEvents
                .Where(e => e.Timestamp >= h24 && e.DestinationIp != null)
                .GroupBy(e => e.DestinationIp!)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new { Ip = g.Key, Count = g.Count() })
                .ToListAsync();

            // ── Top 5 procesos por conexiones de red (últimas 24h) ────────────
            var topNetProcs = await db.NetworkEvents
                .Where(e => e.Timestamp >= h24)
                .GroupBy(e => e.ProcessName)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new { Process = g.Key, Count = g.Count() })
                .ToListAsync();

            // ── Top 5 dominios DNS consultados (últimas 24h) ─────────────────
            var topDomains = await db.DnsEvents
                .Where(e => e.Timestamp >= h24)
                .GroupBy(e => e.QueryName)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new { Domain = g.Key, Count = g.Count() })
                .ToListAsync();

            // ── Actividad por hora (últimas 24h): procesos + red + dns ────────
            var procByHour = await db.ProcessEvents
                .Where(e => e.Timestamp >= h24)
                .GroupBy(e => e.Timestamp.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .ToListAsync();

            var netByHour = await db.NetworkEvents
                .Where(e => e.Timestamp >= h24)
                .GroupBy(e => e.Timestamp.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .ToListAsync();

            // Combinar en array de 24 horas (índice = hora UTC)
            var activityByHour = Enumerable.Range(0, 24).Select(h => new
            {
                Hour      = h,
                Processes = procByHour.FirstOrDefault(x => x.Hour == h)?.Count ?? 0,
                Network   = netByHour.FirstOrDefault(x => x.Hour == h)?.Count ?? 0,
            }).ToArray();

            // ── Últimas 5 alertas ─────────────────────────────────────────────
            var recentAlerts = await db.AlertEvents
                .OrderByDescending(a => a.Timestamp)
                .Take(5)
                .Select(a => new { a.Id, a.Timestamp, a.Severity, a.Rule, a.Description, a.ProcessName })
                .ToListAsync();

            return Results.Ok(new
            {
                totals = new { totalProcesses, totalNetwork, totalDns, totalAlerts },
                last24h = new { proc24, net24, dns24, alerts24 },
                alertsBySeverity,
                topIps,
                topNetProcs,
                topDomains,
                activityByHour,
                recentAlerts,
                generatedAt = now,
            });
        });
    }
}
