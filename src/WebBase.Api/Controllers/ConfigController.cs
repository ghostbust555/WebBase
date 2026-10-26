using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
    public object Get() => new
    {
        name = app.Value.Name,
        environment = app.Value.IsDevelopment ? "development" : "prod",
        build = app.Value.Build,
        googleClientId = google.CurrentValue.Enabled ? google.CurrentValue.ClientId : null,
        cms = cms.Value.Enabled,
    };
}
