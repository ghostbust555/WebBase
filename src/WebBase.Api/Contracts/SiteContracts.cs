namespace WebBase.Api.Contracts;

/// <summary>GET /api/config: what the browser needs to know before anything else. Public - nothing secret.</summary>
public record SiteConfig(
    string Name,
    string Environment,      // "development" (shows the banner) or "prod"
    string Build,
    string? GoogleClientId,  // set when "Sign in with Google" is on
    bool Cms,                // whether the CMS is switched on (CMS_ENABLED)
    DateTime? BuiltAtUtc);   // when this build was made; null if unknown (development)

/// <summary>GET /api/health.</summary>
public record HealthResponse(string Status, bool Database);
