using Microsoft.EntityFrameworkCore;
using Serilog;
using WinEventMonitor.Service.Api;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Security;
using WinEventMonitor.Service.Services;
using WinEventMonitor.Service.Setup;
using WinEventMonitor.Service.Workers;

var builder = WebApplication.CreateBuilder(args);

// Puerto configurable (appsettings.json → EventMonitor:Port, default 51847)
var port = 51847;

// --- Logging a fichero (Serilog, rolling diario, 7 días) ---
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "WinEventMonitor", "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine(logDir, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Conecta Serilog al pipeline de ILogger<T> usado por los Workers (sin esto,
// el fichero service-.log nunca recibe nada y los errores quedan invisibles).
builder.Host.UseSerilog();

// Red de seguridad: sin esto, una excepcion no capturada en un hilo de fondo
// (p.ej. un event handler async void) mata el proceso sin dejar rastro en el log.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Fatal(e.ExceptionObject as Exception, "Excepcion no controlada, terminando: {IsTerminating}", e.IsTerminating);
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Tarea con excepcion no observada");
    e.SetObserved();
};

// --- Soporte servicio de Windows (no-op en consola/dev) ---
builder.Host.UseWindowsService();

// --- Base de datos ---
var dbPath = builder.Configuration["EventMonitor:DatabasePath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
       "WinEventMonitor", "events.db");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
// Solo IDbContextFactory: permite inyeccion tanto en scoped (routes) como en singleton (AlertService)
builder.Services.AddDbContextFactory<EventDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Scoped);

// --- Seguridad ---
builder.Services.AddSingleton<ApiKeyService>();

// --- Setup: configuración de Sysmon y audit policy ---
builder.Services.AddSingleton<SysmonSetupService>();
builder.Services.AddSingleton<TrustedDomainService>();

// --- Alertas de detección ---
builder.Services.AddSingleton<AlertService>();
builder.Services.AddSingleton<AlertRulesService>();

// --- Métricas de sistema (CPU/RAM/Disco/Procesos) ---
builder.Services.AddSingleton<SystemHealthService>();

// --- CORS: dev (Vite) + mismo puerto en producción ---
port = builder.Configuration.GetValue<int>("EventMonitor:Port", 51847);
builder.Services.AddCors(options =>
    options.AddPolicy("LocalOnly", policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174", $"http://localhost:{port}", $"http://127.0.0.1:{port}")
              .WithHeaders("X-Api-Key", "Content-Type")
              .WithMethods("GET", "POST", "DELETE", "PATCH")));

// --- HttpClient para VirusTotal ---
builder.Services.AddHttpClient();

// --- Workers ---
var config = builder.Configuration.GetSection("EventMonitor");
if (config.GetValue<bool>("Sources:Sysmon"))
    builder.Services.AddHostedService<SysmonEventWorker>();
if (config.GetValue<bool>("Sources:Security"))
    builder.Services.AddHostedService<SecurityEventWorker>();

// Purga periódica según RetentionDays (0 = infinito)
builder.Services.AddHostedService<RetentionWorker>();
// Motor de detección de comportamientos sospechosos
builder.Services.AddHostedService<AlertWorker>();
// Ingesta de eventos de logon (4624/4625): acceso remoto, fuerza bruta
builder.Services.AddHostedService<LogonEventWorker>();

// --- Kestrel: solo loopback, puerto configurable ---
builder.WebHost.ConfigureKestrel(k =>
{
    // CertificatePassword no se commitea con valor real: si se usa un certificado,
    // la password se aporta via variable de entorno (EventMonitor__CertificatePassword)
    // o un appsettings local fuera del control de versiones.
    var certPath = builder.Configuration["EventMonitor:CertificatePath"];
    var certPass = builder.Configuration["EventMonitor:CertificatePassword"];

    if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
        k.Listen(System.Net.IPAddress.Loopback, port,
            lo => lo.UseHttps(certPath, certPass));
    else
        k.Listen(System.Net.IPAddress.Loopback, port); // HTTP en dev si no hay cert
});

var app = builder.Build();

