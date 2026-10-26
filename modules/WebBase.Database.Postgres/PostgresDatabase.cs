using Microsoft.EntityFrameworkCore;

namespace WebBase.Database.Postgres;

/// <summary>Migrations at startup, and the health check's "can I reach it?".</summary>
public class PostgresDatabase(AppDbContext db, IConfiguration config) : IDatabase
{
    public string Provider => "postgres";

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        // In prod on this computer, requests use a role that can't change the
        // schema; migrations run as the owner, on a connection that closes after.
        var migration = config.GetConnectionString("Migration");
        if (string.IsNullOrWhiteSpace(migration))
        {
            await db.Database.MigrateAsync(ct);
            return;
        }
        await using var owner = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(migration).Options);
        await owner.Database.MigrateAsync(ct);
    }

    public Task<bool> CanConnectAsync(CancellationToken ct = default) => db.Database.CanConnectAsync(ct);
}
