namespace WebBase.Api.Infrastructure;

/// <summary>
/// Headers on every response that tell the browser to be careful with this
/// site: never guess a file's type, never show it inside another site's frame,
/// and don't leak full URLs to other sites.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            await next();
        });
}
