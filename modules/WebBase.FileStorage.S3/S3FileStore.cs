using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using WebBase.Modules;

namespace WebBase.FileStorage.S3;

/// <summary>
/// An S3 bucket, or a folder (prefix) in one. Also any S3-compatible service -
/// Cloudflare R2, DigitalOcean Spaces, Backblaze B2, MinIO - via S3:ServiceUrl.
///
/// Credentials: S3:AccessKey / SecretKey if set, otherwise the usual AWS
/// chain (the role of the ECS task or EC2 machine, AWS_* environment
/// variables, ~/.aws). Needs s3:ListBucket on the bucket and s3:GetObject on
/// its objects - read-only is enough.
/// </summary>
public sealed class S3FileStore : IFileStore, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3FileStore(S3Options options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket))
            throw new InvalidOperationException("The S3 store needs a bucket: set Bucket (for the CMS, CMS_S3_BUCKET).");
        _bucket = options.Bucket;
        _prefix = FilePaths.NormalizePrefix(options.Prefix);

        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.ForcePathStyle = true;
            // Checksums only where S3 itself requires them: several
            // S3-compatible services don't support the newer default ones.
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }
        if (!string.IsNullOrWhiteSpace(options.Region)) config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        else if (!string.IsNullOrWhiteSpace(options.ServiceUrl)) config.AuthenticationRegion = "us-east-1";

        _s3 = string.IsNullOrWhiteSpace(options.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public string Description => $"s3://{_bucket}/{_prefix}";

    public async Task<IReadOnlyList<FileEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = new List<FileEntry>();
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix };
        try
        {
            ListObjectsV2Response page;
            do
            {
                page = await _s3.ListObjectsV2Async(request, ct);
                foreach (var o in page.S3Objects ?? [])
                {
                    if (o.Key.EndsWith('/')) continue; // a "folder" placeholder some tools create
                    entries.Add(new FileEntry(o.Key[_prefix.Length..], o.Size ?? 0,
                        o.LastModified is { } modified ? new DateTimeOffset(modified.ToUniversalTime()) : DateTimeOffset.MinValue));
                }
                request.ContinuationToken = page.NextContinuationToken;
            } while (page.IsTruncated == true);
        }
        catch (AmazonServiceException e)
        {
            throw new FileStoreUnavailableException($"Can't list {Description}: {e.Message}", e);
        }
        return entries;
    }

    public async Task<Stream?> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!FilePaths.IsSafeRelative(path)) return null;
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, _prefix + path, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonServiceException e)
        {
            throw new FileStoreUnavailableException($"Can't read {path} from {Description}: {e.Message}", e);
        }
    }

    public void Dispose() => _s3.Dispose();
}

/// <summary>Settings for one S3 store (for the CMS: the Cms:S3 section).</summary>
public class S3Options
{
    public string Bucket { get; set; } = "";
    /// <summary>A folder in the bucket, e.g. "site/". Empty = the whole bucket.</summary>
    public string Prefix { get; set; } = "";
    public string Region { get; set; } = "";
    /// <summary>For S3-compatible services (R2, Spaces, B2, MinIO): their endpoint URL.</summary>
    public string ServiceUrl { get; set; } = "";
    /// <summary>Optional; without them the standard AWS credential chain is used.</summary>
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
}

/// <summary>Amazon S3, or anything S3-compatible: the file storage called "s3".</summary>
public class S3Module : IFileStorageModule
{
    public string Name => "s3";

    public IFileStore Create(IConfiguration settings) => new S3FileStore(settings.Get<S3Options>() ?? new S3Options());
}
