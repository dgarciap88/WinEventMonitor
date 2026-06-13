using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Parsers;

namespace WinEventMonitor.Service.Workers;

public class SysmonEventWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SysmonEventWorker> _logger;
    private const string SysmonChannel = "Microsoft-Windows-Sysmon/Operational";

    public SysmonEventWorker(IServiceScopeFactory scopeFactory, ILogger<SysmonEventWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // EventLogWatcher es sincrono internamente, lo corremos en un hilo dedicado
        return Task.Run(() => RunWatcher(stoppingToken), stoppingToken);
    }

    private void RunWatcher(CancellationToken ct)
    {
        var query = new EventLogQuery(SysmonChannel, PathType.LogName,
            "*[System[(EventID=1 or EventID=3 or EventID=5 or EventID=7 or EventID=8 or EventID=10 or EventID=22)]]");
        EventLogWatcher? watcher = null;

        try
        {
            watcher = new EventLogWatcher(query);
            watcher.EventRecordWritten += OnEventWritten;
            watcher.Enabled = true;
            _logger.LogInformation("SysmonEventWorker iniciado, escuchando {Channel}", SysmonChannel);
            ct.WaitHandle.WaitOne();
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogError("Canal Sysmon no encontrado. ¿Está Sysmon instalado?");
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Sin permisos para leer {Channel}. Ejecutar como Administrador.", SysmonChannel);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error inesperado en SysmonEventWorker");
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
                case 1:
                    var create = ProcessCreateParser.FromSysmon(e.EventRecord);
                    if (create is not null) { db.ProcessEvents.Add(create); await db.SaveChangesAsync(); }
                    break;
                case 5:
                    var terminate = ProcessTerminateParser.FromSysmon(e.EventRecord);
                    if (terminate is not null) { db.ProcessEvents.Add(terminate); await db.SaveChangesAsync(); }
                    break;
                case 3:
                    var net = NetworkConnectionParser.FromSysmon(e.EventRecord);
                    if (net is not null) { db.NetworkEvents.Add(net); await db.SaveChangesAsync(); }
                    break;
                case 22:
                    var dns = DnsQueryParser.FromSysmon(e.EventRecord);
                    if (dns is not null) { db.DnsEvents.Add(dns); await db.SaveChangesAsync(); }
                    break;
                case 7:
                    var imgLoad = ImageLoadParser.FromSysmon(e.EventRecord);
                    if (imgLoad is not null) { db.SysmonAdvancedEvents.Add(imgLoad); await db.SaveChangesAsync(); }
                    break;
                case 8:
                    var crt = CreateRemoteThreadParser.FromSysmon(e.EventRecord);
                    if (crt is not null) { db.SysmonAdvancedEvents.Add(crt); await db.SaveChangesAsync(); }
                    break;
                case 10:
                    var procAccess = ProcessAccessParser.FromSysmon(e.EventRecord);
                    if (procAccess is not null) { db.SysmonAdvancedEvents.Add(procAccess); await db.SaveChangesAsync(); }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando evento Sysmon ID {Id}", e.EventRecord.Id);
        }
    }
}
