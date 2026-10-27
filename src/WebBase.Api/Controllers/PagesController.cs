using Microsoft.AspNetCore.Mvc;
using WebBase.Api.Contracts;
using WebBase.Api.Pages;

namespace WebBase.Api.Controllers;

/// <summary>
/// What visitors can read of the authors' pages (docs/PAGES.md): published ones
/// only. A draft answers exactly like a page that doesn't exist.
/// </summary>
[ApiController]
[Route("api/pages")]
public class PagesController(IPageStore pages) : ControllerBase
{
    /// <summary>The published pages that ask to be in the header, in menu order.</summary>
    [HttpGet("menu")]
    public async Task<List<MenuItemDto>> Menu(CancellationToken ct) =>
        (await pages.ListPublishedAsync(ct))
            .Where(p => p.ShowInMenu)
            .OrderBy(p => p.Order).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Select(p => new MenuItemDto(p.Path, p.Title, p.Order))
            .ToList();

    /// <summary>One published page by address: /api/pages/published?path=blog/first-post.</summary>
    [HttpGet("published")]
    public async Task<ActionResult<PublicPageDto>> Published([FromQuery] string? path, CancellationToken ct)
    {
        var clean = (path ?? "").Trim().Trim('/').ToLowerInvariant();
        var page = clean.Length == 0 ? null : await pages.FindByPathAsync(clean, ct);
        return page is { PublishedAtUtc: { } published }
            ? new PublicPageDto(page.Path, page.Title, page.Body, published, page.UpdatedAtUtc)
            : NotFound(new ErrorResponse("No such page."));
    }
}
