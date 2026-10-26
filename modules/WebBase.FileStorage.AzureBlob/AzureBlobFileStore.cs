using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using WebBase.Modules;

namespace WebBase.FileStorage.AzureBlob;

/// <summary>
/// An Azure Blob Storage container, or a folder (prefix) in one.
///
/// Connect with either:
/// - ConnectionString (+ Container), from the storage
///   account's "Access keys" page; or
/// - ContainerUrl, https://account.blob.core.windows.net/container.
///   With a SAS token on the end (?sv=...) that token is used; without one,
///   the app's managed identity (on Azure) or your az login (locally), which
///   needs the "Storage Blob Data Reader" role on the container.
/// </summary>
public class AzureBlobFileStore : IFileStore
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    public AzureBlobFileStore(AzureBlobOptions options)
    {
        _prefix = FilePaths.NormalizePrefix(options.Prefix);
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            if (string.IsNullOrWhiteSpace(options.Container))
                throw new InvalidOperationException("The Azure Blob store has a ConnectionString, so it needs a Container too (for the CMS, CMS_AZURE_CONTAINER).");
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
            throw new InvalidOperationException("The Azure Blob store needs a ContainerUrl or a ConnectionString (for the CMS, CMS_AZURE_CONTAINER_URL or CMS_AZURE_CONNECTION_STRING).");
        }
    }

    public string Description => $"azure blob {_container.Uri.GetLeftPart(UriPartial.Path)}/{_prefix}";

    public async Task<IReadOnlyList<FileEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = new List<FileEntry>();
        try
        {
            await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, _prefix.Length == 0 ? null : _prefix, ct))
            {
                if (blob.Name.EndsWith('/')) continue;
                entries.Add(new FileEntry(blob.Name[_prefix.Length..], blob.Properties.ContentLength ?? 0,
                    blob.Properties.LastModified ?? DateTimeOffset.MinValue));
            }
        }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
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
            var download = await _container.GetBlobClient(_prefix + path).DownloadStreamingAsync(cancellationToken: ct);
            return download.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
        {
            throw new FileStoreUnavailableException($"Can't read {path} from {Description}: {e.Message}", e);
        }
    }
}

/// <summary>Settings for one Azure Blob store (for the CMS: the Cms:Azure section).</summary>
public class AzureBlobOptions
{
    public string ConnectionString { get; set; } = "";
    public string Container { get; set; } = "";
    /// <summary>Instead of a connection string: https://account.blob.core.windows.net/container[?sas].</summary>
    public string ContainerUrl { get; set; } = "";
    public string Prefix { get; set; } = "";
}

/// <summary>Azure Blob Storage: the file storage called "azure".</summary>
public class AzureBlobModule : IFileStorageModule
{
    public string Name => "azure";

    public IFileStore Create(IConfiguration settings) => new AzureBlobFileStore(settings.Get<AzureBlobOptions>() ?? new AzureBlobOptions());
}
