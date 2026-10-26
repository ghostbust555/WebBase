using Microsoft.EntityFrameworkCore;

namespace WebBase.Database.Postgres;

/// <summary>INoteStore on PostgreSQL.</summary>
public class PostgresNoteStore(AppDbContext db) : INoteStore
{
    public async Task<IReadOnlyList<Note>> ListAsync(Guid userId, CancellationToken ct = default) =>
        await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(Note note, CancellationToken ct = default)
    {
        db.Notes.Add(note);
        await db.SaveAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        // Find-then-remove rather than ExecuteDelete, which the tests'
        // in-memory database doesn't support.
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, ct);
        if (note == null) return false;
        db.Notes.Remove(note);
        await db.SaveAsync(ct);
        return true;
    }
}
