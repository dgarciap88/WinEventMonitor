using WinEventMonitor.Service.Services;

namespace WinEventMonitor.Service.Api;

public static class SystemRoutes
{
    public static void MapSystemRoutes(this WebApplication app)
    {
        app.MapGet("/api/system/health", (SystemHealthService sysHealth) =>
            Results.Ok(sysHealth.GetLatest()));

        app.MapGet("/api/system/history", (SystemHealthService sysHealth) =>
            Results.Ok(sysHealth.GetHistory()));
    }
}
