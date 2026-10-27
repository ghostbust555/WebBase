namespace WebBase.Api.Contracts;

// The authors' pages (docs/PAGES.md). What visitors get is deliberately small.

/// <summary>GET /api/pages/published?path=...: a published page, for visitors.</summary>
public record PublicPageDto(string Path, string Title, string Markdown, DateTime PublishedAtUtc, DateTime UpdatedAtUtc);

/// <summary>GET /api/pages/menu: what the header lists, in order.</summary>
public record MenuItemDto(string Path, string Title, int Order);

/// <summary>
/// Create or save a page (the whole page, every time). An empty Path is made from
/// the title. Published = true publishes (a page that already is keeps its original
/// time); false makes it a draft.
/// </summary>
public record SavePageRequest(string Title, string? Path, string Body, bool ShowInMenu, int Order, bool Published);

/// <summary>The editor's whole page.</summary>
public record PageDto(
    Guid Id, string Path, string Title, string Body, bool ShowInMenu, int Order, bool Published,
    DateTime? PublishedAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid UpdatedById)
{
    public static PageDto From(Page p) => new(p.Id, p.Path, p.Title, p.Body, p.ShowInMenu, p.Order, p.IsPublished,
        p.PublishedAtUtc, p.CreatedAtUtc, p.UpdatedAtUtc, p.UpdatedById);
}

/// <summary>A row of the editor's list: no body.</summary>
public record PageSummaryDto(Guid Id, string Path, string Title, bool ShowInMenu, int Order, bool Published, DateTime UpdatedAtUtc)
{
    public static PageSummaryDto From(Page p) => new(p.Id, p.Path, p.Title, p.ShowInMenu, p.Order, p.IsPublished, p.UpdatedAtUtc);
}
