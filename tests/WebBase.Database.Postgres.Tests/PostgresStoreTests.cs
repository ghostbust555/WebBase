using Npgsql;
using WebBase.Modules;

namespace WebBase.Database.Postgres.Tests;

/// <summary>
/// The contract against real PostgreSQL. Needs TEST_POSTGRES, a connection
/// string for a server where it may create databases; ./scripts/test.sh starts
/// one and sets it. Without it these tests are skipped, not failed.
/// Each test gets its own empty database, migrated from scratch, so the
/// migrations are tested too.
/// </summary>
public class PostgresStoreTests : StoreContractTests
{
    protected override IDatabaseModule Module => new PostgresModule();

    private static readonly string? Server = Environment.GetEnvironmentVariable("TEST_POSTGRES");
    private readonly string _database = $"contract_{Guid.NewGuid():N}";

    protected override async Task<Dictionary<string, string?>> CreateDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(Server))
            Assert.Skip("TEST_POSTGRES isn't set; ./scripts/test.sh sets it to test the Postgres stores.");

        await using (var admin = new NpgsqlConnection(Server))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        return new()
        {
            ["ConnectionStrings:Default"] = new NpgsqlConnectionStringBuilder(Server) { Database = _database }.ConnectionString,
        };
    }

    protected override async Task DropDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(Server)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Server);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
