using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WebBase.Api.Infrastructure;

/// <summary>
/// Slows down password guessing: each visitor gets a handful of sign-in
/// attempts per minute. Put [EnableRateLimiting(RateLimits.Auth)] on anything
/// else worth protecting the same way.
/// </summary>
public static class RateLimits
{
    public const string Auth = "auth";

    public static IServiceCollection AddAppRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = config.GetValue("RateLimits:AuthPerMinute", 10);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Too many attempts. Wait a minute and try again.\"}", ct);
            };
        });
    }

    /// <summary>
    /// The visitor's real address. Normally UseForwardedHeaders has already put
    /// it in RemoteIpAddress (cloud load balancers add it themselves). Behind a
    /// Cloudflare tunnel every request arrives from the tunnel, so
    /// RateLimits:ClientIpHeader names the header that says who it was
    /// (CF-Connecting-IP). It is empty by default: a header anyone can send
    /// would let a flood pick its own address.
    /// </summary>
    public static string ClientIp(HttpContext context)
    {
        var header = context.RequestServices.GetService<IConfiguration>()?["RateLimits:ClientIpHeader"];
        if (!string.IsNullOrWhiteSpace(header) && context.Request.Headers[header].FirstOrDefault() is { Length: > 0 } fromHeader)
            return fromHeader;
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
