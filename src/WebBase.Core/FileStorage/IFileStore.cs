namespace WebBase.FileStorage;

/// <summary>
/// A place files are kept, read-only: a folder (LocalFileStore), or a cloud
/// bucket from a module (WebBase.FileStorage.S3, .AzureBlob, .GoogleCloud).
/// The CMS only ever lists and reads its files through this, so any of them
/// works the same way; Cms:Source picks one (docs/CMS.md, docs/MODULES.md).
///
/// The contract (tests/WebBase.Testing/FileStoreContractTests.cs checks it):
/// - Paths are relative to the store's root, use "/" between folders, and
///   never start with "/". Folders exist only as part of file paths.
/// - ListAsync returns every file under the root, however deep.
/// - OpenAsync returns null for a file that isn't there, never throws for it.
/// </summary>
public interface IFileStore
{
    /// <summary>For logs and errors, e.g. "files in /app/content" or "s3://bucket/site/".</summary>
    string Description { get; }

    Task<IReadOnlyList<FileEntry>> ListAsync(CancellationToken ct = default);

    Task<Stream?> OpenAsync(string path, CancellationToken ct = default);
}

/// <summary>One file. Size and LastModified tell the CMS when it needs to re-read it.</summary>
public record FileEntry(string Path, long Size, DateTimeOffset LastModified);

/// <summary>The store couldn't be reached (network, permissions). The site answers 503 until it can.</summary>
public class FileStoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
