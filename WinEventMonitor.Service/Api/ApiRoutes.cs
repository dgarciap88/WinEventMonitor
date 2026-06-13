using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Setup;

namespace WinEventMonitor.Service.Api;

public static class ApiRoutes
{
    public static void MapApiRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // GET /api/processes
        api.MapGet("/processes", async (
            EventDbContext db,
            DateTime? from, DateTime? to,
            string? name, string? user,
            bool? elevated,
            int page = 1, int pageSize = 50) =>
        {
            pageSize = Math.Min(pageSize, 500);
            var q = db.ProcessEvents.AsNoTracking();

            if (from.HasValue)    q = q.Where(p => p.Timestamp >= from.Value.ToUniversalTime());
            if (to.HasValue)      q = q.Where(p => p.Timestamp <= to.Value.ToUniversalTime());
            if (!string.IsNullOrEmpty(name))  q = q.Where(p => p.ProcessName.Contains(name));
            if (!string.IsNullOrEmpty(user))  q = q.Where(p => p.UserName == user);
            if (elevated.HasValue) q = q.Where(p => p.IsElevated == elevated.Value);

            var total = await q.CountAsync();
            var data  = await q.OrderByDescending(p => p.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Results.Ok(new { data, total, page, pageSize });
        });

        // GET /api/network
        api.MapGet("/network", async (
            EventDbContext db,
            DateTime? from, DateTime? to,
            string? process, string? destIp,
            int? destPort,
            int page = 1, int pageSize = 50) =>
        {
            pageSize = Math.Min(pageSize, 500);
            var q = db.NetworkEvents.AsNoTracking();

            if (from.HasValue)       q = q.Where(n => n.Timestamp >= from.Value.ToUniversalTime());
            if (to.HasValue)         q = q.Where(n => n.Timestamp <= to.Value.ToUniversalTime());
            if (!string.IsNullOrEmpty(process)) q = q.Where(n => n.ProcessName.Contains(process));
            if (!string.IsNullOrEmpty(destIp))  q = q.Where(n => n.DestinationIp == destIp);
            if (destPort.HasValue)   q = q.Where(n => n.DestinationPort == destPort.Value);

            var total = await q.CountAsync();
            var data  = await q.OrderByDescending(n => n.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Results.Ok(new { data, total, page, pageSize });
        });

        // GET /api/dns
        api.MapGet("/dns", async (
            EventDbContext db,
            TrustedDomainService trustedDomains,
            DateTime? from, DateTime? to,
            string? process, string? domain,
            bool? excludeTrusted,
            int page = 1, int pageSize = 50) =>
        {
            pageSize = Math.Min(pageSize, 500);
            var q = db.DnsEvents.AsNoTracking();

            if (from.HasValue)        q = q.Where(d => d.Timestamp >= from.Value.ToUniversalTime());
            if (to.HasValue)          q = q.Where(d => d.Timestamp <= to.Value.ToUniversalTime());
            if (!string.IsNullOrEmpty(process)) q = q.Where(d => d.ProcessName.Contains(process));
            if (!string.IsNullOrEmpty(domain))  q = q.Where(d => d.QueryName.Contains(domain));

            if (excludeTrusted == true)
            {
                foreach (var trusted in trustedDomains.GetDomains())
                {
                    var exact = trusted;
                    var suffix = "." + trusted;
                    q = q.Where(d => d.QueryName != exact
                                  && !d.QueryName.EndsWith(suffix));
                }
            }

            var total = await q.CountAsync();
            var data  = await q.OrderByDescending(d => d.Timestamp)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            return Results.Ok(new { data, total, page, pageSize });
        });
    }
}
