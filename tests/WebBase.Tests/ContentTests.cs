using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebBase.Api.Content;
using WebBase.Api.Infrastructure;

namespace WebBase.Tests;

/// <summary>The optional CMS, against a throwaway content folder.</summary>
public sealed class ContentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("content-tests-").FullName;

    public ContentTests()
    {
        Write("index.md", "---\ntitle: Home\n---\nWelcome");
        Write("about.md", "---\ntitle: \"About us\"\nmenu: true\norder: 1\n---\nHello\n");
        Write("blog/index.md", "---\ntitle: Blog\n---\nPosts");
        Write("blog/older.md", "---\ntitle: Older\ndate: 2026-01-01\n---\nA");
        Write("blog/newer.md", "---\ndate: 2026-06-01\n---\nB");
        Write("blog/secret.md", "---\ntitle: Secret\ndraft: true\n---\nC");
        Write("notes/one.md", "No front matter at all");
        Write("_private.md", "ignored");
        Write("images/cat.svg", "<svg/>");
        Write("images/_draft-banner.svg", "<svg/>");
        Write(".git/config", "secret-ish");
        Write("theme.css", ":root { --accent: #0f766e; color: red; } body { display: none }");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private ContentService Service(bool development = false, IFileStore? store = null) => new(
        store ?? new LocalFileStore(_root),
        Options.Create(new CmsOptions { Enabled = true, RefreshSeconds = 0 }),
        Options.Create(new AppOptions { Environment = development ? "development" : "prod" }),
        NullLogger<ContentService>.Instance, TimeProvider.System);

    [Theory]
    [InlineData("", "Home")]
    [InlineData("/", "Home")]
    [InlineData("about", "About us")]
    [InlineData("/About/", "About us")]
    [InlineData("blog", "Blog")]
    [InlineData("blog/index", "Blog")]
    [InlineData("blog/older", "Older")]
    [InlineData("blog/newer", "Newer")]    // no title: made from the file name
    [InlineData("notes", "Notes")]         // a folder with no index.md
    public async Task Addresses_map_to_files_and_folders(string path, string title)
    {
        Assert.Equal(title, (await Service().GetPageAsync(path))?.Title);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("_private")]
    [InlineData("../etc/passwd")]
    [InlineData("blog/../../x")]
    [InlineData("blog/secret")]            // a draft, in prod
    [InlineData("images")]                 // a folder with files but no pages
    public async Task Missing_ignored_unsafe_and_draft_pages_are_not_found(string path)
    {
        Assert.Null(await Service().GetPageAsync(path));
    }

    [Fact]
    public async Task Drafts_are_visible_in_development()
    {
        Assert.True((await Service(development: true).GetPageAsync("blog/secret"))!.Draft);
    }

    [Fact]
    public async Task Front_matter_is_read_and_removed_from_the_body()
    {
        var about = (await Service().GetPageAsync("about"))!;
        Assert.Equal("Hello\n", about.Markdown);
        Assert.True(about.Menu);
        Assert.Equal("No front matter at all", (await Service().GetPageAsync("notes/one"))!.Markdown);
    }

    [Fact]
    public async Task A_folder_lists_its_pages_newest_first_without_drafts()
    {
        var blog = (await Service().GetPageAsync("blog"))!;
        Assert.Equal(["blog/newer", "blog/older"], blog.Children!.Select(c => c.Path));
    }

    [Fact]
    public async Task All_pages_skips_ignored_files_and_folders_without_pages()
    {
        var paths = (await Service().AllPagesAsync()).Select(p => p.Path).ToList();
        Assert.Equal("about", paths[0]); // order: 1 comes first
        Assert.Contains("blog/older", paths);
        Assert.Contains("notes/one", paths);
        Assert.DoesNotContain(paths, p => p.Contains("private") || p.Contains("secret") || p == "images");
    }

    [Fact]
    public async Task Edits_show_up_once_the_refresh_time_has_passed()
    {
        var clock = new ManualClock();
        var service = new ContentService(new LocalFileStore(_root),
            Options.Create(new CmsOptions { Enabled = true, RefreshSeconds = 60 }),
            Options.Create(new AppOptions()), NullLogger<ContentService>.Instance, clock);
        Assert.Equal("About us", (await service.GetPageAsync("about"))!.Title);

        Write("about.md", "---\ntitle: Changed\n---\n");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "about.md"), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("About us", (await service.GetPageAsync("about"))!.Title); // still the copy in memory
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("Changed", (await service.GetPageAsync("about"))!.Title);
    }

    [Fact]
    public async Task An_unreachable_store_keeps_the_last_copy_and_otherwise_says_so()
    {
        var store = new FlakyStore(new LocalFileStore(_root));
        var service = Service(store: store);
        Assert.NotNull(await service.GetPageAsync("about"));

        store.Down = true;
        Assert.NotNull(await service.GetPageAsync("about")); // the last good copy
        await Assert.ThrowsAsync<FileStoreUnavailableException>(() => Service(store: store).GetPageAsync("about"));
    }

    [Fact]
    public async Task The_theme_keeps_only_the_variables()
    {
        var theme = await Service().ThemeAsync();
        Assert.Contains("--accent: #0f766e", theme);
        Assert.DoesNotContain("color: red", theme);
        Assert.DoesNotContain("display", theme);
    }

    [Theory]
    [InlineData(":root { --a: 1px; }", ":root:root {\n  --a: 1px;\n}\n")]
    [InlineData("/* :root { --gone: 1 } */ :root{--b:red}", ":root:root {\n  --b: red;\n}\n")]
    [InlineData(":root { --x: 1; } @media (prefers-color-scheme: dark) { :root { --x: 2; } }",
        ":root:root {\n  --x: 1;\n}\n@media (prefers-color-scheme: dark) {\n  :root:root {\n    --x: 2;\n  }\n}\n")]
    [InlineData("body { --not-root: 1 }", "")]
    [InlineData(":root { --evil: red</style><script>; --ok: 1 }", ":root:root {\n  --ok: 1;\n}\n")]
    [InlineData(":root { notavariable: 1; --: 2 }", "")]
    public void Theme_css_is_sanitised(string source, string expected)
    {
        Assert.Equal(expected, ThemeCss.Build(source));
    }

    [Fact]
    public async Task The_api_serves_pages_and_files_when_enabled()
    {
        using var app = new TestApp().WithWebHostBuilder(b =>
        {
            b.UseSetting("Cms:Enabled", "true");
            b.UseSetting("Cms:ContentPath", _root);
            b.UseSetting("Cms:RefreshSeconds", "0");
        });
        var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/content/page?path=about");
        Assert.Equal("About us", page.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/content/page?path=nope")).StatusCode);
        var cat = await client.GetAsync("/content/images/cat.svg");
        Assert.Equal(HttpStatusCode.OK, cat.StatusCode);
        Assert.Equal("image/svg+xml", cat.Content.Headers.ContentType?.MediaType);
        Assert.Equal("<svg/>", await cat.Content.ReadAsStringAsync());

        // Unchanged since last time: 304, no body.
        var again = new HttpRequestMessage(HttpMethod.Get, "/content/images/cat.svg");
        again.Headers.IfNoneMatch.Add(cat.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);

        // Never: the markdown itself (drafts live there), private and hidden files, missing ones.
        foreach (var path in new[] { "blog/secret.md", "about.md", "images/_draft-banner.svg", ".git/config", "images/nope.svg", "../appsettings.json" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/content/{path}")).StatusCode);

        var theme = await client.GetAsync("/api/content/theme.css");
        Assert.Equal("text/css", theme.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--accent", await theme.Content.ReadAsStringAsync());
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("cms").GetBoolean());
    }

    [Fact]
    public async Task The_api_is_404_when_the_cms_is_off()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/content/pages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/content/images/placeholder.svg")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("cms").GetBoolean());
        // The website always links the theme, so it's an empty stylesheet, not a 404.
        var theme = await client.GetAsync("/api/content/theme.css");
        Assert.Equal(HttpStatusCode.OK, theme.StatusCode);
        Assert.Equal("", await theme.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_api_says_503_when_the_store_is_unreachable()
    {
        using var app = new TestApp().WithWebHostBuilder(b =>
        {
            b.UseSetting("Cms:Enabled", "true");
            b.ConfigureServices(s => s.AddSingleton<IFileStore>(new FlakyStore(new LocalFileStore(_root)) { Down = true }));
        });
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/content/page?path=about")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/content/theme.css")).StatusCode);
    }

    private sealed class FlakyStore(IFileStore inner) : IFileStore
    {
        public bool Down { get; set; }
        public string Description => "flaky";
        public Task<IReadOnlyList<FileEntry>> ListAsync(CancellationToken ct = default) =>
            Down ? throw new FileStoreUnavailableException("down") : inner.ListAsync(ct);
        public Task<Stream?> OpenAsync(string path, CancellationToken ct = default) =>
            Down ? throw new FileStoreUnavailableException("down") : inner.OpenAsync(path, ct);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
