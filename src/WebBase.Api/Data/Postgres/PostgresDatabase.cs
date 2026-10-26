using Microsoft.EntityFrameworkCore;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Data.Postgres;

/// <summary>Migrations at startup, and the health check's "can I reach it?".</summary>
public class PostgresDatabase(AppDbContext db, IConfiguration config) : IDatabase
{
    public string Provider => "postgres";

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        // The test suite's in-memory stand-in has no migrations.
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

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

public static class PostgresRegistration
{
    /// <summary>
    /// DATABASE_URL (as cloud hosts provide it) wins over
    /// ConnectionStrings:Default (as docker compose provides it).
    /// </summary>
    public static string? ConnectionString(IConfiguration config) =>
        DatabaseUrl.ToConnectionString(config["DATABASE_URL"]) ?? config.GetConnectionString("Default");

    public static IServiceCollection AddPostgresDatabase(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = ConnectionString(config);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDatabase, PostgresDatabase>();
        services.AddScoped<IUserStore, PostgresUserStore>();
        services.AddScoped<INoteStore, PostgresNoteStore>();
        return services;
    }
}
