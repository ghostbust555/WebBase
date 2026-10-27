namespace WebBase.Models;

/// <summary>
/// A page of the site that authors write in the browser (docs/PAGES.md): the
/// database-backed sibling of the markdown files in content/ (docs/CMS.md).
///
/// Site-owned content, so there is no owner id: it belongs to the site, and
/// the author check on the endpoints (RequireAuthor) is what guards it.
/// <see cref="UpdatedById"/> only records who touched it last.
/// </summary>
public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The address, without a leading slash: "about", "blog/hello". Lower-case
    /// words, digits, - and _ between slashes. Unique.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>The text, as markdown. Shown through the client's markdown renderer, which sanitises it.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>List the page in the site's header (once published).</summary>
    public bool ShowInMenu { get; set; }

    /// <summary>Position in the menu: lower first.</summary>
    public int Order { get; set; }

    /// <summary>When it was published. Null = a draft that only authors can see.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The author who saved it last.</summary>
    public Guid UpdatedById { get; set; }

    public bool IsPublished => PublishedAtUtc != null;
}
