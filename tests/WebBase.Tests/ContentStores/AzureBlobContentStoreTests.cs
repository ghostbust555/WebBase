using Azure.Storage.Blobs;
using WebBase.Api.Content;
using WebBase.Api.Infrastructure;

namespace WebBase.Tests.ContentStores;

/// <summary>
/// The contract against Azure Blob Storage. Needs TEST_AZURE_BLOB, a
/// connection string; ./scripts/test.sh starts Azurite (Microsoft's local
/// emulator) with a one-off random key and sets it. Skipped without it.
/// </summary>
public class AzureBlobContentStoreTests : ContentStoreContractTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("TEST_AZURE_BLOB");
    private readonly string _container = $"t-{Guid.NewGuid():N}";
    private BlobContainerClient? _admin;

    protected override async Task<IContentStore> CreateAsync(Dictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) Assert.Skip("TEST_AZURE_BLOB isn't set; ./scripts/test.sh sets it.");
        _admin = new BlobContainerClient(ConnectionString, _container);
        await _admin.CreateAsync();
        foreach (var (path, bytes) in files) await _admin.UploadBlobAsync(path, new BinaryData(bytes));

        return new AzureBlobContentStore(new CmsAzureOptions
        {
            ConnectionString = ConnectionString!, Container = _container, Prefix = Prefix,
        });
    }

    protected override async Task CleanUpAsync()
    {
        if (_admin != null) await _admin.DeleteIfExistsAsync();
    }
}
