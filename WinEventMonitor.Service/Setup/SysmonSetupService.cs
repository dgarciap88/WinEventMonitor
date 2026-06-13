using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;

namespace WinEventMonitor.Service.Setup;

public class SysmonSetupService(IServiceScopeFactory scopeFactory, IConfiguration config)
{
    // Sysmon puede estar en distintas rutas según si es 32 o 64 bits
    private static readonly string[] SysmonPaths =
    [
        @"C:\Windows\Sysmon64.exe",
        @"C:\Windows\Sysmon.exe",
        @"C:\Tools\Sysmon64.exe",
        @"C:\Tools\Sysmon.exe",
    ];

    private static readonly string ConfigPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WinEventMonitor", "sysmon-config.xml");

    // Fichero centinela: indica que nuestra config se aplicó correctamente
    private static readonly string AppliedFlagPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WinEventMonitor", "sysmon-config.applied");

    // GUID de la subcategoría "Process Creation" - independiente del idioma del SO
    private const string ProcessCreationGuid = "{0CCE922B-69AE-11D9-BED3-505054503030}";

    // GUID de la subcategoría "Logon" (cubre eventos 4624 y 4625)
    private const string LogonGuid = "{0CCE9215-69AE-11D9-BED3-505054503030}";

    public SetupStatus GetStatus()
    {
        var execPath = SysmonPaths.FirstOrDefault(File.Exists);
        return new SetupStatus(
            new SysmonStatus(
                ExecutableFound: execPath is not null,
                ExecutablePath: execPath,
                ServiceRunning: IsSysmonServiceRunning(),
                ConfigApplied: File.Exists(AppliedFlagPath)),
            new AuditPolicyStatus(
                ProcessCreationEnabled: IsProcessCreationAuditEnabled(),
                LogonAuditEnabled: IsLogonAuditEnabled()),
            GetStorageInfo());
    }

    private StorageInfo GetStorageInfo()
    {
        var dbPath = config["EventMonitor:DatabasePath"]
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WinEventMonitor", "events.db");

        var sizeBytes = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0L;
        var retentionDays = config.GetValue<int>("EventMonitor:RetentionDays", 30);

        long processes = 0, network = 0, dns = 0, logons = 0;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
            processes = db.ProcessEvents.LongCount();
            network   = db.NetworkEvents.LongCount();
            dns       = db.DnsEvents.LongCount();
            logons    = db.LogonEvents.LongCount();
        }
        catch { /* ignorar si la BD aún no está lista */ }

