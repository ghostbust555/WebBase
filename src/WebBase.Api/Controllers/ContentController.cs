using Microsoft.AspNetCore.Mvc;
using WebBase.Api.Contracts;
using WebBase.Api.Services;

namespace WebBase.Api.Controllers;

/// <summary>
/// The optional CMS (see Services/ContentService.cs and docs/CMS.md). Every
/// endpoint answers 404 while Cms:Enabled is false.
/// </summary>
[ApiController]
[Route("api/content")]
public class ContentController(ContentService content) : ControllerBase
{
    /// <summary>Every page: for menus and site maps.</summary>
    [HttpGet("pages")]
    public ActionResult<List<ContentSummary>> Pages() =>
        content.Enabled ? content.AllPages() : NotFound(new ErrorResponse("The CMS is switched off."));

    /// <summary>One page by address: /api/content/page?path=blog/first-post (empty for the home page).</summary>
    [HttpGet("page")]
    public ActionResult<ContentPage> Page([FromQuery] string? path)
    {
        if (!content.Enabled) return NotFound(new ErrorResponse("The CMS is switched off."));
        return content.GetPage(path) is { } page ? page : NotFound(new ErrorResponse("No such page."));
    }
}
