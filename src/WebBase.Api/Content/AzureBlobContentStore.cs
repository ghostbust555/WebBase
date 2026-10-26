using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Content;

/// <summary>
/// An Azure Blob Storage container, or a folder (prefix) in one.
///
/// Connect with either:
/// - Cms:Azure:ConnectionString (+ Cms:Azure:Container), from the storage
///   account's "Access keys" page; or
/// - Cms:Azure:ContainerUrl, https://account.blob.core.windows.net/container.
///   With a SAS token on the end (?sv=...) that token is used; without one,
///   the app's managed identity (on Azure) or your az login (locally), which
///   needs the "Storage Blob Data Reader" role on the container.
/// </summary>
public class AzureBlobContentStore : IContentStore
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    public AzureBlobContentStore(CmsAzureOptions options)
    {
        _prefix = ContentPaths.NormalizePrefix(options.Prefix);
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            if (string.IsNullOrWhiteSpace(options.Container))
                throw new InvalidOperationException("Cms:Azure:ConnectionString is set, so Cms:Azure:Container must be too.");
            _container = new BlobContainerClient(options.ConnectionString, options.Container);
        }
        else if (!string.IsNullOrWhiteSpace(options.ContainerUrl))
        {
            var url = new Uri(options.ContainerUrl);
            _container = url.Query.Contains("sig=")
                ? new BlobContainerClient(url)
                : new BlobContainerClient(url, new DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException("Cms:Source is azure, so set Cms:Azure:ConnectionString or Cms:Azure:ContainerUrl.");
        }
    }

    public string Description => $"azure blob {_container.Uri.GetLeftPart(UriPartial.Path)}/{_prefix}";

    public async Task<IReadOnlyList<ContentEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = new List<ContentEntry>();
        try
        {
            await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, _prefix.Length == 0 ? null : _prefix, ct))
            {
                if (blob.Name.EndsWith('/')) continue;
                entries.Add(new ContentEntry(blob.Name[_prefix.Length..], blob.Properties.ContentLength ?? 0,
                    blob.Properties.LastModified ?? DateTimeOffset.MinValue));
            }
        }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
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
            var download = await _container.GetBlobClient(_prefix + path).DownloadStreamingAsync(cancellationToken: ct);
            return download.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
        {
            throw new ContentUnavailableException($"Can't read {path} from {Description}: {e.Message}", e);
        }
    }
}
