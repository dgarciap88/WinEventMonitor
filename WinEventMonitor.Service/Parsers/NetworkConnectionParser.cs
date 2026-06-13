using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

public static class NetworkConnectionParser
{
    // Sysmon Event ID 3
    public static NetworkEvent? FromSysmon(EventRecord e)
    {
        try
        {
            var xml = e.ToXml();
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var data = doc.Descendants(System.Xml.Linq.XName.Get("Data", ns))
                          .ToDictionary(
                              d => d.Attribute("Name")?.Value ?? string.Empty,
                              d => d.Value);

            // Filtrar solo conexiones salientes (Initiated = true)
            if (!string.Equals(data.GetValueOrDefault("Initiated"), "true", StringComparison.OrdinalIgnoreCase))
                return null;

            return new NetworkEvent
            {
                Timestamp       = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                Pid             = int.TryParse(data.GetValueOrDefault("ProcessId"), out var pid) ? pid : 0,
                ProcessName     = Path.GetFileName(data.GetValueOrDefault("Image") ?? string.Empty),
                ExecutablePath  = data.GetValueOrDefault("Image"),
                UserName        = data.GetValueOrDefault("User"),
                Protocol        = data.GetValueOrDefault("Protocol")?.ToUpperInvariant(),
                SourceIp        = data.GetValueOrDefault("SourceIp"),
                SourcePort      = int.TryParse(data.GetValueOrDefault("SourcePort"), out var sp) ? sp : null,
                DestinationIp   = data.GetValueOrDefault("DestinationIp"),
                DestinationPort = int.TryParse(data.GetValueOrDefault("DestinationPort"), out var dp) ? dp : null,
                Initiated       = true,
            };
        }
        catch { return null; }
    }
}
