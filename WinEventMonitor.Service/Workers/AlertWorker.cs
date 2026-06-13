using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Workers;

/// <summary>
/// Motor de detección de comportamientos sospechosos.
/// Se ejecuta cada 60 s y aplica reglas sobre los eventos nuevos de la BD.
/// </summary>
public class AlertWorker(AlertService alertService, AlertRulesService rulesService, IServiceScopeFactory scopeFactory) : BackgroundService
{
    private DateTime _lastScan = DateTime.UtcNow.AddMinutes(-2);

    // ─── Listas de referencia (LOLBins) ───────────────────────────────────────
    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
          "mshta.exe", "regsvr32.exe", "rundll32.exe", "certutil.exe", "bitsadmin.exe" };

    private static readonly HashSet<string> DocumentApps = new(StringComparer.OrdinalIgnoreCase)
        { "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe", "msaccess.exe",
          "mspub.exe", "acrord32.exe", "acrobat.exe",
          "chrome.exe", "firefox.exe", "msedge.exe", "iexplore.exe", "opera.exe" };

    private static readonly string[] SuspiciousTlds =
        [".xyz", ".top", ".tk", ".club", ".onion", ".bit", ".cc", ".pw", ".icu", ".gq", ".ml", ".cf"];

    private static readonly int[] LateralMovementPorts = [445, 135, 3389, 5985, 5986, 22];
    private static readonly Dictionary<int, string> PortNames = new()
        { {445,"SMB"}, {135,"RPC/DCOM"}, {3389,"RDP"}, {5985,"WinRM"}, {5986,"WinRM HTTPS"}, {22,"SSH"} };

    private static readonly HashSet<string> SystemUsers = new(StringComparer.OrdinalIgnoreCase)
        { "SYSTEM", "NT AUTHORITY\\SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE" };

    private static readonly string[] SuspiciousPaths =
        [@"\Temp\", @"\AppData\Local\Temp\", @"\Users\Public\", @"\Downloads\",
         @"\ProgramData\Temp\", @"C:\Temp\", @"C:\Windows\Temp\"];

    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Pequeño delay de arranque para no sobrecargar durante el boot
        await Task.Delay(TimeSpan.FromSeconds(45), ct);

        while (!ct.IsCancellationRequested)
        {
            await ScanAsync();
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    private async Task ScanAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();

            var since = _lastScan;
            _lastScan = DateTime.UtcNow;

            var newProcs   = await db.ProcessEvents.AsNoTracking()
                                .Where(e => e.EventType == "Create" && e.Timestamp > since)
                                .ToListAsync();
            var newDns     = await db.DnsEvents.AsNoTracking()
                                .Where(e => e.Timestamp > since)
                                .ToListAsync();
            var newNetwork = await db.NetworkEvents.AsNoTracking()
                                .Where(e => e.Timestamp > since && e.Initiated)
                                .ToListAsync();
            var newAdvanced = await db.SysmonAdvancedEvents.AsNoTracking()
                                .Where(e => e.Timestamp > since)
                                .ToListAsync();

            // ── Reglas originales ─────────────────────────────────────────────
            await CheckLolBinsAsync(db, newProcs);
            await CheckEncodedPowerShellAsync(newProcs);
            await CheckSuspiciousPathsAsync(newProcs);
            await CheckSuspiciousDnsAsync(newDns);
            await CheckLateralMovementAsync(newNetwork);
            await CheckReverseShellAsync(db, newProcs, since);
            await CheckBruteForceAsync(db, since);
            await CheckRdpFromNewIpAsync(db, since);
            // ── Nuevas reglas ─────────────────────────────────────────────────
            await CheckScriptHostChildAsync(db, newProcs);
            await CheckPowerShellDropperAsync(newProcs);
            await CheckUncPathAsync(newProcs);
            await CheckShadowCopyDeletionAsync(newProcs);
            await CheckLolBinProxyAsync(db, newProcs);
            // ── Sysmon avanzado (IDs 7, 8, 10) ───────────────────────────────
            await CheckUnsignedImageAsync(newAdvanced.Where(e => e.EventId == 7).ToList());
            await CheckRemoteThreadAsync(newAdvanced.Where(e => e.EventId == 8).ToList());
            await CheckLsassAccessAsync(newAdvanced.Where(e => e.EventId == 10).ToList());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AlertWorker] Error: {ex.Message}");
        }
    }

    // ── Regla 1: Shell invocado por aplicación de documentos / navegador ─────
    private async Task CheckLolBinsAsync(EventDbContext db, List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(1)) return;
        var shellProcs = procs
            .Where(e => Shells.Contains(e.ProcessName) && e.ParentPid.HasValue)
            .ToList();

        if (shellProcs.Count == 0) return;

        var parentPids = shellProcs.Select(e => e.ParentPid!.Value).Distinct().ToList();

        // Buscar el CreateProcess más reciente de cada PID padre
        var parentEvents = db.ProcessEvents
            .AsNoTracking()
            .Where(e => e.EventType == "Create" && parentPids.Contains(e.Pid))
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var parentMap = parentEvents
            .GroupBy(e => e.Pid)
            .ToDictionary(g => g.Key, g => g.First().ProcessName);

        foreach (var ev in shellProcs)
        {
            if (!ev.ParentPid.HasValue) continue;
            if (!parentMap.TryGetValue(ev.ParentPid.Value, out var parentName)) continue;
            if (!DocumentApps.Contains(parentName)) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = ev.Timestamp,
                Severity    = "High",
                Rule        = "LOLBin – Shell desde app de documentos",
                Description = $"{parentName} lanzó {ev.ProcessName}",
                Pid         = ev.Pid,
                ProcessName = ev.ProcessName,
                Details     = $"Padre: {parentName} (PID {ev.ParentPid}) → Hijo: {ev.ProcessName} (PID {ev.Pid}) | CmdLine: {ev.CommandLine}",
                MitreTechnique = "T1059",
            });
        }
    }

    // ── Regla 2: PowerShell con comando codificado ────────────────────────────
    private async Task CheckEncodedPowerShellAsync(List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(2)) return;
        var suspicious = procs.Where(e =>
            (e.ProcessName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
             e.ProcessName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) &&
            e.CommandLine != null &&
            (e.CommandLine.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase) ||
             e.CommandLine.Contains(" -enc ", StringComparison.OrdinalIgnoreCase) ||
             e.CommandLine.Contains(" -e ", StringComparison.OrdinalIgnoreCase)));

        foreach (var ev in suspicious)
            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = ev.Timestamp,
                Severity    = "High",
                Rule        = "PowerShell Encoded",
                Description = "PowerShell ejecutado con comando codificado (posible ofuscación)",
                Pid         = ev.Pid,
                ProcessName = ev.ProcessName,
                Details     = ev.CommandLine,
                MitreTechnique = "T1059.001",
            });
    }

    // ── Regla 3: Proceso lanzado desde ruta sospechosa (Temp, Public…) ───────
    private async Task CheckSuspiciousPathsAsync(List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(3)) return;
        foreach (var ev in procs)
        {
            var cmdLine = ev.CommandLine;
            if (cmdLine == null) continue;

            var matchedPath = SuspiciousPaths
                .FirstOrDefault(p => cmdLine.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (matchedPath == null) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = ev.Timestamp,
                Severity    = "Medium",
                Rule        = "Proceso desde ruta sospechosa",
                Description = $"{ev.ProcessName} ejecutado desde directorio de escritura libre",
                Pid         = ev.Pid,
                ProcessName = ev.ProcessName,
                Details     = $"Ruta detectada: …{matchedPath}… | CmdLine: {cmdLine}",
                MitreTechnique = "T1036.005",
            });
        }
    }

    // ── Regla 4: Consulta DNS a TLD sospechoso ────────────────────────────────
    private async Task CheckSuspiciousDnsAsync(List<DnsEvent> dnsEvents)
    {
        if (!rulesService.IsEnabled(4)) return;
        foreach (var ev in dnsEvents)
        {
            var tld = SuspiciousTlds
                .FirstOrDefault(t => ev.QueryName.EndsWith(t, StringComparison.OrdinalIgnoreCase));
            if (tld == null) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = ev.Timestamp,
                Severity    = "Medium",
                Rule        = "DNS – TLD sospechoso",
                Description = $"Consulta DNS a dominio de TLD inusual ({tld})",
                Pid         = ev.Pid,
                ProcessName = ev.ProcessName,
                Details     = $"Query: {ev.QueryName} | IPs: {ev.QueryResults} | Estado: {ev.QueryStatus}",
                MitreTechnique = "T1568",
            });
        }
    }

    // ── Regla 5: Movimiento lateral (conexión a puertos de administración) ────
    private async Task CheckLateralMovementAsync(List<NetworkEvent> netEvents)
    {
        if (!rulesService.IsEnabled(5)) return;
        foreach (var ev in netEvents)
        {
            if (!ev.DestinationPort.HasValue) continue;
            if (!LateralMovementPorts.Contains(ev.DestinationPort.Value)) continue;
            if (ev.UserName != null && SystemUsers.Contains(ev.UserName)) continue;

            var portName = PortNames.GetValueOrDefault(ev.DestinationPort.Value, ev.DestinationPort.Value.ToString());
            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = ev.Timestamp,
                Severity    = "Medium",
                Rule        = "Movimiento lateral – Puerto de administración",
                Description = $"{ev.ProcessName} conectó a {portName} ({ev.DestinationIp}:{ev.DestinationPort})",
                Pid         = ev.Pid,
                ProcessName = ev.ProcessName,
                Details     = $"Usuario: {ev.UserName} | Proto: {ev.Protocol} | Dest: {ev.DestinationIp}:{ev.DestinationPort} ({portName})",
                MitreTechnique = "T1021",
            });
        }
    }

    // ── Regla 6: Posible reverse shell (shell + conexión de red reciente) ─────
    private async Task CheckReverseShellAsync(EventDbContext db, List<Models.ProcessEvent> procs, DateTime since)
    {
        if (!rulesService.IsEnabled(6)) return;
        var shellProcs = procs.Where(e => Shells.Contains(e.ProcessName)).ToList();
        if (shellProcs.Count == 0) return;

        var shellPids = shellProcs.Select(e => e.Pid).Distinct().ToList();

        // Buscar NetworkEvents de esos PIDs en la ventana since..now
        var netEvents = await db.NetworkEvents.AsNoTracking()
            .Where(e => shellPids.Contains(e.Pid) && e.Timestamp >= since && e.Initiated)
            .ToListAsync();

        foreach (var net in netEvents)
        {
            var proc = shellProcs.FirstOrDefault(p => p.Pid == net.Pid);
            if (proc is null) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = net.Timestamp,
                Severity    = "High",
                Rule        = "Reverse Shell – Shell con conexión de red",
                Description = $"{proc.ProcessName} (PID {proc.Pid}) realizó conexión de red (posible reverse shell)",
                Pid         = proc.Pid,
                ProcessName = proc.ProcessName,
                Details        = $"Destino: {net.DestinationIp}:{net.DestinationPort} | CmdLine: {proc.CommandLine}",
                MitreTechnique = "T1059",
            });
        }
    }

    // ── Regla 7: Fuerza bruta — N fallos de logon desde misma IP en 5 min ────
    private async Task CheckBruteForceAsync(EventDbContext db, DateTime since)
    {
        if (!rulesService.IsEnabled(7)) return;
        const int threshold = 5;
        var window = since.AddMinutes(-5); // ventana deslizante de 5 min antes del último scan

        var attackers = await db.LogonEvents.AsNoTracking()
            .Where(e => !e.Success && e.SourceIp != null && e.Timestamp >= window)
            .GroupBy(e => e.SourceIp!)
            .Where(g => g.Count() >= threshold)
            .Select(g => new { ip = g.Key, count = g.Count(), lastAt = g.Max(x => x.Timestamp) })
            .ToListAsync();

        foreach (var att in attackers)
        {
            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = att.lastAt,
                Severity    = "High",
                Rule        = "Fuerza bruta – Logon fallido repetido",
                Description = $"{att.count} intentos de logon fallidos desde {att.ip} en 5 min",
                Pid         = null,
                ProcessName = null,
                Details     = $"IP atacante: {att.ip} | Intentos: {att.count} en ventana de 5 min",
                MitreTechnique = "T1110",
            });
        }
    }

    // ── Regla 8: RDP desde IP nueva (no vista en últimas 7 días) ─────────────
    private async Task CheckRdpFromNewIpAsync(EventDbContext db, DateTime since)
    {
        if (!rulesService.IsEnabled(8)) return;
        var newRdp = await db.LogonEvents.AsNoTracking()
            .Where(e => e.Success && e.LogonType == 10 && e.SourceIp != null && e.Timestamp >= since)
            .ToListAsync();

        if (newRdp.Count == 0) return;

        var knownIps = await db.LogonEvents.AsNoTracking()
            .Where(e => e.Success && e.LogonType == 10 && e.SourceIp != null
                     && e.Timestamp >= since.AddDays(-7) && e.Timestamp < since)
            .Select(e => e.SourceIp!)
            .Distinct()
            .ToListAsync();

        foreach (var ev in newRdp)
        {
            if (knownIps.Contains(ev.SourceIp!)) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp   = ev.Timestamp,
                Severity    = "Medium",
                Rule        = "RDP desde IP nueva",
                Description = $"Sesión RDP exitosa desde IP no vista en 7 días: {ev.SourceIp}",
                Pid         = null,
                ProcessName = null,
                Details     = $"Usuario: {ev.UserName} | IP: {ev.SourceIp}:{ev.SourcePort} | Estación: {ev.WorkstationName}",
                MitreTechnique = "T1021.001",
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // NUEVAS REGLAS (9-13) — Detección sin nuevas fuentes de datos
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
        { "wscript.exe", "cscript.exe", "mshta.exe" };

    private static readonly HashSet<string> ProxyBins = new(StringComparer.OrdinalIgnoreCase)
        { "msiexec.exe", "regsvr32.exe", "wmic.exe", "odbcconf.exe", "installutil.exe" };

    private static readonly string[] DropperKeywords =
        ["Invoke-Expression", " IEX ", "(IEX", "DownloadString", "DownloadFile",
         "Net.WebClient", "Invoke-WebRequest", "iwr ", "FromBase64String", "[Convert]::"];

    private static readonly string[] ShadowDeleteKeywords =
        ["delete shadows", "shadowcopy delete", "shadowstorage", "recoveryenabled no",
         "vssadmin delete", "wbadmin delete catalog", "bcdedit /set"];

    // ── Regla 9: Shell lanzado desde wscript / cscript ────────────────────
    private async Task CheckScriptHostChildAsync(EventDbContext db, List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(9)) return;
        var children = procs.Where(e => e.ParentPid.HasValue).ToList();
        if (children.Count == 0) return;

        var parentPids = children.Select(e => e.ParentPid!.Value).Distinct().ToList();
        var parentMap  = db.ProcessEvents.AsNoTracking()
            .Where(e => e.EventType == "Create" && parentPids.Contains(e.Pid))
            .OrderByDescending(e => e.Timestamp)
            .ToList()
            .GroupBy(e => e.Pid)
            .ToDictionary(g => g.Key, g => g.First().ProcessName);

        foreach (var ev in children)
        {
            if (!ev.ParentPid.HasValue) continue;
            if (!parentMap.TryGetValue(ev.ParentPid.Value, out var parentName)) continue;
            if (!ScriptHosts.Contains(parentName)) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(9, "High"),
                Rule           = "Script Host – Hijo de wscript/cscript",
                Description    = $"{parentName} lanzó {ev.ProcessName} (posible macro maliciosa)",
                Pid            = ev.Pid,
                ProcessName    = ev.ProcessName,
                Details        = $"Padre: {parentName} (PID {ev.ParentPid}) → {ev.ProcessName} (PID {ev.Pid}) | Cmd: {ev.CommandLine}",
                MitreTechnique = "T1059.005",
            });
        }
    }

    // ── Regla 10: PowerShell con indicadores de dropper ───────────────────
    private async Task CheckPowerShellDropperAsync(List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(10)) return;
        var psProcs = procs.Where(e =>
            (e.ProcessName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
             e.ProcessName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) &&
            e.CommandLine != null);

        foreach (var ev in psProcs)
        {
            var kw = DropperKeywords.FirstOrDefault(k =>
                ev.CommandLine!.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (kw == null) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(10, "High"),
                Rule           = "PowerShell Dropper",
                Description    = $"PowerShell con indicador de descarga/ejecución dinámica ({kw.Trim()})",
                Pid            = ev.Pid,
                ProcessName    = ev.ProcessName,
                Details        = ev.CommandLine,
                MitreTechnique = "T1059.001",
            });
        }
    }

    // ── Regla 11: Ejecución desde UNC path ────────────────────────────────
    private async Task CheckUncPathAsync(List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(11)) return;
        foreach (var ev in procs)
        {
            var cmd = ev.CommandLine;
            if (cmd == null) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(cmd, @"\\\\[a-zA-Z0-9_.\-]+\\")) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(11, "High"),
                Rule           = "Ejecución desde UNC Path",
                Description    = $"{ev.ProcessName} ejecutado desde ruta de red (UNC)",
                Pid            = ev.Pid,
                ProcessName    = ev.ProcessName,
                Details        = $"CmdLine: {cmd}",
                MitreTechnique = "T1021.002",
            });
        }
    }

    // ── Regla 12: vssadmin / wmic eliminando shadow copies ────────────────
    private async Task CheckShadowCopyDeletionAsync(List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(12)) return;
        foreach (var ev in procs)
        {
            var cmd = ev.CommandLine;
            if (cmd == null) continue;
            var kw = ShadowDeleteKeywords.FirstOrDefault(k =>
                cmd.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (kw == null) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(12, "High"),
                Rule           = "Eliminación de Shadow Copies",
                Description    = $"{ev.ProcessName} intentó eliminar copias de seguridad del sistema",
                Pid            = ev.Pid,
                ProcessName    = ev.ProcessName,
                Details        = $"Indicador: '{kw.Trim()}' | CmdLine: {cmd}",
                MitreTechnique = "T1490",
            });
        }
    }

    // ── Regla 13: Shell lanzado desde binario proxy (msiexec / regsvr32…) ─
    private async Task CheckLolBinProxyAsync(EventDbContext db, List<Models.ProcessEvent> procs)
    {
        if (!rulesService.IsEnabled(13)) return;
        var shellProcs = procs.Where(e => Shells.Contains(e.ProcessName) && e.ParentPid.HasValue).ToList();
        if (shellProcs.Count == 0) return;

        var parentPids = shellProcs.Select(e => e.ParentPid!.Value).Distinct().ToList();
        var parentMap  = db.ProcessEvents.AsNoTracking()
            .Where(e => e.EventType == "Create" && parentPids.Contains(e.Pid))
            .OrderByDescending(e => e.Timestamp)
            .ToList()
            .GroupBy(e => e.Pid)
            .ToDictionary(g => g.Key, g => g.First().ProcessName);

        foreach (var ev in shellProcs)
        {
            if (!ev.ParentPid.HasValue) continue;
            if (!parentMap.TryGetValue(ev.ParentPid.Value, out var parentName)) continue;
            if (!ProxyBins.Contains(parentName)) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(13, "High"),
                Rule           = "LOLBin – Proxy de confianza",
                Description    = $"{parentName} lanzó {ev.ProcessName} (shell via binario proxy del sistema)",
                Pid            = ev.Pid,
                ProcessName    = ev.ProcessName,
                Details        = $"Padre: {parentName} (PID {ev.ParentPid}) → {ev.ProcessName} (PID {ev.Pid}) | Cmd: {ev.CommandLine}",
                MitreTechnique = "T1218",
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // REGLAS SYSMON AVANZADO (14-16)
    // ══════════════════════════════════════════════════════════════════════════

    // ── Regla 14: DLL sin firma cargada (Sysmon ID 7) ─────────────────────
    private async Task CheckUnsignedImageAsync(List<Models.SysmonAdvancedEvent> events)
    {
        if (!rulesService.IsEnabled(14)) return;
        foreach (var ev in events)
        {
            if (ev.Signed == true) continue;
            // Reducir ruido: excluir rutas de sistema conocidas
            var path = ev.ImagePath?.ToLowerInvariant() ?? "";
            if (path.StartsWith(@"c:\windows\winsxs\") ||
                path.StartsWith(@"c:\windows\system32\") ||
                path.StartsWith(@"c:\windows\syswow64\")) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(14, "Medium"),
                Rule           = "DLL sin firma cargada",
                Description    = $"{ev.SourceProcessName} cargó imagen sin firma digital",
                Pid            = ev.SourcePid,
                ProcessName    = ev.SourceProcessName,
                Details        = $"Imagen: {ev.ImagePath} | Estado: {ev.SignatureStatus ?? "Unsigned"} | SHA256: {ev.Sha256}",
                MitreTechnique = "T1055.001",
            });
        }
    }

    // ── Regla 15: CreateRemoteThread en proceso externo (Sysmon ID 8) ─────
    private async Task CheckRemoteThreadAsync(List<Models.SysmonAdvancedEvent> events)
    {
        if (!rulesService.IsEnabled(15)) return;
        foreach (var ev in events)
        {
            if (ev.TargetPid == ev.SourcePid) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(15, "High"),
                Rule           = "CreateRemoteThread – Inyección de hilo",
                Description    = $"{ev.SourceProcessName} inyectó hilo en {ev.TargetProcessName}",
                Pid            = ev.SourcePid,
                ProcessName    = ev.SourceProcessName,
                Details        = $"Fuente: {ev.SourceProcessName} (PID {ev.SourcePid}) → Destino: {ev.TargetProcessName} (PID {ev.TargetPid}) | Addr: {ev.StartAddress} | Módulo: {ev.StartModule}",
                MitreTechnique = "T1055",
            });
        }
    }

    // ── Regla 16: Acceso a lsass.exe (Sysmon ID 10) ───────────────────────
    private async Task CheckLsassAccessAsync(List<Models.SysmonAdvancedEvent> events)
    {
        if (!rulesService.IsEnabled(16)) return;
        foreach (var ev in events)
        {
            if (!string.Equals(ev.TargetProcessName, "lsass.exe", StringComparison.OrdinalIgnoreCase)) continue;

            await alertService.AddAsync(new AlertEvent
            {
                Timestamp      = ev.Timestamp,
                Severity       = rulesService.GetSeverity(16, "High"),
                Rule           = "Acceso a LSASS – Volcado de credenciales",
                Description    = $"{ev.SourceProcessName} accedió a lsass.exe (posible credential dump)",
                Pid            = ev.SourcePid,
                ProcessName    = ev.SourceProcessName,
                Details        = $"Fuente: {ev.SourceProcessName} (PID {ev.SourcePid}) | GrantedAccess: {ev.GrantedAccess} | CallTrace: {(ev.CallTrace?.Length > 200 ? ev.CallTrace[..200] + "…" : ev.CallTrace)}",
                MitreTechnique = "T1003.001",
            });
        }
    }
}
