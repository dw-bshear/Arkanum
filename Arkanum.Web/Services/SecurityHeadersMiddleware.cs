using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Arkanum.Web.Services;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        Apply(context);
        await next(context);
    }

    public static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'";

        if (IsSensitivePath(context.Request.Path) || context.User?.Identity?.IsAuthenticated == true)
        {
            headers["Cache-Control"] = "no-store, no-cache, max-age=0";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }
    }

    public static bool IsSensitivePath(PathString path)
    {
        return path.StartsWithSegments("/Vaults", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Settings", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Users", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Audit", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Setup", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Logout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase);
    }
}

public static class SecurityHeadersApplicationBuilderExtensions
{
    public static IApplicationBuilder UseArkanumSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
