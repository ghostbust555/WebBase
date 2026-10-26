namespace WebBase.Api.Data;

/// <summary>
/// The database, as C# sees it. Each DbSet is a table. Change a model or add a
/// DbSet here, then make a migration (./scripts/ef.sh migrations add Name) - the
/// app applies it by itself the next time it starts.
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
            note.HasOne(n => n.User).WithMany(u => u.Notes)
                .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
