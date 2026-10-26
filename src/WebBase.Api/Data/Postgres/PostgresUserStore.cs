using Microsoft.EntityFrameworkCore;

namespace WebBase.Api.Data.Postgres;

/// <summary>IUserStore on PostgreSQL. Uniqueness is the database's unique indexes (AppDbContext).</summary>
public class PostgresUserStore(AppDbContext db) : IUserStore
{
    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> FindByGoogleSubjectAsync(string subject, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.GoogleSubject == subject, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        db.Users.Update(user);
        await db.SaveAsync(ct);
    }
}
