using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Content;

/// <summary>
/// An S3 bucket, or a folder (prefix) in one. Also any S3-compatible service -
/// Cloudflare R2, DigitalOcean Spaces, Backblaze B2, MinIO - via Cms:S3:ServiceUrl.
///
/// Credentials: Cms:S3:AccessKey / SecretKey if set, otherwise the usual AWS
/// chain (the role of the ECS task or EC2 machine, AWS_* environment
/// variables, ~/.aws). Needs s3:ListBucket on the bucket and s3:GetObject on
/// its objects - read-only is enough.
/// </summary>
public sealed class S3ContentStore : IContentStore, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3ContentStore(CmsS3Options options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket))
            throw new InvalidOperationException("Cms:Source is s3, so Cms:S3:Bucket must be set.");
        _bucket = options.Bucket;
        _prefix = ContentPaths.NormalizePrefix(options.Prefix);

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

    public async Task<IReadOnlyList<ContentEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = new List<ContentEntry>();
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
                    entries.Add(new ContentEntry(o.Key[_prefix.Length..], o.Size ?? 0,
                        o.LastModified is { } modified ? new DateTimeOffset(modified.ToUniversalTime()) : DateTimeOffset.MinValue));
                }
                request.ContinuationToken = page.NextContinuationToken;
            } while (page.IsTruncated == true);
        }
        catch (AmazonServiceException e)
        {
            throw new ContentUnavailableException($"Can't list {Description}: {e.Message}", e);
        }
        return entries;
    }

    public async Task<Stream?> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!ContentPaths.IsSafeRelative(path)) return null;
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
            throw new ContentUnavailableException($"Can't read {path} from {Description}: {e.Message}", e);
        }
    }

    public void Dispose() => _s3.Dispose();
}
