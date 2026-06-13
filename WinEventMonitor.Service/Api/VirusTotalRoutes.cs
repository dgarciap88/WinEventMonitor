using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinEventMonitor.Service.Api;

public static partial class VirusTotalRoutes
{
    [GeneratedRegex(@"^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Regex();

    public static void MapVirusTotalRoutes(this WebApplication app)
    {
        // GET /api/virustotal/{sha256}
        // Consulta VirusTotal por hash SHA256.
        // Requiere "EventMonitor:VirusTotalApiKey" en appsettings (gratuito en virustotal.com).
        app.MapGet("/api/virustotal/{sha256}", async (
            string sha256,
            IConfiguration config,
            IHttpClientFactory httpClientFactory) =>
        {
            if (!Sha256Regex().IsMatch(sha256))
                return Results.BadRequest(new { error = "SHA256 inválido" });

            var apiKey = config["EventMonitor:VirusTotalApiKey"] ?? string.Empty;
            if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { available = false, message = "API key de VirusTotal no configurada en appsettings.json" });

            try
            {
                var http = httpClientFactory.CreateClient();
                http.DefaultRequestHeaders.Add("x-apikey", apiKey);

                var resp = await http.GetAsync($"https://www.virustotal.com/api/v3/files/{sha256}");

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return Results.Ok(new { available = true, found = false, sha256 });

                resp.EnsureSuccessStatusCode();

                var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var attrs = json.RootElement.GetProperty("data").GetProperty("attributes");
                var stats = attrs.GetProperty("last_analysis_stats");

                int malicious  = stats.GetProperty("malicious").GetInt32();
                int suspicious = stats.TryGetProperty("suspicious",  out var sEl) ? sEl.GetInt32() : 0;
                int undetected = stats.TryGetProperty("undetected",  out var uEl) ? uEl.GetInt32() : 0;
                int harmless   = stats.TryGetProperty("harmless",    out var hEl) ? hEl.GetInt32() : 0;
                int total      = malicious + suspicious + undetected + harmless;

                string? name  = attrs.TryGetProperty("meaningful_name", out var nEl) ? nEl.GetString() : null;
                string verdict = malicious > 5 ? "malicious" : malicious > 0 ? "suspicious" : "clean";

                return Results.Ok(new { available = true, found = true, sha256, name, malicious, total, verdict });
            }
            catch (HttpRequestException ex)
            {
                return Results.Ok(new { available = true, found = false, sha256, error = ex.Message });
            }
        });
    }
}
