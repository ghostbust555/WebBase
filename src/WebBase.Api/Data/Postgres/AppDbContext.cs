using Microsoft.EntityFrameworkCore;

namespace WebBase.Api.Data.Postgres;

/// <summary>
/// The PostgreSQL database, as Entity Framework sees it. Each DbSet is a table.
/// Only the stores in this folder use it; the rest of the app talks to
/// IUserStore / INoteStore. Change a model or add a DbSet here, then make a
/// migration (./scripts/ef.sh migrations add Name) - the app applies it by
/// itself the next time it starts.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
            user.HasIndex(u => u.GoogleSubject).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.DisplayName).HasMaxLength(80);
        });

        model.Entity<Note>(note =>
        {
            note.HasIndex(n => new { n.UserId, n.CreatedAtUtc });
            note.Property(n => n.Text).HasMaxLength(2000);
            // Deleting a user deletes their notes. The models carry only the
            // id (no navigation properties), so they stay plain classes.
            note.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// SaveChanges, with Postgres's errors turned into the stores' contract
    /// (DuplicateKeyException, KeyNotFoundException). Afterwards nothing is
    /// tracked, so an object a caller holds is never saved behind its back.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new KeyNotFoundException("That record doesn't exist (any more).", e);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new DuplicateKeyException($"That would duplicate a unique value ({pg.ConstraintName}).", e);
        }
        finally
        {
            ChangeTracker.Clear();
        }
    }
}
