using WebBase.Modules;
using WebBase.Testing.InMemory;

namespace WebBase.Tests;

/// <summary>
/// The API tests run on the in-memory database, so it must keep the same
/// contract as the real ones: otherwise a test could pass here and the app
/// fail on PostgreSQL.
/// </summary>
public class InMemoryStoreTests : StoreContractTests
{
    protected override IDatabaseModule Module => new InMemoryDatabaseModule();
    protected override Task<Dictionary<string, string?>> CreateDatabaseAsync() => Task.FromResult(new Dictionary<string, string?>());
    protected override Task DropDatabaseAsync() => Task.CompletedTask;
}
