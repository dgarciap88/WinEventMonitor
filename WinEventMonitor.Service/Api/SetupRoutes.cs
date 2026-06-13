using WinEventMonitor.Service.Setup;

namespace WinEventMonitor.Service.Api;

public static class SetupRoutes
{
    public static void MapSetupRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/setup");

        group.MapGet("/status", (SysmonSetupService svc) =>
            Results.Ok(svc.GetStatus()));

        group.MapPost("/sysmon", (SysmonSetupService svc) =>
        {
            var (success, message) = svc.ApplySysmonConfig();
            return success
                ? Results.Ok(new { message })
                : Results.Problem(detail: message, statusCode: 500);
        });

        group.MapPost("/audit", (SysmonSetupService svc) =>
        {
            var (success, message) = svc.EnableAuditPolicy();
            return success
                ? Results.Ok(new { message })
                : Results.Problem(detail: message, statusCode: 500);
        });

        group.MapPost("/audit-logon", (SysmonSetupService svc) =>
        {
            var (success, message) = svc.EnableLogonAuditPolicy();
            return success
                ? Results.Ok(new { message })
                : Results.Problem(detail: message, statusCode: 500);
        });

        // ─── Dominios DNS de confianza ───────────────────────────────────────

        // GET /api/setup/trusted-domains
        group.MapGet("/trusted-domains", (TrustedDomainService svc) =>
            Results.Ok(svc.GetDomains()));

        // POST /api/setup/trusted-domains/add  { "domain": "example.com" }
        group.MapPost("/trusted-domains/add", (TrustedDomainService svc, DomainDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Domain))
                return Results.BadRequest(new { error = "Dominio vacío" });
            return Results.Ok(svc.AddDomain(dto.Domain));
        });

        // POST /api/setup/trusted-domains/remove  { "domain": "example.com" }
        group.MapPost("/trusted-domains/remove", (TrustedDomainService svc, DomainDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Domain))
                return Results.BadRequest(new { error = "Dominio vacío" });
            return Results.Ok(svc.RemoveDomain(dto.Domain));
        });
    }
}

public record DomainDto(string Domain);