// --- Migracion automatica al arrancar ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
    db.Database.EnsureCreated();
    // Añadir columna solo si no existe (SQLite no soporta IF NOT EXISTS en ALTER TABLE)
    var cols = db.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('NetworkEvents')")
        .ToList();
    if (!cols.Contains("ExecutablePath"))
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE NetworkEvents ADD COLUMN ExecutablePath TEXT NULL");
    // Tabla AlertEvents: EnsureCreated la crea si la BD ya existe antes de añadirla
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS AlertEvents (
            Id TEXT NOT NULL PRIMARY KEY,
            Timestamp TEXT NOT NULL,
            Severity TEXT NOT NULL,
            Rule TEXT NOT NULL,
            Description TEXT NOT NULL,
            Pid INTEGER NULL,
            ProcessName TEXT NULL,
            Details TEXT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_AlertEvents_Timestamp ON AlertEvents (Timestamp)");
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_AlertEvents_Severity ON AlertEvents (Severity)");

    // Columna MitreTechnique añadida en Fase 10
    var alertCols = db.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AlertEvents')")
        .ToList();
    if (!alertCols.Contains("MitreTechnique"))
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE AlertEvents ADD COLUMN MitreTechnique TEXT NULL");

    // Columna Status (New/Reviewed/Dismissed/Trusted) — mejoras funcionales, Fase 1
    if (!alertCols.Contains("Status"))
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE AlertEvents ADD COLUMN Status TEXT NOT NULL DEFAULT 'New'");

    // Columna RelatedIp — mejoras funcionales, Fase 4: permite ofrecer "Bloquear IP"
    if (!alertCols.Contains("RelatedIp"))
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE AlertEvents ADD COLUMN RelatedIp TEXT NULL");

    // Tabla AlertExceptions — mejoras funcionales, Fase 1: permite silenciar una
    // regla para un proceso concreto (o todos) sin desactivar la regla entera.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS AlertExceptions (
            Id TEXT NOT NULL PRIMARY KEY,
            Rule TEXT NOT NULL,
            ProcessName TEXT NULL,
            CreatedAt TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_AlertExceptions_Rule ON AlertExceptions (Rule)");

    // Tabla SysmonAdvancedEvents (Sysmon IDs 7, 8, 10)
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS SysmonAdvancedEvents (
            Id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            Timestamp         TEXT    NOT NULL,
            EventId           INTEGER NOT NULL,
            SourcePid         INTEGER NOT NULL,
            SourceProcessName TEXT    NOT NULL,
            ImagePath         TEXT    NULL,
            Signed            INTEGER NULL,
            Signature         TEXT    NULL,
            SignatureStatus   TEXT    NULL,
            Sha256            TEXT    NULL,
            TargetPid         INTEGER NULL,
            TargetProcessName TEXT    NULL,
            StartAddress      TEXT    NULL,
            StartModule       TEXT    NULL,
            StartFunction     TEXT    NULL,
            GrantedAccess     TEXT    NULL,
            CallTrace         TEXT    NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_SysmonAdvancedEvents_Timestamp ON SysmonAdvancedEvents (Timestamp)");
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_SysmonAdvancedEvents_EventId ON SysmonAdvancedEvents (EventId)");
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_SysmonAdvancedEvents_SourcePid ON SysmonAdvancedEvents (SourcePid)");
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_SysmonAdvancedEvents_TargetPid ON SysmonAdvancedEvents (TargetPid)");

    // Columnas para Sysmon ID 13 (RegistryEvent) — mejoras funcionales, Fase 3
    var sysmonAdvCols = db.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('SysmonAdvancedEvents')")
        .ToList();
    if (!sysmonAdvCols.Contains("TargetObject"))
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE SysmonAdvancedEvents ADD COLUMN TargetObject TEXT NULL");
    if (!sysmonAdvCols.Contains("RegistryDetails"))
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE SysmonAdvancedEvents ADD COLUMN RegistryDetails TEXT NULL");

    // Tabla LogonEvents
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS LogonEvents (
            Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            Timestamp        TEXT NOT NULL,
            EventId          INTEGER NOT NULL,
            Success          INTEGER NOT NULL,
            LogonType        INTEGER NOT NULL,
            LogonTypeName    TEXT NOT NULL,
            UserName         TEXT NULL,
            Domain           TEXT NULL,
            SourceIp         TEXT NULL,
            SourcePort       INTEGER NULL,
            WorkstationName  TEXT NULL,
            LogonProcessName TEXT NULL,
            AuthPackage      TEXT NULL,
            FailureReason    TEXT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_LogonEvents_Timestamp ON LogonEvents (Timestamp)");
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_LogonEvents_SourceIp ON LogonEvents (SourceIp)");
    db.Database.ExecuteSqlRaw(
        "CREATE INDEX IF NOT EXISTS IX_LogonEvents_Success ON LogonEvents (Success)");
}

// --- Inicializar API Key al arrancar ---
app.Services.GetRequiredService<ApiKeyService>().GetOrCreateKey();

// Sin esto, una excepcion no capturada en un endpoint devuelve la pagina de
// error por defecto (o un 500 vacio) y no queda registrada en ningun sitio.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    Log.Error(ex, "Excepcion no controlada en {Path}", context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("""{"error":"internal_error"}""");
}));

app.UseCors("LocalOnly");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapApiRoutes();
app.MapSetupRoutes();
app.MapLiveProcessRoutes();
app.MapAlertRoutes();
app.MapAlertRulesRoutes();
app.MapStatsRoutes();
app.MapSystemRoutes();
app.MapLogonRoutes();
app.MapConnectionsRoutes();
app.MapTimelineRoutes();
app.MapVirusTotalRoutes();

// Fallback: devuelve index.html para que React Router funcione
app.MapFallbackToFile("index.html");

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "El servicio termino de forma inesperada");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
