using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using WinEventMonitor.Service.Models;

namespace WinEventMonitor.Service.Parsers;

/// <summary>Parser para Sysmon ID 7 – Image Load.</summary>
public static class ImageLoadParser
{
    private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static SysmonAdvancedEvent? FromSysmon(EventRecord e)
    {
        try
        {
            var data = ParseEventData(e);
            return new SysmonAdvancedEvent
            {
                Timestamp         = e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                EventId           = 7,
                SourcePid         = int.TryParse(data.GetValueOrDefault("ProcessId"), out var pid) ? pid : 0,
                SourceProcessName = Path.GetFileName(data.GetValueOrDefault("Image") ?? string.Empty),
                ImagePath         = data.GetValueOrDefault("ImageLoaded"),
                Signed            = data.TryGetValue("Signed", out var s)
                                        ? s.Equals("true", StringComparison.OrdinalIgnoreCase)
                                        : null,
                Signature         = data.GetValueOrDefault("Signature"),
                SignatureStatus   = data.GetValueOrDefault("SignatureStatus"),
                Sha256            = ParseSha256(data.GetValueOrDefault("Hashes")),
            };
        }
        catch { return null; }
    }

    /// <summary>Convierte el XML del evento en un diccionario Data[Name] → Value.</summary>
    internal static Dictionary<string, string> ParseEventData(EventRecord e)
    {
        var doc = XDocument.Parse(e.ToXml());
        return doc.Descendants(XName.Get("Data", Ns))
                  .ToDictionary(
                      d => d.Attribute("Name")?.Value ?? string.Empty,
                      d => d.Value,
                      StringComparer.OrdinalIgnoreCase);
    }

    internal static string? ParseSha256(string? hashes) =>
        hashes?.Split(',')
               .FirstOrDefault(h => h.StartsWith("SHA256=", StringComparison.OrdinalIgnoreCase))
               ?.Substring(7);
}
