using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

public static class ProcessTerminateParser
{
    // Sysmon Event ID 5
    public static ProcessEvent? FromSysmon(EventRecord e)
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

            return new ProcessEvent
            {
                Timestamp   = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventType   = "Terminate",
                EventSource = "Sysmon",
                Pid         = int.TryParse(data.GetValueOrDefault("ProcessId"), out var pid) ? pid : 0,
                ProcessName = Path.GetFileName(data.GetValueOrDefault("Image") ?? string.Empty),
                IsElevated  = false,
            };
        }
        catch { return null; }
    }

    // Security Event ID 4689
    public static ProcessEvent? FromSecurity(EventRecord e)
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

            return new ProcessEvent
            {
                Timestamp   = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventType   = "Terminate",
                EventSource = "Security",
                Pid         = Convert.ToInt32(data.GetValueOrDefault("ProcessId") ?? "0", 16),
                ProcessName = Path.GetFileName(data.GetValueOrDefault("ProcessName") ?? string.Empty),
                UserName    = $"{data.GetValueOrDefault("SubjectDomainName")}\\{data.GetValueOrDefault("SubjectUserName")}",
                IsElevated  = false,
            };
        }
        catch { return null; }
    }
}
