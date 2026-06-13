using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

/// <summary>Parser para Sysmon ID 10 – ProcessAccess.</summary>
public static class ProcessAccessParser
{
    public static SysmonAdvancedEvent? FromSysmon(EventRecord e)
    {
        try
        {
            var data = ImageLoadParser.ParseEventData(e);
            return new SysmonAdvancedEvent
            {
                Timestamp         = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventId           = 10,
                SourcePid         = int.TryParse(data.GetValueOrDefault("SourceProcessId"), out var spid) ? spid : 0,
                SourceProcessName = Path.GetFileName(data.GetValueOrDefault("SourceImage") ?? string.Empty),
                TargetPid         = int.TryParse(data.GetValueOrDefault("TargetProcessId"), out var tpid) ? tpid : null,
                TargetProcessName = Path.GetFileName(data.GetValueOrDefault("TargetImage") ?? string.Empty),
                GrantedAccess     = data.GetValueOrDefault("GrantedAccess"),
                CallTrace         = data.GetValueOrDefault("CallTrace"),
            };
        }
        catch { return null; }
    }
}
