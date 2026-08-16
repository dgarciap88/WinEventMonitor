using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

/// <summary>Parser para Sysmon ID 13 – RegistryEvent (Value Set). Usado para detectar persistencia.</summary>
public static class RegistryEventParser
{
    public static SysmonAdvancedEvent? FromSysmon(EventRecord e)
    {
        try
        {
            var data = ImageLoadParser.ParseEventData(e);
            return new SysmonAdvancedEvent
            {
                Timestamp         = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventId           = 13,
                SourcePid         = int.TryParse(data.GetValueOrDefault("ProcessId"), out var pid) ? pid : 0,
                SourceProcessName = Path.GetFileName(data.GetValueOrDefault("Image") ?? string.Empty),
                TargetObject      = data.GetValueOrDefault("TargetObject"),
                RegistryDetails   = data.GetValueOrDefault("Details"),
            };
        }
        catch { return null; }
    }
}
