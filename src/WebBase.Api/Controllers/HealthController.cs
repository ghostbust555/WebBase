using Microsoft.AspNetCore.Mvc;

namespace WebBase.Api.Controllers;

/// <summary>
/// "Are you alive, and can you reach the database?" Used by deploy.sh after a
/// release, by Docker's healthcheck, and by any cloud host's health probe.
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController(IDatabase db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var database = await db.CanConnectAsync(ct);
        var body = new { status = database ? "ok" : "degraded", database };
        return database ? Ok(body) : StatusCode(503, body);
    }
}
