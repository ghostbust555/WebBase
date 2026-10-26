using System.Text;
using Google.Cloud.Storage.V1;
using WebBase.FileStorage;

namespace WebBase.FileStorage.GoogleCloud.Tests;

/// <summary>
/// The contract against Google Cloud Storage. Needs TEST_GCS_URL, the address
/// of a stand-in (fake-gcs-server); ./scripts/test.sh starts one and sets it.
/// Skipped without it.
/// </summary>
public class GoogleCloudFileStoreTests : FileStoreContractTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("TEST_GCS_URL");
    private readonly string _bucket = $"t-{Guid.NewGuid():N}";
    private StorageClient? _admin;

    protected override async Task<IFileStore> CreateAsync(Dictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(Url)) Assert.Skip("TEST_GCS_URL isn't set; ./scripts/test.sh sets it.");
        _admin = new StorageClientBuilder { BaseUri = Url!.TrimEnd('/') + "/storage/v1/", UnauthenticatedAccess = true }.Build();
        await _admin.CreateBucketAsync("test-project", _bucket);
        foreach (var (path, bytes) in files)
            await _admin.UploadObjectAsync(_bucket, path, "application/octet-stream", new MemoryStream(bytes));

        return new GoogleCloudFileStore(new GoogleCloudStorageOptions { Bucket = _bucket, Prefix = Prefix, ServiceUrl = Url! });
    }

    protected override async Task CleanUpAsync()
    {
        if (_admin == null) return;
        await foreach (var o in _admin.ListObjectsAsync(_bucket)) await _admin.DeleteObjectAsync(_bucket, o.Name);
        await _admin.DeleteBucketAsync(_bucket);
        _admin.Dispose();
    }

    [Fact]
    public void A_bucket_is_required()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new GoogleCloudFileStore(new GoogleCloudStorageOptions()));
        Assert.Contains("CMS_GCS_BUCKET", error.Message);
    }
}
