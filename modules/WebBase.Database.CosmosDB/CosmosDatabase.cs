using System.Text.Json;
using Microsoft.Azure.Cosmos;
using WebBase.Modules;

namespace WebBase.Database.CosmosDB;

/// <summary>
/// Azure Cosmos DB (NoSQL API) behind the same interfaces as Postgres.
/// Three containers:
///   users  one document per user, partitioned by its id
///   keys   one tiny document per unique value ("this email belongs to user X"),
///          partitioned by its id - Cosmos only enforces uniqueness of ids
///          within a partition, so a unique email is an id that can exist once
///   notes  partitioned by userId: a user's notes live together, and a note can
///          only be found by saying whose it is (the INoteStore rule, for free)
///   pages  the authors' pages, all in one partition ("site") with a unique key on /path
/// </summary>
public class CosmosDatabase(CosmosClient client, CosmosSettings settings) : IDatabase
{
    public string Provider => "cosmos";

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        // With a shared throughput, the three containers split it: what Cosmos DB's free tier
        // (1000 RU/s in all) needs. Without one, each container reserves its own 400 RU/s.
        ThroughputProperties? shared = settings.DatabaseThroughput is { } ru ? ThroughputProperties.CreateManualThroughput(ru) : null;
        Microsoft.Azure.Cosmos.Database db = await client.CreateDatabaseIfNotExistsAsync(settings.DatabaseName, shared, cancellationToken: ct);
        await db.CreateContainerIfNotExistsAsync(CosmosSettings.Users, "/id", cancellationToken: ct);
        await db.CreateContainerIfNotExistsAsync(CosmosSettings.Keys, "/id", cancellationToken: ct);
        await db.CreateContainerIfNotExistsAsync(CosmosSettings.Notes, "/userId", cancellationToken: ct);
        // The authors' pages: one logical partition ("site"), so the unique key on the path
        // means "unique in the whole site". A unique key can only be set when the container is made.
        var pages = new ContainerProperties(CosmosSettings.Pages, "/site");
        pages.UniqueKeyPolicy.UniqueKeys.Add(new UniqueKey { Paths = { "/path" } });
        await db.CreateContainerIfNotExistsAsync(pages, cancellationToken: ct);
    }

    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            await client.GetDatabase(settings.DatabaseName).ReadAsync(cancellationToken: ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public class CosmosSettings
{
    public const string Users = "users", Keys = "keys", Notes = "notes", Pages = "pages";
    public string DatabaseName { get; init; } = "webbase";

    /// <summary>RU/s shared by all containers, or null for serverless accounts and the emulator.</summary>
    public int? DatabaseThroughput { get; init; }
}

/// <summary>
/// Azure Cosmos DB for NoSQL: Database:Provider "cosmos". Settings:
/// Cosmos:ConnectionString (required, a secret), Cosmos:Database (default
/// "webbase"), Cosmos:DatabaseThroughput (RU/s for the whole database, 400-1000
/// on the free tier; leave empty for serverless), Cosmos:Gateway - true only
/// for the local emulator.
/// </summary>
public class CosmosModule : IDatabaseModule
{
    public string Name => "cosmos";

    public void Register(IServiceCollection services, IConfiguration config)
    {
        var connectionString = config["Cosmos:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database:Provider is cosmos, so Cosmos:ConnectionString must be set.");
        var gateway = config.GetValue("Cosmos:Gateway", false);

        // One client for the whole app: it's thread-safe and holds the connections.
        services.AddSingleton(new CosmosClient(connectionString, new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            ConnectionMode = gateway ? ConnectionMode.Gateway : ConnectionMode.Direct,
            LimitToEndpoint = gateway,
            // Emulator only: its binary-encoded query answers didn't parse in
            // SDK 3.54 (emulator vnext-preview, October 2026), so ask for JSON text.
            HttpClientFactory = gateway ? () => new HttpClient(new TextOnly { InnerHandler = new HttpClientHandler() }) : null,
        }));
        services.AddSingleton(new CosmosSettings
        {
            DatabaseName = config["Cosmos:Database"] ?? "webbase",
            DatabaseThroughput = config.GetValue<int?>("Cosmos:DatabaseThroughput"),
        });
        services.AddScoped<IDatabase, CosmosDatabase>();
        services.AddScoped<IUserStore, CosmosUserStore>();
        services.AddScoped<INoteStore, CosmosNoteStore>();
        services.AddScoped<IPageStore, CosmosPageStore>();
    }

    private sealed class TextOnly : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.Remove("x-ms-cosmos-supported-serialization-formats");
            request.Headers.Remove("x-ms-documentdb-content-serialization-format");
            return base.SendAsync(request, ct);
        }
    }

}

internal static class CosmosExceptions
{
    internal static bool Is(this CosmosException e, System.Net.HttpStatusCode status) => e.StatusCode == status;
}
