using Microsoft.EntityFrameworkCore;

namespace WebBase.Database.Postgres;

/// <summary>IPageStore on PostgreSQL. A unique index on the path is the uniqueness rule (AppDbContext).</summary>
public class PostgresPageStore(AppDbContext db) : IPageStore
{
    public async Task<IReadOnlyList<Page>> ListAsync(CancellationToken ct = default) =>
        await db.Pages.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Page>> ListPublishedAsync(CancellationToken ct = default) =>
        await db.Pages.AsNoTracking().Where(p => p.PublishedAtUtc != null).ToListAsync(ct);

    public Task<Page?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Page?> FindByPathAsync(string path, CancellationToken ct = default) =>
        db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Path == path, ct);

    public async Task AddAsync(Page page, CancellationToken ct = default)
    {
        db.Pages.Add(page);
        await db.SaveAsync(ct);
    }

    public async Task UpdateAsync(Page page, CancellationToken ct = default)
    {
        db.Pages.Update(page);
        await db.SaveAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page == null) return false;
        db.Pages.Remove(page);
        await db.SaveAsync(ct);
        return true;
    }
}
