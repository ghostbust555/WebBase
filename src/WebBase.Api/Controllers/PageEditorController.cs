using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebBase.Api.Content;
using WebBase.Api.Contracts;
using WebBase.Api.Infrastructure;
using WebBase.Api.Pages;

namespace WebBase.Api.Controllers;

/// <summary>
/// Writing the authors' pages (docs/PAGES.md): list (drafts too), read, create,
/// save, delete. Authors only. The pages belong to the site, so nothing here
/// filters by user: [RequireAuthor] is the whole guard.
/// </summary>
[ApiController]
[Route("api/editor/pages")]
[Authorize, RequireAuthor]
public class PageEditorController(IPageStore pages, ContentService files) : ControllerBase
{
    [HttpGet]
    public async Task<List<PageSummaryDto>> List(CancellationToken ct) =>
        (await pages.ListAsync(ct)).OrderByDescending(p => p.UpdatedAtUtc).Select(PageSummaryDto.From).ToList();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PageDto>> Get(Guid id, CancellationToken ct) =>
        await pages.FindByIdAsync(id, ct) is { } page ? PageDto.From(page) : NotFound(new ErrorResponse("No such page."));

    [HttpPost]
    public async Task<ActionResult<PageDto>> Create(SavePageRequest request, CancellationToken ct)
    {
        var problem = Check(request, out var title, out var path);
        if (problem != null) return problem;
        if (await FileUsesAsync(path!, ct) is { } shadowed) return shadowed;

        var now = DateTime.UtcNow;
        var page = new Page { CreatedAtUtc = now };
        Fill(page, request, title!, path!, now, User.GetUserId());
        try
        {
            await pages.AddAsync(page, ct);
        }
        catch (DuplicateKeyException)
        {
            return Taken();
        }
        return CreatedAtAction(nameof(Get), new { id = page.Id }, PageDto.From(page));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PageDto>> Save(Guid id, SavePageRequest request, CancellationToken ct)
    {
        var page = await pages.FindByIdAsync(id, ct);
        if (page == null) return NotFound(new ErrorResponse("No such page."));
        var problem = Check(request, out var title, out var path);
        if (problem != null) return problem;
        // Only a new address has to be free of files: a page whose file twin appeared later must stay editable.
        if (path != page.Path && await FileUsesAsync(path!, ct) is { } shadowed) return shadowed;

        Fill(page, request, title!, path!, DateTime.UtcNow, User.GetUserId());
        try
        {
            await pages.UpdateAsync(page, ct);
        }
        catch (DuplicateKeyException)
        {
            return Taken();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ErrorResponse("That page was deleted while you were editing it.")); // someone else removed it
        }
        return PageDto.From(page);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await pages.RemoveAsync(id, ct) ? NoContent() : NotFound(new ErrorResponse("No such page."));

    // ---- Rules -------------------------------------------------------------

    /// <summary>Null if the request is fine (with the cleaned title and path), else the 400 to send.</summary>
    private ActionResult? Check(SavePageRequest request, out string? title, out string? path)
    {
        title = path = null;
        var t = (request.Title ?? "").Trim();
        if (t.Length == 0) return Bad("Give the page a title.");
        if (t.Length > PageRules.MaxTitle) return Bad($"Titles can be at most {PageRules.MaxTitle} characters.");
        if (PageRules.HasControlCharacters(t)) return Bad("The title has characters that can't be saved.");

        var body = request.Body ?? "";
        if (body.Length > PageRules.MaxBody) return Bad($"Pages can be at most {PageRules.MaxBody:N0} characters ({body.Length:N0} now).");
        if (PageRules.HasControlCharacters(body)) return Bad("The text has control characters that can't be saved.");
        if (Math.Abs(request.Order) > PageRules.MaxOrder) return Bad($"The menu position must be between -{PageRules.MaxOrder} and {PageRules.MaxOrder}.");

        var wanted = string.IsNullOrWhiteSpace(request.Path) ? PageRules.SlugFromTitle(t) : request.Path;
        if (string.IsNullOrWhiteSpace(wanted))
            return Bad("Couldn't make an address from that title. Type one: lower-case letters, digits, - and _.");
        path = PageRules.CleanPath(wanted, out var problem);
        if (path == null) return Bad(problem!);
        title = t;
        return null;
    }

    private static void Fill(Page page, SavePageRequest request, string title, string path, DateTime now, Guid by)
    {
        page.UpdatedById = by;
        page.Title = title;
        page.Path = path;
        page.Body = (request.Body ?? "").Replace("\r\n", "\n");
        page.ShowInMenu = request.ShowInMenu;
        page.Order = request.Order;
        page.PublishedAtUtc = request.Published ? page.PublishedAtUtc ?? now : null;
        page.UpdatedAtUtc = now;
    }

    /// <summary>
    /// A file in content/ (the CMS) at this address wins over a database page, so
    /// refuse to make one that would be hidden behind it. Null if the address is free.
    /// </summary>
    private async Task<ActionResult?> FileUsesAsync(string path, CancellationToken ct)
    {
        if (!files.Enabled) return null;
        try
        {
            if (await files.GetPageAsync(path, ct) == null) return null;
        }
        catch (FileStoreUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("The site's content files can't be reached right now, so the address can't be checked. Try again in a minute."));
        }
        return Conflict(new ErrorResponse($"A file in the site's content folder already serves /{path}, and files win. Choose another address."));
    }

    private ConflictObjectResult Taken() => Conflict(new ErrorResponse("Another page already uses that address. Choose a different one."));

    private BadRequestObjectResult Bad(string message) => BadRequest(new ErrorResponse(message));
}
