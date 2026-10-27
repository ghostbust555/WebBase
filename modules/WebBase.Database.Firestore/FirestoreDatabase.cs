using Google.Api.Gax;
using Google.Cloud.Firestore;
using WebBase.Modules;

namespace WebBase.Database.Firestore;

/// <summary>
/// Google Cloud Firestore (Native mode) behind the same interfaces as Postgres.
/// Collections:
///   users              one document per user, its id the user's id
///   keys               one tiny document per unique value ("this email belongs to user X",
///                      "this page address belongs to page Y"), its id a hash of the value
///   users/{id}/notes   each user's notes, inside their own user document: a note can only
///                      be reached by saying whose it is (the INoteStore rule, for free)
///   pages              the pages authors write, one document each
///
/// Uniqueness is a transaction: read the keys, refuse if one belongs to someone
/// else, write the record and its keys together. Firestore retries a transaction
/// that raced another, so the loser sees the winner's key and gets DuplicateKeyException.
///
/// Nothing to create up front: Firestore makes collections on first write, and every
/// query here uses one field, which Firestore indexes by itself.
/// </summary>
public class FirestoreDatabase(FirestoreDb db) : IDatabase
{
    public string Provider => "firestore";

    public Task PrepareAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await db.Collection(FirestoreNames.Users).Limit(1).GetSnapshotAsync(timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public static class FirestoreNames
{
    public const string Users = "users", Keys = "keys", Notes = "notes", Pages = "pages";
}

/// <summary>
/// Google Cloud Firestore (Native mode): Database:Provider "firestore". Settings:
/// Firestore:ProjectId (default: the project the app runs in, which Cloud Run
/// knows; elsewhere GOOGLE_CLOUD_PROJECT), Firestore:Database (default "(default)",
/// the one database Firestore's free tier covers).
///
/// Signs in the Google way (Application Default Credentials): on Cloud Run, as
/// the service's own account, with no key or password anywhere. On a computer,
/// `gcloud auth application-default login`. With FIRESTORE_EMULATOR_HOST set
/// (host:port), it talks to the local emulator instead, with no sign-in.
/// </summary>
public class FirestoreModule : IDatabaseModule
{
    public string Name => "firestore";

    public void Register(IServiceCollection services, IConfiguration config)
    {
        var projectId = config["Firestore:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId)) projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
        if (string.IsNullOrWhiteSpace(projectId)) projectId = Platform.Instance()?.ProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException(
                "Database:Provider is firestore, so set Firestore:ProjectId to your Google Cloud project's id " +
                "(on Cloud Run it's found by itself). See modules/WebBase.Database.Firestore/README.md.");
        var databaseId = config["Firestore:Database"];

        // Built on first use, not here: building it signs in, and an app that
        // can't must still start far enough to say so on /api/health.
        services.AddSingleton(_ => new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = string.IsNullOrWhiteSpace(databaseId) ? null : databaseId,
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.Build());
        services.AddScoped<IDatabase, FirestoreDatabase>();
        services.AddScoped<IUserStore, FirestoreUserStore>();
        services.AddScoped<INoteStore, FirestoreNoteStore>();
        services.AddScoped<IPageStore, FirestorePageStore>();
    }
}
