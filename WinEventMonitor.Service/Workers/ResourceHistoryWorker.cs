using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Workers;

/// <summary>
/// Guarda una muestra de recursos del sistema cada pocos minutos para poder
/// ver tendencias de horas/días (SystemHealthService solo guarda 2 min en memoria).
/// </summary>
public class ResourceHistoryWorker(
    IServiceScopeFactory scopeFactory,
    SystemHealthService healthService,
    ILogger<ResourceHistoryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Dar tiempo a que SystemHealthService tenga al menos una muestra real.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SampleAsync(stoppingToken);
            await Task.Delay(SampleInterval, stoppingToken);
        }
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        try
        {
            var snapshot = healthService.GetLatest();
            if (snapshot.GeneratedAt == default) return; // aun sin muestra valida

            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            var disk = snapshot.Disk.FirstOrDefault(d => d.Name.Equals(systemDrive, StringComparison.OrdinalIgnoreCase))
                       ?? snapshot.Disk.FirstOrDefault();
            var diskUsedPct = disk is { TotalGb: > 0 }
                ? Math.Round((disk.TotalGb - disk.FreeGb) / disk.TotalGb * 100, 1)
                : 0;
            var ramPct = snapshot.Ram.TotalMb > 0
                ? Math.Round((double)snapshot.Ram.UsedMb / snapshot.Ram.TotalMb * 100, 1)
                : 0;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();

            db.ResourceSamples.Add(new ResourceSample
            {
                Timestamp       = DateTime.UtcNow,
                CpuPct          = snapshot.Cpu.TotalPercent,
                RamPct          = ramPct,
                RamUsedMb       = snapshot.Ram.UsedMb,
                RamTotalMb      = snapshot.Ram.TotalMb,
                DiskUsedPct     = diskUsedPct,
                NetSentBytesSec = snapshot.Net.BytesSentSec,
                NetRecvBytesSec = snapshot.Net.BytesRecvSec,
            });
            await db.SaveChangesAsync(ct);

            var cutoff = DateTime.UtcNow - Retention;
            await db.ResourceSamples.Where(r => r.Timestamp < cutoff).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error guardando muestra de recursos");
        }
    }
}
