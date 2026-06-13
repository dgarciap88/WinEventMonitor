using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

public static class DnsQueryParser
{
    // Sysmon Event ID 22
    public static DnsEvent? FromSysmon(EventRecord e)
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

            return new DnsEvent
            {
                Timestamp    = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                Pid          = int.TryParse(data.GetValueOrDefault("ProcessId"), out var pid) ? pid : 0,
                ProcessName  = Path.GetFileName(data.GetValueOrDefault("Image") ?? string.Empty),
                UserName     = data.GetValueOrDefault("User"),
                QueryName    = data.GetValueOrDefault("QueryName") ?? string.Empty,
                QueryResults = data.GetValueOrDefault("QueryResults"),
                QueryStatus  = data.GetValueOrDefault("QueryStatus"),
            };
        }
        catch { return null; }
    }
}
