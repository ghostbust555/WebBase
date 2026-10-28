using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebBase.Api.Contracts;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Controllers;

/// <summary>
/// What the browser needs to know about this deployment before anything else:
/// which environment it is (dev shows a banner), which build, and which ways of
/// signing in are switched on. Public - nothing here is a secret.
/// </summary>
[ApiController]
[Route("api/config")]
public class ConfigController(
    IOptions<AppOptions> app, IOptionsMonitor<GoogleAuthOptions> google, IOptions<CmsOptions> cms) : ControllerBase
{
    [HttpGet]
    public SiteConfig Get() => new(
        Name: app.Value.Name,
        Environment: app.Value.IsDevelopment ? "development" : "prod",
        Build: app.Value.Build,
        GoogleClientId: google.CurrentValue.Enabled ? google.CurrentValue.ClientId : null,
        Cms: cms.Value.Enabled,
        BuiltAtUtc: DateTime.TryParse(app.Value.BuiltAtUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var built) ? built : null);
}
