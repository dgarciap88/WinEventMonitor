using WinEventMonitor.Service.Security;

namespace WinEventMonitor.Service.Security;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyService _apiKeyService;

    public ApiKeyMiddleware(RequestDelegate next, ApiKeyService apiKeyService)
    {
        _next = next;
        _apiKeyService = apiKeyService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (!_apiKeyService.Validate(provided))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
        await _next(context);
    }
}
