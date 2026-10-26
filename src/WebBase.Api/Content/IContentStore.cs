namespace WebBase.Api.Content;

/// <summary>
/// Where the CMS's files are kept: a folder, an S3 bucket, an Azure Blob
/// container... The CMS (ContentService) only ever lists and reads files
/// through this, so any of them works the same way. Cms:Source picks one
/// (ContentRegistration.cs, docs/CMS.md).
///
/// The contract (tests/WebBase.Tests/ContentStores/ checks it):
/// - Paths are relative to the store's root, use "/" between folders, and
///   never start with "/". Folders exist only as part of file paths.
/// - ListAsync returns every file under the root, however deep.
/// - OpenAsync returns null for a file that isn't there, never throws for it.
/// </summary>
public interface IContentStore
{
    /// <summary>For logs and errors, e.g. "files in /app/content" or "s3://bucket/site/".</summary>
    string Description { get; }

    Task<IReadOnlyList<ContentEntry>> ListAsync(CancellationToken ct = default);

    Task<Stream?> OpenAsync(string path, CancellationToken ct = default);
}

/// <summary>One file. Size and LastModified tell the CMS when it needs to re-read it.</summary>
public record ContentEntry(string Path, long Size, DateTimeOffset LastModified);

/// <summary>The store couldn't be reached (network, permissions). The site answers 503 until it can.</summary>
public class ContentUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
