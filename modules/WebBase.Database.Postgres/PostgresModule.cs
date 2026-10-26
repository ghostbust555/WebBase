using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WebBase.Modules;

namespace WebBase.Database.Postgres;

/// <summary>
/// PostgreSQL through Entity Framework Core: Database:Provider "postgres", the
/// app's default. Settings:
/// - DATABASE_URL, postgres://user:password@host:5432/db (as cloud hosts give
///   it), or ConnectionStrings:Default, a .NET connection string (as the
///   compose files give it). DATABASE_URL wins.
/// - ConnectionStrings:Migration, optional: migrations run on this one (the
///   owner role) instead.
/// </summary>
public class PostgresModule : IDatabaseModule
{
    public string Name => "postgres";

    public static string? ConnectionString(IConfiguration config) =>
        DatabaseUrl.ToConnectionString(config["DATABASE_URL"]) ?? config.GetConnectionString("Default");

    public void Register(IServiceCollection services, IConfiguration config)
    {
        var connectionString = ConnectionString(config);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDatabase, PostgresDatabase>();
        services.AddScoped<IUserStore, PostgresUserStore>();
        services.AddScoped<INoteStore, PostgresNoteStore>();
    }
}

/// <summary>
/// For ./scripts/ef.sh (dotnet ef): how to make an AppDbContext without
/// starting the website. Migrations are made against the development database
/// settings; the app applies them itself when it starts.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString = PostgresModule.ConnectionString(config)
            ?? "Host=localhost;Port=5432;Database=webbase;Username=webbase;Password=webbase";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }
}
