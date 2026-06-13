using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;

namespace WinEventMonitor.Service.Workers;

public class RetentionWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RetentionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primera purga al arrancar (después de 1 min para no solapar con EnsureCreated)
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PurgeAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        var days = config.GetValue<int>("EventMonitor:RetentionDays", 30);
        if (days <= 0) return; // 0 = retención infinita

        var cutoff = DateTime.UtcNow.AddDays(-days);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();

            var processes = await db.ProcessEvents
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            var network = await db.NetworkEvents
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            var dns = await db.DnsEvents
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            if (processes + network + dns > 0)
                logger.LogInformation(
                    "Retención: eliminados {P} procesos, {N} red, {D} DNS anteriores a {Cutoff:yyyy-MM-dd}",
                    processes, network, dns, cutoff);

            // VACUUM para liberar espacio en disco en SQLite
            await db.Database.ExecuteSqlRawAsync("VACUUM;", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error en la purga de retención");
        }
    }
}
