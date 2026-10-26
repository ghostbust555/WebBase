using Azure.Storage.Blobs;
using WebBase.FileStorage;

namespace WebBase.FileStorage.AzureBlob.Tests;

/// <summary>
/// The contract against Azure Blob Storage. Needs TEST_AZURE_BLOB, a
/// connection string; ./scripts/test.sh starts Azurite (Microsoft's local
/// emulator) with a one-off random key and sets it. Skipped without it.
/// </summary>
public class AzureBlobFileStoreTests : FileStoreContractTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("TEST_AZURE_BLOB");
    private readonly string _container = $"t-{Guid.NewGuid():N}";
    private BlobContainerClient? _admin;

    protected override async Task<IFileStore> CreateAsync(Dictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) Assert.Skip("TEST_AZURE_BLOB isn't set; ./scripts/test.sh sets it.");
        _admin = new BlobContainerClient(ConnectionString, _container);
        await _admin.CreateAsync();
        foreach (var (path, bytes) in files) await _admin.UploadBlobAsync(path, new BinaryData(bytes));

        return new AzureBlobFileStore(new AzureBlobOptions
        {
            ConnectionString = ConnectionString!, Container = _container, Prefix = Prefix,
        });
    }

    protected override async Task CleanUpAsync()
    {
        if (_admin != null) await _admin.DeleteIfExistsAsync();
    }
}
