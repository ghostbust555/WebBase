using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebBase.Api.Content;
using WebBase.Api.Infrastructure;

namespace WebBase.Tests.ContentStores;

/// <summary>
/// What every place the CMS can read from must do, written once. Each kind of
/// store gets a subclass that knows how to put files into an empty one; the
/// tests then read them back through IContentStore. A new kind of store is
/// finished when these pass for it.
/// </summary>
public abstract class ContentStoreContractTests : IAsyncLifetime
{
    /// <summary>The files every test starts with, inside the store's folder (prefix).</summary>
    protected static readonly Dictionary<string, string> Files = new()
    {
        ["index.md"] = "---\ntitle: Home\n---\nWelcome",
        ["about.md"] = "About",
        ["blog/2026/deep.md"] = "---\ntitle: Deep\n---\nDown here",
        ["images/cat.svg"] = "<svg/>",
        ["theme.css"] = ":root { --accent: #123456; }",
        ["with space é.txt"] = "unicode",
    };

    /// <summary>Files next to the store's folder that it must NOT see (another site's, say).</summary>
    protected static readonly Dictionary<string, string> Neighbours = new()
    {
        ["outside.md"] = "not mine",
        ["site-other/index.md"] = "not mine either",
    };

    /// <summary>Folder (prefix) the store under test reads, inside its bucket/container/directory.</summary>
    protected const string Prefix = "site";

    protected IContentStore Store { get; private set; } = null!;

    /// <summary>
    /// Make an empty bucket (or container, or directory), put every file in
    /// <paramref name="files"/> there (paths relative to its root), and return a store
    /// reading its "site/" folder. Call Assert.Skip if it can't be tested here.
    /// </summary>
    protected abstract Task<IContentStore> CreateAsync(Dictionary<string, byte[]> files);

    protected abstract Task CleanUpAsync();

    public async ValueTask InitializeAsync()
    {
        var all = Files.ToDictionary(f => $"{Prefix}/{f.Key}", f => Encoding.UTF8.GetBytes(f.Value));
        foreach (var (path, text) in Neighbours) all[path] = Encoding.UTF8.GetBytes(text);
        Store = await CreateAsync(all);
    }

    public async ValueTask DisposeAsync()
    {
        (Store as IDisposable)?.Dispose();
        await CleanUpAsync();
        GC.SuppressFinalize(this);
    }

    private static async Task<string?> ReadAsync(IContentStore store, string path)
    {
        await using var stream = await store.OpenAsync(path);
        return stream == null ? null : await new StreamReader(stream).ReadToEndAsync();
    }

    [Fact]
    public async Task It_lists_every_file_in_its_folder_however_deep_and_nothing_else()
    {
        var listed = await Store.ListAsync();
        Assert.Equal(Files.Keys.Order(StringComparer.Ordinal), listed.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.All(listed, e => Assert.Equal(Encoding.UTF8.GetByteCount(Files[e.Path]), e.Size));
        Assert.All(listed, e => Assert.True(e.LastModified > DateTimeOffset.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task It_reads_files_back_exactly()
    {
        foreach (var (path, text) in Files) Assert.Equal(text, await ReadAsync(Store, path));
    }

    [Fact]
    public async Task A_missing_file_is_null_not_an_error()
    {
        Assert.Null(await ReadAsync(Store, "nope.md"));
        Assert.Null(await ReadAsync(Store, "blog/nope/deeper.md"));
    }

    [Fact]
    public async Task It_cannot_read_outside_its_folder()
    {
        Assert.Null(await ReadAsync(Store, "../outside.md"));
        Assert.Null(await ReadAsync(Store, "../site-other/index.md"));
    }

    [Fact]
    public async Task The_cms_works_on_top_of_it()
    {
        var cms = new ContentService(Store, Options.Create(new CmsOptions { Enabled = true, RefreshSeconds = 0 }),
            Options.Create(new AppOptions()), NullLogger<ContentService>.Instance, TimeProvider.System);

        Assert.Equal("Home", (await cms.GetPageAsync(""))!.Title);
        Assert.Equal("Down here", (await cms.GetPageAsync("blog/2026/deep"))!.Markdown);
        Assert.Contains("--accent: #123456", await cms.ThemeAsync());
        Assert.NotNull(await cms.FindFileAsync("images/cat.svg"));
        Assert.Contains("about", (await cms.AllPagesAsync()).Select(p => p.Path));
    }
}
