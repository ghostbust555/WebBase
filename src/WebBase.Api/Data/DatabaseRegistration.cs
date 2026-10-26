using WebBase.Api.Data.Postgres;

namespace WebBase.Api.Data;

/// <summary>
/// Picks the database implementation named by Database:Provider
/// (Database__Provider=...). Everything else in the app asks for IUserStore,
/// INoteStore and IDatabase and never learns which one it got.
///
/// Adding another (Cosmos DB, SQL Server, DynamoDB...): implement those
/// interfaces in a folder next to Postgres/, add a case below, and run the
/// contract tests against it. docs/DATABASE.md walks through it.
/// </summary>
public static class DatabaseRegistration
{
    public const string DefaultProvider = "postgres";

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Database:Provider"] ?? "").Trim().ToLowerInvariant();
        if (provider.Length == 0) provider = DefaultProvider;

        switch (provider)
        {
            case "postgres":
                services.AddPostgresDatabase(config);
                break;
            default:
                throw new InvalidOperationException(
                    $"Database:Provider is '{provider}', which this app doesn't have. Known: postgres. See docs/DATABASE.md.");
        }
        return services;
    }
}