        return new StorageInfo(
            FileSizeBytes: sizeBytes,
            FileSizeMb: (sizeBytes / 1024.0 / 1024.0).ToString("F2"),
            RetentionDays: retentionDays,
            TotalProcessEvents: processes,
            TotalNetworkEvents: network,
            TotalDnsEvents: dns,
            TotalLogonEvents: logons);
    }

    public (bool Success, string Message) ApplySysmonConfig()
    {
        var execPath = SysmonPaths.FirstOrDefault(File.Exists);
        if (execPath is null)
            return (false, "No se encontró el ejecutable de Sysmon (Sysmon64.exe / Sysmon.exe).");

        // Escribe el XML de configuración a disco
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, SysmonConfigXml, Encoding.UTF8);

        var (exitCode, output) = RunProcess(execPath, $"-accepteula -c \"{ConfigPath}\"");
        if (exitCode == 0)
        {
            // Marca que nuestra config fue aplicada
            File.WriteAllText(AppliedFlagPath, DateTime.UtcNow.ToString("O"));
            return (true, "Configuración Sysmon aplicada correctamente (IDs 1, 3, 5, 7, 8, 10, 22).");
        }

        return (false, $"Error al aplicar configuración Sysmon (exit {exitCode}): {output.Trim()}");
    }

    public (bool Success, string Message) EnableAuditPolicy()
    {
        var (exitCode, output) = RunProcess(
            @"C:\Windows\System32\auditpol.exe",
            $"/set /subcategory:\"{ProcessCreationGuid}\" /success:enable");

        if (exitCode == 0)
            return (true, "Política de auditoría 'Process Creation' habilitada correctamente.");

        return (false, $"Error al habilitar auditoría (exit {exitCode}): {output.Trim()}");
    }

    private static bool IsSysmonServiceRunning()
    {
        // Usamos sc.exe para no depender de System.ServiceProcess
        foreach (var svcName in new[] { "Sysmon64", "Sysmon" })
        {
            var (_, output) = RunProcess(@"C:\Windows\System32\sc.exe", $"query {svcName}");
            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsProcessCreationAuditEnabled()
    {
        try
        {
            var (_, output) = RunProcess(
                @"C:\Windows\System32\auditpol.exe",
                $"/get /subcategory:\"{ProcessCreationGuid}\"");
            var notAuditing = output.Contains("No Auditing", StringComparison.OrdinalIgnoreCase)
                           || output.Contains("No auditando", StringComparison.OrdinalIgnoreCase);
            return !notAuditing && output.Length > 50;
        }
        catch { return false; }
    }

    private static bool IsLogonAuditEnabled()
    {
        try
        {
            var (_, output) = RunProcess(
                @"C:\Windows\System32\auditpol.exe",
                $"/get /subcategory:\"{LogonGuid}\"");
            // auditpol muestra "No Auditing" (EN) / "No auditando" (ES) cuando está desactivado.
            // Cualquier otro valor indica que hay algún tipo de auditoría activa.
            // Comprobamos que al menos sucesos correctos O erróneos estén habilitados.
            var notAuditing = output.Contains("No Auditing", StringComparison.OrdinalIgnoreCase)
                           || output.Contains("No auditando", StringComparison.OrdinalIgnoreCase);
            return !notAuditing && output.Length > 50; // output vacío = no se pudo leer
        }
        catch { return false; }
    }

    public (bool Success, string Message) EnableLogonAuditPolicy()
    {
        var (exitCode, output) = RunProcess(
            @"C:\Windows\System32\auditpol.exe",
            $"/set /subcategory:\"{LogonGuid}\" /success:enable /failure:enable");

        if (exitCode == 0)
            return (true, "Política de auditoría 'Logon' habilitada correctamente (eventos 4624 y 4625).");

        return (false, $"Error al habilitar auditoría de logon (exit {exitCode}): {output.Trim()}");
    }

    private static (int ExitCode, string Output) RunProcess(string exe, string args)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        proc.Start();
        // Leer ambos streams antes de WaitForExit para evitar deadlock
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(15_000);
        return (proc.ExitCode, stdoutTask.Result + stderrTask.Result);
    }

    // Configuración MVP embebida: captura IDs 1, 3, 5 y 22 con filtros de ruido mínimos
    private const string SysmonConfigXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Sysmon schemaversion="4.90">
          <EventFiltering>

            <!-- ID 1: Process Create -->
            <ProcessCreate onmatch="exclude">
              <Image condition="begin with">C:\Windows\SoftwareDistribution</Image>
              <Image condition="is">C:\Windows\System32\WmiPrvSE.exe</Image>
              <Image condition="is">C:\Windows\System32\conhost.exe</Image>
              <Image condition="begin with">C:\ProgramData\Microsoft\Windows Defender</Image>
            </ProcessCreate>

            <!-- ID 3: Network Connect (solo salientes, sin loopback ni multicast) -->
            <NetworkConnect onmatch="exclude">
              <DestinationIp condition="is">127.0.0.1</DestinationIp>
              <DestinationIp condition="is">::1</DestinationIp>
              <DestinationIp condition="begin with">239.</DestinationIp>
              <DestinationIp condition="begin with">224.</DestinationIp>
              <DestinationPort condition="is">5353</DestinationPort>
              <DestinationPort condition="is">5355</DestinationPort>
            </NetworkConnect>

            <!-- ID 5: Process Terminate -->
            <ProcessTerminate onmatch="exclude">
              <Image condition="is">C:\Windows\System32\WmiPrvSE.exe</Image>
              <Image condition="is">C:\Windows\System32\conhost.exe</Image>
            </ProcessTerminate>

            <!-- ID 22: DNS Query -->
            <DnsQuery onmatch="exclude">
              <QueryName condition="end with">.local</QueryName>
              <QueryName condition="end with">.arpa</QueryName>
              <QueryName condition="is">wpad</QueryName>
            </DnsQuery>

            <!-- ID 7: Image Load — solo imágenes sin firma desde rutas no-sistema -->
            <ImageLoad onmatch="include">
              <Signed condition="is">false</Signed>
            </ImageLoad>

            <!-- ID 8: CreateRemoteThread — excluir WMI (ruidoso pero legítimo) -->
            <CreateRemoteThread onmatch="exclude">
              <SourceImage condition="is">C:\Windows\System32\wbem\WmiPrvSE.exe</SourceImage>
              <SourceImage condition="is">C:\Windows\System32\svchost.exe</SourceImage>
            </CreateRemoteThread>

            <!-- ID 10: ProcessAccess — solo accesos a lsass.exe (credential dump) -->
            <ProcessAccess onmatch="include">
              <TargetImage condition="end with">lsass.exe</TargetImage>
            </ProcessAccess>

          </EventFiltering>
        </Sysmon>
        """;
}
