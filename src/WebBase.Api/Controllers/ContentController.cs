using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using WebBase.Api.Content;
using WebBase.Api.Contracts;

namespace WebBase.Api.Controllers;

/// <summary>
/// The optional CMS (see Content/ContentService.cs and docs/CMS.md). Every
/// endpoint answers 404 while Cms:Enabled is false, except theme.css, which is
/// then simply empty.
/// </summary>
[ApiController]
[Route("api/content")]
[ContentUnavailable]
public class ContentController(ContentService content) : ControllerBase
{
    private static readonly ErrorResponse Off = new("The CMS is switched off.");

    /// <summary>Every page: for menus and site maps.</summary>
    [HttpGet("pages")]
    public async Task<ActionResult<List<ContentSummary>>> Pages(CancellationToken ct) =>
        content.Enabled ? await content.AllPagesAsync(ct) : NotFound(Off);

    /// <summary>One page by address: /api/content/page?path=blog/first-post (empty for the home page).</summary>
    [HttpGet("page")]
    public async Task<ActionResult<ContentPage>> Page([FromQuery] string? path, CancellationToken ct)
    {
        if (!content.Enabled) return NotFound(Off);
        return await content.GetPageAsync(path, ct) is { } page ? page : NotFound(new ErrorResponse("No such page."));
    }

    /// <summary>
    /// The CMS's colours: the variables from theme.css (Content/ThemeCss.cs).
    /// The website always links it, so it's never a 404, only sometimes empty.
    /// </summary>
    [HttpGet("theme.css")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Theme(CancellationToken ct)
    {
        string css;
        try { css = await content.ThemeAsync(ct); }
        catch (FileStoreUnavailableException) { css = ""; } // the site's own colours, rather than a broken page

        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(css)))[..16]}\"");
        Response.Headers.CacheControl = "no-cache"; // always ask, so an edit shows on the next load
        if (Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false)))
            return StatusCode(StatusCodes.Status304NotModified);
        Response.GetTypedHeaders().ETag = etag;
        return Content(css, "text/css; charset=utf-8");
    }

    private static readonly FileExtensionContentTypeProvider Types = new();

    /// <summary>
    /// The CMS's other files, as they are: images/cat.jpg in the content store
    /// is /content/images/cat.jpg. Never markdown (drafts live there), nor
    /// anything whose name starts with . or _.
    /// </summary>
    [HttpGet("/content/{**path}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> File(string path, CancellationToken ct)
    {
        if (!content.Enabled) return NotFound();
        var file = await content.FindFileAsync(path, ct);
        if (file == null || !Types.TryGetContentType(path, out var type)) return NotFound();

        var etag = new EntityTagHeaderValue($"\"{file.LastModified.UtcTicks:x}-{file.Size:x}\"");
        if (Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false)))
            return StatusCode(StatusCodes.Status304NotModified);

        var stream = await content.OpenAsync(file, ct);
        if (stream == null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=300";
        return File(stream, type, file.LastModified, etag);
    }
}

/// <summary>The content store can't be reached: a 503 with a readable message, not a 500.</summary>
public class ContentUnavailableAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not FileStoreUnavailableException) return;
        context.HttpContext.RequestServices.GetRequiredService<ILogger<ContentController>>()
            .LogError(context.Exception, "CMS content unavailable");
        context.Result = new ObjectResult(new ErrorResponse("The site's content can't be reached right now. Try again in a minute."))
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
