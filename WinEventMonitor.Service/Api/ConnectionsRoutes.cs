using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class ConnectionsRoutes
{
    public static void MapConnectionsRoutes(this WebApplication app)
    {
        // GET /api/system/connections — puertos en escucha + conexiones activas por proceso
        app.MapGet("/api/system/connections", (SystemHealthService _) =>
        {
            var connections = GetActiveConnections();
            return Results.Ok(connections);
        });
    }

    // ─── Conexiones TCP activas via .NET NetworkInformation + P/Invoke ────────

    private static object GetActiveConnections()
    {
        var tcpRows    = GetExtendedTcpTable();
        var listenRows = tcpRows.Where(r => r.State == TcpState.Listen).ToList();
        var estabRows  = tcpRows.Where(r => r.State == TcpState.Established).ToList();

        // Complementar con nombres de proceso
        var pidNames  = GetProcessNames();

        var listening = listenRows.Select(r => new
        {
            protocol    = "TCP",
            localPort   = r.LocalEndPoint.Port,
            localIp     = r.LocalEndPoint.Address.ToString(),
            state       = "LISTEN",
            pid         = r.OwningPid,
            processName = pidNames.GetValueOrDefault(r.OwningPid, "?"),
        }).OrderBy(r => r.localPort).ToList();

        var established = estabRows.Select(r => new
        {
            protocol      = "TCP",
            localPort     = r.LocalEndPoint.Port,
            localIp       = r.LocalEndPoint.Address.ToString(),
            remoteIp      = r.RemoteEndPoint.Address.ToString(),
            remotePort    = r.RemoteEndPoint.Port,
            state         = "ESTABLISHED",
            pid           = r.OwningPid,
            processName   = pidNames.GetValueOrDefault(r.OwningPid, "?"),
        }).OrderBy(r => r.localPort).ToList();

        return new { listening, established, generatedAt = DateTime.UtcNow };
    }

    private static Dictionary<int, string> GetProcessNames()
    {
        var dict = new Dictionary<int, string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { dict[p.Id] = p.ProcessName; }
            catch { /* proceso ya terminado */ }
            finally { p.Dispose(); }
        }
        return dict;
    }

    // ─── P/Invoke: GetExtendedTcpTable ────────────────────────────────────────

    private record TcpRow(IPEndPoint LocalEndPoint, IPEndPoint RemoteEndPoint, TcpState State, int OwningPid);

    private static List<TcpRow> GetExtendedTcpTable()
    {
        var rows  = new List<TcpRow>();
        var size  = 0;
        var buf   = IntPtr.Zero;

        try
        {
            // Primera llamada para obtener el tamaño necesario
            GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2 /*AF_INET*/, TcpTableClass.TCP_TABLE_OWNER_PID_ALL, 0);
            buf = Marshal.AllocHGlobal(size);
            if (GetExtendedTcpTable(buf, ref size, true, 2, TcpTableClass.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return rows;

            var numEntries = Marshal.ReadInt32(buf);
            var offset     = 4; // después del campo dwNumEntries
            int rowSize    = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buf + offset);
                offset += rowSize;

                var localIp    = new IPAddress(row.dwLocalAddr);
                var remoteIp   = new IPAddress(row.dwRemoteAddr);
                var localPort  = NetworkToHostOrder16(row.dwLocalPort);
                var remotePort = NetworkToHostOrder16(row.dwRemotePort);
                var state      = (TcpState)row.dwState;

                rows.Add(new TcpRow(
                    new IPEndPoint(localIp,  localPort),
                    new IPEndPoint(remoteIp, remotePort),
                    state,
                    row.dwOwningPid));
            }
        }
        catch { /* sin permisos o plataforma no soportada */ }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }

        return rows;
    }

    private static int NetworkToHostOrder16(uint n) =>
        (int)(((n & 0xFF) << 8) | ((n >> 8) & 0xFF));

    private enum TcpTableClass { TCP_TABLE_OWNER_PID_ALL = 5 }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public int  dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize,
        bool bOrder, int ulAf, TcpTableClass tableClass, uint reserved);
}
