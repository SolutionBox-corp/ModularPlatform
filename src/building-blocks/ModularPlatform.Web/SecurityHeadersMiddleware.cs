using Microsoft.AspNetCore.Http;
using ModularPlatform.Web.Errors;

namespace ModularPlatform.Web;

/// <summary>Baseline security headers for every API response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironmentAccessor env)
{
    public async Task Invoke(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        // HSTS only outside Development (a max-age on http://localhost would pin the dev box to HTTPS and break it).
        // Honoured by browsers only over HTTPS; harmless on native API clients.
        if (!env.IsDevelopment)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        await next(context);
    }
}
