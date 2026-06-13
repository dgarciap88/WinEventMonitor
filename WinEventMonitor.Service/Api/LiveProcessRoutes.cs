using System.Management;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using WinEventMonitor.Service.Data;
using WinEventMonitor.Service.Models;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class LiveProcessRoutes
{
    public static void MapLiveProcessRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/processes");

        // --- Árbol en vivo: WMI + enriquecido con la BD ---------------------
        group.MapGet("/live", async (EventDbContext db, SystemHealthService sysHealth) =>
        {
            // Métricas en caché (CPU delta + WorkingSet) — snapshot de hace <2s
            var metricByPid = sysHealth.GetMetricByPid();
            var processes = GetWmiProcesses(metricByPid);

            // Enriquecer con último evento Create de BD (últimas 24h) por PID
            var livePids = processes.Select(p => p.Pid).Distinct().ToList();
            var dbCreates = await db.ProcessEvents
                .Where(e => e.EventType == "Create" && livePids.Contains(e.Pid))
                .OrderByDescending(e => e.Timestamp)
                .ToListAsync();

            var dbLookup = dbCreates
                .GroupBy(e => e.Pid)
                .ToDictionary(g => g.Key, g => g.First());

            var enriched = processes.Select(p =>
            {
                if (dbLookup.TryGetValue(p.Pid, out var ev))
                    return p with
                    {
                        UserName       = p.UserName ?? ev.UserName,
                        // Si la lectura nativa falló (null → false), el registro de BD puede
                        // corregirlo; si la nativa dice true, siempre prevalece.
                        IsElevated     = p.IsElevated || ev.IsElevated,
                        IntegrityLevel = p.IntegrityLevel ?? ev.IntegrityLevel,
                        Sha256         = ev.Sha256,
                    };
                return p;
            }).ToList();

            return Results.Ok(enriched);
        });

        // --- Árbol histórico: eventos Create de la BD en una ventana de tiempo
        group.MapGet("/tree", async (EventDbContext db, int hours = 1) =>
        {
            var clampedHours = Math.Max(1, Math.Min(hours, 168)); // 1h – 7d
            var cutoff = DateTime.UtcNow.AddHours(-clampedHours);

            var events = await db.ProcessEvents
                .Where(e => e.EventType == "Create" && e.Timestamp >= cutoff)
                .OrderBy(e => e.Timestamp)
                .Take(3000)
                .ToListAsync();

            var result = events.Select(e => new LiveProcess(
                Pid:            e.Pid,
                ParentPid:      e.ParentPid ?? 0,
                Name:           e.ProcessName,
                CommandLine:    e.CommandLine,
                ExecutablePath: null,
                UserName:       e.UserName,
                IsElevated:     e.IsElevated,
                IntegrityLevel: e.IntegrityLevel,
                Sha256:         e.Sha256,
                Source:         "db"
            )).ToList();

            return Results.Ok(result);
        });
    }

    // -------------------------------------------------------------------------
    // WMI: consulta Win32_Process para obtener todos los procesos en ejecución
    // -------------------------------------------------------------------------
    private static List<LiveProcess> GetWmiProcesses(IReadOnlyDictionary<int, ProcessMetric>? metrics = null)
    {
        var result = new List<LiveProcess>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process");

            foreach (ManagementObject obj in searcher.Get())
            {
                var pid       = Convert.ToInt32(obj["ProcessId"]);
                var parentPid = Convert.ToInt32(obj["ParentProcessId"]);
                var name      = obj["Name"]?.ToString() ?? string.Empty;
                var cmdLine   = obj["CommandLine"]?.ToString();
                var exePath   = obj["ExecutablePath"]?.ToString();

                var isElevated = TryGetTokenElevation(pid) ?? false;

                // Métricas de CPU y RAM desde el snapshot de SystemHealthService
                ProcessMetric? metric = null;
                metrics?.TryGetValue(pid, out metric);

                result.Add(new LiveProcess(
                    Pid:            pid,
                    ParentPid:      parentPid,
                    Name:           name,
                    CommandLine:    cmdLine,
                    ExecutablePath: exePath,
                    UserName:       null,
                    IsElevated:     isElevated,
                    IntegrityLevel: null,
                    Sha256:         null,
                    Source:         "live",
                    CpuPercent:     metric?.CpuPercent,
                    WorkingSetMb:   metric?.WorkingSetMb
                ));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WMI error: {ex.Message}");
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // P/Invoke: lectura directa del token de elevación de Windows
    // Requiere que el servicio corra como Administrador (PROCESS_QUERY_LIMITED_INFORMATION
    // no necesita SeDebugPrivilege; falla silenciosamente en procesos protegidos).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Devuelve true si el proceso tiene token elevado, false si no, null si no es accesible.
    /// </summary>
    private static bool? TryGetTokenElevation(int pid)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint TOKEN_QUERY = 0x0008;
        const int TOKEN_ELEVATION_CLASS = 20; // TokenElevation

        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken)) return null;
            try
            {
                int size   = Marshal.SizeOf<TOKEN_ELEVATION>();
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(hToken, TOKEN_ELEVATION_CLASS, buffer, (uint)size, out _))
                        return null;
                    var elev = Marshal.PtrToStructure<TOKEN_ELEVATION>(buffer);
                    return elev.TokenIsElevated != 0;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(hToken); }
        }
        finally { CloseHandle(hProcess); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION { public int TokenIsElevated; }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint access, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInfoClass,
        IntPtr tokenInfo, uint tokenInfoLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
