using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class AlertRulesRoutes
{
    public static void MapAlertRulesRoutes(this WebApplication app)
    {
        // GET /api/alert-rules — devuelve todas las reglas con su estado
        app.MapGet("/api/alert-rules", (AlertRulesService rulesService) =>
            Results.Ok(rulesService.GetAll()));

        // PATCH /api/alert-rules/{id} — actualiza enabled y/o severity de una regla
        app.MapPatch("/api/alert-rules/{id:int}", (
            int id,
            AlertRulePatch body,
            AlertRulesService rulesService) =>
        {
            if (body.Severity is not null &&
                body.Severity is not ("High" or "Medium" or "Low"))
                return Results.BadRequest(new { detail = "Severity debe ser High, Medium o Low." });

            if (!rulesService.TryUpdate(id, body.Enabled, body.Severity, out var updated))
                return Results.NotFound(new { detail = $"Regla {id} no existe." });

            return Results.Ok(updated);
        });
    }

    private sealed record AlertRulePatch(bool Enabled, string? Severity);
}
