using WebBase.Models;

namespace WebBase.Data;

/// <summary>
/// Where the authors' pages are kept (docs/PAGES.md).
///
/// This is the exception to the "every method takes the owner's id" rule
/// (docs/AUTHORS.md): these pages belong to the site, not to a user, so
/// there is no owner to pass. Whoever may reach this store is decided by the
/// caller - the endpoints that write are behind [RequireAuthor].
///
/// The contract (tests/WebBase.Testing/StoreContractTests.cs):
/// - Reads return copies.
/// - Paths are compared exactly; callers pass them already cleaned (PagePaths).
/// - No two pages share a path. Breaking that throws <see cref="DuplicateKeyException"/>,
///   even when two requests race.
/// - No order is promised for lists: the caller sorts what it needs.
/// - Times come back in UTC, to the millisecond at least.
/// </summary>
public interface IPageStore
{
    /// <summary>Every page, drafts included.</summary>
    Task<IReadOnlyList<Page>> ListAsync(CancellationToken ct = default);

    /// <summary>Only the published pages: what visitors can see.</summary>
    Task<IReadOnlyList<Page>> ListPublishedAsync(CancellationToken ct = default);

    Task<Page?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The page at an address, draft or published.</summary>
    Task<Page?> FindByPathAsync(string path, CancellationToken ct = default);

    /// <summary>Saves a new page. Throws <see cref="DuplicateKeyException"/> if the path is taken.</summary>
    Task AddAsync(Page page, CancellationToken ct = default);

    /// <summary>
    /// Saves every field of an existing page. Throws <see cref="DuplicateKeyException"/>
    /// if the new path belongs to another page, and <see cref="KeyNotFoundException"/>
    /// if the page doesn't exist.
    /// </summary>
    Task UpdateAsync(Page page, CancellationToken ct = default);

    /// <summary>False if there was no such page.</summary>
    Task<bool> RemoveAsync(Guid id, CancellationToken ct = default);
}
