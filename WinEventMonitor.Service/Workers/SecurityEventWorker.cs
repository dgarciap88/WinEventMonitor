using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Parsers;

namespace WinEventMonitor.Service.Workers;

public class SecurityEventWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityEventWorker> _logger;
    private const string SecurityChannel = "Security";

    public SecurityEventWorker(IServiceScopeFactory scopeFactory, ILogger<SecurityEventWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunWatcher(stoppingToken), stoppingToken);
    }

    private void RunWatcher(CancellationToken ct)
    {
        var query = new EventLogQuery(SecurityChannel, PathType.LogName, "*[System[(EventID=4688 or EventID=4689)]]");
        EventLogWatcher? watcher = null;

        try
        {
            watcher = new EventLogWatcher(query);
            watcher.EventRecordWritten += OnEventWritten;
            watcher.Enabled = true;
            _logger.LogInformation("SecurityEventWorker iniciado, escuchando {Channel}", SecurityChannel);
            ct.WaitHandle.WaitOne();
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogError("Canal Security no encontrado.");
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Sin permisos para leer {Channel}. Ejecutar como Administrador.", SecurityChannel);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error inesperado en SecurityEventWorker");
        }
        finally
        {
            watcher?.Dispose();
        }
    }

    private async void OnEventWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();

            switch (e.EventRecord.Id)
            {
                case 4688:
                    var create = ProcessCreateParser.FromSecurity(e.EventRecord);
                    if (create is not null) { db.ProcessEvents.Add(create); await db.SaveChangesAsync(); }
                    break;
                case 4689:
                    var terminate = ProcessTerminateParser.FromSecurity(e.EventRecord);
                    if (terminate is not null) { db.ProcessEvents.Add(terminate); await db.SaveChangesAsync(); }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando evento Security ID {Id}", e.EventRecord.Id);
        }
    }
}
