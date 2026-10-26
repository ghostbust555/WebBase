using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using WebBase.FileStorage;

namespace WebBase.FileStorage.S3.Tests;

/// <summary>
/// The contract against S3 or anything S3-compatible. Needs TEST_S3_URL, an
/// S3 endpoint accepting the key "test"/"test"; ./scripts/test.sh starts one
/// (adobe/s3mock) and sets it. Skipped without it.
/// </summary>
public class S3FileStoreTests : FileStoreContractTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("TEST_S3_URL");
    private readonly string _bucket = $"t-{Guid.NewGuid():N}";
    private AmazonS3Client? _admin;

    protected override async Task<IFileStore> CreateAsync(Dictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(Url)) Assert.Skip("TEST_S3_URL isn't set; ./scripts/test.sh sets it.");
        _admin = new AmazonS3Client(new BasicAWSCredentials("test", "test"),
            new AmazonS3Config { ServiceURL = Url, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await _admin.PutBucketAsync(_bucket);
        foreach (var (path, bytes) in files)
            await _admin.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = path, InputStream = new MemoryStream(bytes) });

        return new S3FileStore(new S3Options
        {
            Bucket = _bucket, Prefix = Prefix, ServiceUrl = Url!, AccessKey = "test", SecretKey = "test",
        });
    }

    protected override async Task CleanUpAsync()
    {
        if (_admin == null) return;
        var objects = await _admin.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket });
        foreach (var o in objects.S3Objects ?? []) await _admin.DeleteObjectAsync(_bucket, o.Key);
        await _admin.DeleteBucketAsync(_bucket);
        _admin.Dispose();
    }
}
