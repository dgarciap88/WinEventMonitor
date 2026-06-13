using System.Diagnostics.Eventing.Reader;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

public static class ProcessCreateParser
{
    // Sysmon Event ID 1
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
                Timestamp      = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventType      = "Create",
                EventSource    = "Sysmon",
                Pid            = int.TryParse(data.GetValueOrDefault("ProcessId"), out var pid) ? pid : 0,
                ParentPid      = int.TryParse(data.GetValueOrDefault("ParentProcessId"), out var ppid) ? ppid : null,
                ProcessName    = Path.GetFileName(data.GetValueOrDefault("Image") ?? string.Empty),
                CommandLine    = data.GetValueOrDefault("CommandLine"),
                UserName       = data.GetValueOrDefault("User"),
                IntegrityLevel = data.GetValueOrDefault("IntegrityLevel"),
                IsElevated     = string.Equals(data.GetValueOrDefault("IntegrityLevel"), "High", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(data.GetValueOrDefault("IntegrityLevel"), "System", StringComparison.OrdinalIgnoreCase),
                Sha256         = data.GetValueOrDefault("Hashes")
                                    ?.Split(',')
                                    .FirstOrDefault(h => h.StartsWith("SHA256="))
                                    ?.Substring(7),
            };
        }
        catch { return null; }
    }

    // Security Event ID 4688
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

            var tokenElevationType = data.GetValueOrDefault("TokenElevationType") ?? string.Empty;

            return new ProcessEvent
            {
                Timestamp   = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventType   = "Create",
                EventSource = "Security",
                Pid         = Convert.ToInt32(data.GetValueOrDefault("NewProcessId") ?? "0", 16),
                ParentPid   = Convert.ToInt32(data.GetValueOrDefault("ProcessId") ?? "0", 16),
                ProcessName = Path.GetFileName(data.GetValueOrDefault("NewProcessName") ?? string.Empty),
                CommandLine = data.GetValueOrDefault("CommandLine"),
                UserName    = $"{data.GetValueOrDefault("SubjectDomainName")}\\{data.GetValueOrDefault("SubjectUserName")}",
                IsElevated  = tokenElevationType == "%%1937",  // TokenElevationTypeFull
            };
        }
        catch { return null; }
    }
}
