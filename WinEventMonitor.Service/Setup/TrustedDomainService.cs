using System.Text.Json;

namespace WinEventMonitor.Service.Setup;

/// <summary>
/// Gestiona la lista de dominios DNS de confianza que se excluyen de la vista.
/// Se persiste en C:\ProgramData\WinEventMonitor\trusted-domains.json.
/// </summary>
public class TrustedDomainService
{
    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WinEventMonitor", "trusted-domains.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static readonly List<string> DefaultDomains =
    [
        "microsoft.com", "windows.com", "windowsupdate.com", "windowsazure.com",
        "live.com", "microsoftonline.com", "office.com", "office365.com",
        "google.com", "googleapis.com", "gstatic.com", "googlevideo.com",
        "apple.com", "icloud.com", "mzstatic.com",
        "cloudflare.com", "cloudflare-dns.com",
        "akamaiedge.net", "akamaized.net",
        "amazon.com", "amazonaws.com",
        "github.com", "githubusercontent.com",
    ];

    private readonly object _lock = new();

    public List<string> GetDomains()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath))
                return [.. DefaultDomains];
            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<string>>(json) ?? [.. DefaultDomains];
            }
            catch { return [.. DefaultDomains]; }
        }
    }

    public List<string> AddDomain(string domain)
    {
        lock (_lock)
        {
            var clean = Normalize(domain);
            if (string.IsNullOrEmpty(clean)) return GetDomains();
            var domains = GetDomains();
            if (!domains.Contains(clean, StringComparer.OrdinalIgnoreCase))
                domains.Add(clean);
            Save(domains);
            return domains;
        }
    }

    public List<string> RemoveDomain(string domain)
    {
        lock (_lock)
        {
            var clean = Normalize(domain);
            var domains = GetDomains();
            domains.RemoveAll(d => d.Equals(clean, StringComparison.OrdinalIgnoreCase));
            Save(domains);
            return domains;
        }
    }

    private static string Normalize(string domain) =>
        domain.ToLowerInvariant().Trim().TrimStart('.');

    private static void Save(List<string> domains)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(domains, JsonOpts));
    }
}
