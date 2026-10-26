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
/// </summary>
public class CosmosDatabase(CosmosClient client, CosmosSettings settings) : IDatabase
{
    public string Provider => "cosmos";

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        Microsoft.Azure.Cosmos.Database db = await client.CreateDatabaseIfNotExistsAsync(settings.DatabaseName, cancellationToken: ct);
        await db.CreateContainerIfNotExistsAsync(CosmosSettings.Users, "/id", cancellationToken: ct);
        await db.CreateContainerIfNotExistsAsync(CosmosSettings.Keys, "/id", cancellationToken: ct);
        await db.CreateContainerIfNotExistsAsync(CosmosSettings.Notes, "/userId", cancellationToken: ct);
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
    public const string Users = "users", Keys = "keys", Notes = "notes";
    public string DatabaseName { get; init; } = "webbase";
}

/// <summary>
/// Azure Cosmos DB for NoSQL: Database:Provider "cosmos". Settings:
/// Cosmos:ConnectionString (required, a secret), Cosmos:Database (default
/// "webbase"), Cosmos:Gateway - true only for the local emulator.
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
        services.AddSingleton(new CosmosSettings { DatabaseName = config["Cosmos:Database"] ?? "webbase" });
        services.AddScoped<IDatabase, CosmosDatabase>();
        services.AddScoped<IUserStore, CosmosUserStore>();
        services.AddScoped<INoteStore, CosmosNoteStore>();
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
