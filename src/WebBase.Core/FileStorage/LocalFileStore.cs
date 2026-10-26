namespace WebBase.FileStorage;

/// <summary>A folder on disk: content/ in development, /app/content inside the release image.</summary>
public class LocalFileStore(string root) : IFileStore
{
    private readonly string _root = Path.GetFullPath(root);

    public string Description => $"files in {_root}";

    public Task<IReadOnlyList<FileEntry>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root)) return Task.FromResult<IReadOnlyList<FileEntry>>([]);
        IReadOnlyList<FileEntry> entries = new DirectoryInfo(_root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(f => new FileEntry(
                Path.GetRelativePath(_root, f.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                f.Length, f.LastWriteTimeUtc))
            .ToList();
        return Task.FromResult(entries);
    }

    public Task<Stream?> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!FilePaths.IsSafeRelative(path)) return Task.FromResult<Stream?>(null);
        var full = Path.GetFullPath(Path.Combine(_root, path));
        // Never outside the folder, whatever the path says.
        var inside = full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return Task.FromResult<Stream?>(inside && File.Exists(full) ? File.OpenRead(full) : null);
    }
}
