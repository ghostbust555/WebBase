using Microsoft.Azure.Cosmos;

namespace WebBase.Tests.StoreContract;

/// <summary>The contract against Cosmos DB (or its emulator). Needs TEST_COSMOS, a connection string.</summary>
public class CosmosStoreTests : StoreContractTests
{
    private static readonly string? Account = Environment.GetEnvironmentVariable("TEST_COSMOS");
    private readonly string _database = $"contract_{Guid.NewGuid():N}";

    protected override Task<Dictionary<string, string?>> CreateDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(Account)) Assert.Skip("TEST_COSMOS isn't set.");
        return Task.FromResult(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "cosmos",
            ["Cosmos:ConnectionString"] = Account,
            ["Cosmos:Database"] = _database,
            ["Cosmos:Gateway"] = "true",
        });
    }

    protected override async Task DropDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(Account)) return;
        using var client = new CosmosClient(Account, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway, LimitToEndpoint = true });
        try { await client.GetDatabase(_database).DeleteAsync(); } catch (CosmosException) { }
    }
}
