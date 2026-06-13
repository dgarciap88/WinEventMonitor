using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Workers;

/// <summary>
/// Ingesta eventos 4624 (logon success) y 4625 (logon failure) del canal Security.
/// Captura RDP (tipo 10), Network/SMB (tipo 3), WinRM (tipo 3 vía wsmprovhost),
/// Interactive (tipo 2) y fallos de autenticación.
/// </summary>
public class LogonEventWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogonEventWorker> _logger;

    public LogonEventWorker(IServiceScopeFactory scopeFactory, ILogger<LogonEventWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunWatcher(stoppingToken), stoppingToken);

    private void RunWatcher(CancellationToken ct)
    {
        const string channel = "Security";
        var query = new EventLogQuery(channel, PathType.LogName,
            "*[System[(EventID=4624 or EventID=4625)]]");
        EventLogWatcher? watcher = null;
        try
        {
            watcher = new EventLogWatcher(query);
            watcher.EventRecordWritten += OnEvent;
            watcher.Enabled = true;
            _logger.LogInformation("LogonEventWorker iniciado — escuchando 4624/4625");
            ct.WaitHandle.WaitOne();
        }
        catch (EventLogNotFoundException)   { _logger.LogError("Canal Security no encontrado."); }
        catch (UnauthorizedAccessException) { _logger.LogError("Sin permisos para leer Security. Ejecutar como Administrador."); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { _logger.LogError(ex, "Error en LogonEventWorker"); }
        finally { watcher?.Dispose(); }
    }

    private async void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is null) return;
        try
        {
            var ev = ParseLogon(e.EventRecord);
            if (ev is null) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
            db.LogonEvents.Add(ev);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando evento {Id}", e.EventRecord.Id);
        }
    }

    private static LogonEvent? ParseLogon(EventRecord rec)
    {
        try
        {
            var xml = rec.ToXml();
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var ns  = "http://schemas.microsoft.com/win/2004/08/events/event";

            string? Get(string name) =>
                doc.Descendants(System.Xml.Linq.XName.Get("Data", ns))
                   .FirstOrDefault(d => d.Attribute("Name")?.Value == name)
                   ?.Value?.Trim();

            var logonTypeStr = Get("LogonType");
            if (!int.TryParse(logonTypeStr, out var logonType)) return null;

            // Ignorar logons del propio sistema (SYSTEM, procesos de servicio internos)
            // para no saturar la BD con ruido; conservar los de interés de seguridad.
            var user = Get("TargetUserName") ?? Get("SubjectUserName");
            if (user is "SYSTEM" or "-" or "DWM-1" or "DWM-2" or "UMFD-0" or "UMFD-1") return null;
            if (user != null && user.EndsWith("$")) return null; // cuentas de equipo

            var sourceIp   = Get("IpAddress");
            var sourcePort = Get("IpPort");
            if (sourceIp is "-" or "::1" or "127.0.0.1") sourceIp = null;

            return new LogonEvent
            {
                Timestamp        = rec.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventId          = rec.Id,
                Success          = rec.Id == 4624,
                LogonType        = logonType,
                LogonTypeName    = LogonEvent.GetLogonTypeName(logonType),
                UserName         = user,
                Domain           = Get("TargetDomainName") ?? Get("SubjectDomainName"),
                SourceIp         = sourceIp,
                SourcePort       = int.TryParse(sourcePort, out var sp) ? sp : null,
                WorkstationName  = Get("WorkstationName"),
                LogonProcessName = Get("LogonProcessName"),
                AuthPackage      = Get("AuthenticationPackageName"),
                FailureReason    = rec.Id == 4625 ? Get("FailureReason") ?? Get("SubStatus") : null,
            };
        }
        catch { return null; }
    }
}
