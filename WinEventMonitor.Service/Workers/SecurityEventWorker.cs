using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;
using WinEventMonitor.Service.Parsers;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Workers;

public class SecurityEventWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertService _alertService;
    private readonly AlertRulesService _rulesService;
    private readonly ILogger<SecurityEventWorker> _logger;
    private const string SecurityChannel = "Security";

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(2);

    public SecurityEventWorker(
        IServiceScopeFactory scopeFactory,
        AlertService alertService,
        AlertRulesService rulesService,
        ILogger<SecurityEventWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _alertService = alertService;
        _rulesService = rulesService;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => RunWithRetry(stoppingToken), stoppingToken);
    }

    private void RunWithRetry(CancellationToken ct)
    {
        var delay = InitialRetryDelay;
        while (!ct.IsCancellationRequested)
        {
            var startedAt = DateTime.UtcNow;
            RunWatcher(ct);
            if (ct.IsCancellationRequested) break;

            delay = DateTime.UtcNow - startedAt > delay
                ? InitialRetryDelay
                : TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));

            _logger.LogWarning("SecurityEventWorker se detuvo inesperadamente, reintentando en {Delay}s", delay.TotalSeconds);
            ct.WaitHandle.WaitOne(delay);
        }
    }

    private void RunWatcher(CancellationToken ct)
    {
        var query = new EventLogQuery(SecurityChannel, PathType.LogName,
            "*[System[(EventID=4688 or EventID=4689 or EventID=1102)]]");
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
                case 1102:
                    await HandleLogClearedAsync(e.EventRecord);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando evento Security ID {Id}", e.EventRecord.Id);
        }
    }

    // ── Regla 21: registro de seguridad borrado (Event 1102) ─────────────────
    // Windows genera este evento SIEMPRE al borrar el log, sin depender de la
    // politica de auditoria: es la tecnica clasica para ocultar rastro de un ataque.
    private async Task HandleLogClearedAsync(EventRecord rec)
    {
        if (!_rulesService.IsEnabled(21)) return;

        var data = ImageLoadParser.ParseEventData(rec);
        var user = data.GetValueOrDefault("SubjectUserName");
        var domain = data.GetValueOrDefault("SubjectDomainName");
        var who = string.IsNullOrEmpty(user) ? "desconocido" : $"{domain}\\{user}";

        await _alertService.AddAsync(new AlertEvent
        {
            Timestamp      = rec.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
            Severity       = _rulesService.GetSeverity(21, "High"),
            Rule           = "Auditoría – Registro de seguridad borrado",
            Description    = $"El registro de eventos de seguridad fue borrado por {who}",
            Details        = $"Usuario: {who}",
            MitreTechnique = "T1070.001",
        });
    }
}
