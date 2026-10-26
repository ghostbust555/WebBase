using System.Globalization;
using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using WebBase.Modules;

namespace WebBase.FileStorage.GoogleCloud;

/// <summary>
/// A Google Cloud Storage bucket, or a folder (prefix) in one.
///
/// Credentials: Google's Application Default Credentials. On Cloud Run that's
/// the service's own service account, with nothing to configure: give it the
/// "Storage Object Viewer" role on the bucket (scripts/cloud/gcp.sh does).
/// On your computer: `gcloud auth application-default login`, or
/// GOOGLE_APPLICATION_CREDENTIALS pointing at a key file.
/// </summary>
public sealed class GoogleCloudFileStore : IFileStore, IDisposable
{
    private readonly StorageClient _client;
    private readonly string _bucket;
    private readonly string _prefix;

    public GoogleCloudFileStore(GoogleCloudStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket))
            throw new InvalidOperationException("The Google Cloud Storage store needs a bucket: set Bucket (for the CMS, CMS_GCS_BUCKET).");
        _bucket = options.Bucket;
        _prefix = FilePaths.NormalizePrefix(options.Prefix);

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            // A local stand-in (fake-gcs-server), for tests: no credentials.
            _client = new StorageClientBuilder
            {
                BaseUri = options.ServiceUrl.TrimEnd('/') + "/storage/v1/",
                UnauthenticatedAccess = true,
            }.Build();
            return;
        }
        try
        {
            _client = StorageClient.Create();
        }
        catch (InvalidOperationException e)
        {
            throw new InvalidOperationException(
                "No Google credentials for reading the bucket. On Cloud Run they're automatic; on your computer run " +
                "`gcloud auth application-default login`. " + e.Message, e);
        }
    }

    public string Description => $"gs://{_bucket}/{_prefix}";

    public async Task<IReadOnlyList<FileEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = new List<FileEntry>();
        try
        {
            var options = new ListObjectsOptions { Fields = "items(name,size,updated),nextPageToken" };
            await foreach (var o in _client.ListObjectsAsync(_bucket, _prefix.Length == 0 ? null : _prefix, options).WithCancellation(ct))
            {
                if (o.Name.EndsWith('/')) continue; // a "folder" placeholder the console creates
                entries.Add(new FileEntry(o.Name[_prefix.Length..], (long)(o.Size ?? 0), Updated(o.UpdatedRaw)));
            }
        }
        catch (Exception e) when (e is GoogleApiException or HttpRequestException)
        {
            throw new FileStoreUnavailableException($"Can't list {Description}: {e.Message}", e);
        }
        return entries;
    }

    /// <summary>
    /// The object's last change. Parsed here rather than by the client
    /// (UpdatedDateTimeOffset), which rejects RFC 3339 times written with
    /// fewer than three decimals, as some GCS-compatible servers do.
    /// </summary>
    private static DateTimeOffset Updated(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;

    public async Task<Stream?> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!FilePaths.IsSafeRelative(path)) return null;
        var stream = new MemoryStream();
        try
        {
            await _client.DownloadObjectAsync(_bucket, _prefix + path, stream, cancellationToken: ct);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception e) when (e is GoogleApiException or HttpRequestException)
        {
            throw new FileStoreUnavailableException($"Can't read {path} from {Description}: {e.Message}", e);
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Settings for one Google Cloud Storage store (for the CMS: the Cms:Gcs section).</summary>
public class GoogleCloudStorageOptions
{
    public string Bucket { get; set; } = "";
    /// <summary>A folder in the bucket, e.g. "site/". Empty = the whole bucket.</summary>
    public string Prefix { get; set; } = "";
    /// <summary>Tests only: a local stand-in's address, e.g. http://localhost:4443. Empty = Google itself.</summary>
    public string ServiceUrl { get; set; } = "";
}

/// <summary>Google Cloud Storage: the file storage called "gcs".</summary>
public class GoogleCloudStorageModule : IFileStorageModule
{
    public string Name => "gcs";

    public IFileStore Create(IConfiguration settings) =>
        new GoogleCloudFileStore(settings.Get<GoogleCloudStorageOptions>() ?? new GoogleCloudStorageOptions());
}
