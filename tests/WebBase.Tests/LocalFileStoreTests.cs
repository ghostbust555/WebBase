namespace WebBase.Tests;

/// <summary>The contract for a folder on disk (the default). Always runs.</summary>
public class LocalFileStoreTests : FileStoreContractTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("content-store-").FullName;

    protected override Task<IFileStore> CreateAsync(Dictionary<string, byte[]> files)
    {
        foreach (var (path, bytes) in files)
        {
            var full = Path.Combine(_root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return Task.FromResult<IFileStore>(new LocalFileStore(Path.Combine(_root, Prefix)));
    }

    protected override Task CleanUpAsync()
    {
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_just_empty()
    {
        Assert.Empty(await new LocalFileStore(Path.Combine(_root, "missing")).ListAsync());
    }
}
