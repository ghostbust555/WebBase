namespace WebBase.Api.Infrastructure;

/// <summary>Facts about this deployment. Bound from the "App" section.</summary>
public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Shown in the page title and the API docs.</summary>
    public string Name { get; set; } = "WebBase";

    /// <summary>
    /// "development" or "prod". Configuration rather than a compile-time
    /// constant, because prod runs the very same image dev was built as.
    /// Empty is treated as prod: the safe assumption for a deployment nobody
    /// told.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Which build this is. Baked into the image by deploy.sh.</summary>
    public string Build { get; set; } = string.Empty;

    public bool IsDevelopment =>
        Environment.Trim().Equals("development", StringComparison.OrdinalIgnoreCase);
}

/// <summary>How sign-in tokens are signed. Bound from the "Jwt" section.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The signing secret. Anyone who has it can sign in as anyone, so it lives
    /// in .env.local (Jwt__Key=...) and never in git. setup.sh generates one.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "WebBase";
    public string Audience { get; set; } = "WebBase";

    /// <summary>
    /// How long a sign-in lasts. The client renews its token once it is past
    /// half its life, so anyone who uses the site at least that often stays
    /// signed in.
    /// </summary>
    public int LifetimeDays { get; set; } = 30;

    /// <summary>Used only in development when no key is set, so a fresh clone runs.</summary>
    public const string DevelopmentKey = "development-only-key-never-use-this-in-prod-0123456789abcdef";

    public const int MinimumKeyLength = 32;
}

/// <summary>
/// The optional file-based CMS (Services/ContentService.cs). Bound from "Cms".
/// </summary>
public class CmsOptions
{
    public const string SectionName = "Cms";

    /// <summary>Off by default: no content endpoints, no /content files, no CMS pages.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The content folder. Relative paths are from the API project folder, so
    /// the default finds content/ at the repository root on a plain `dotnet
    /// run`. Docker sets an absolute path (compose.dev.yaml, ops/Dockerfile).
    /// </summary>
    public string ContentPath { get; set; } = "../../content";
}

/// <summary>
/// "Sign in with Google". Only the client id, which is public by design: the
/// ID-token flow used here needs no client secret. Empty means off - no button,
/// no Google script, and the endpoint answers 404.
/// </summary>
public class GoogleAuthOptions
{
    public const string SectionName = "Auth:Google";

    public string? ClientId { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId);
}
