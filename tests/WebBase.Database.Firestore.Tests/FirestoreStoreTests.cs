using WebBase.Modules;

namespace WebBase.Database.Firestore.Tests;

/// <summary>
/// The contract against the Firestore emulator. Needs TEST_FIRESTORE, the
/// emulator's host:port (e.g. localhost:8085); skipped without it. Each test gets
/// its own project id, which the emulator keeps apart, and clears it afterwards.
/// The module's README.md shows how to start the emulator.
/// </summary>
public class FirestoreStoreTests : StoreContractTests
{
    protected override IDatabaseModule Module => new FirestoreModule();

    private static readonly string? Emulator = Environment.GetEnvironmentVariable("TEST_FIRESTORE");
    private readonly string _project = $"contract-{Guid.NewGuid():N}"[..30];

    protected override Task<Dictionary<string, string?>> CreateDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(Emulator)) Assert.Skip("TEST_FIRESTORE isn't set.");
        // How Google's libraries find an emulator. Every test sets the same value.
        Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", Emulator);
        return Task.FromResult(new Dictionary<string, string?> { ["Firestore:ProjectId"] = _project });
    }

    protected override async Task DropDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(Emulator)) return;
        using var http = new HttpClient();
        try { await http.DeleteAsync($"http://{Emulator}/emulator/v1/projects/{_project}/databases/(default)/documents"); }
        catch (HttpRequestException) { }
    }
}
