using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WebBase.Api.Infrastructure;
using WebBase.Api.Services;

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
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private ContentService Service(bool development = false) => new(
        Options.Create(new CmsOptions { Enabled = true, ContentPath = _root }),
        Options.Create(new AppOptions { Environment = development ? "development" : "prod" }),
        new FakeEnvironment());

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
    public void Addresses_map_to_files_and_folders(string path, string title)
    {
        Assert.Equal(title, Service().GetPage(path)?.Title);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("_private")]
    [InlineData("../etc/passwd")]
    [InlineData("blog/../../x")]
    [InlineData("blog/secret")]            // a draft, in prod
    [InlineData("images")]                 // a folder with files but no pages
    public void Missing_ignored_unsafe_and_draft_pages_are_not_found(string path)
    {
        Assert.Null(Service().GetPage(path));
    }

    [Fact]
    public void Drafts_are_visible_in_development()
    {
        Assert.True(Service(development: true).GetPage("blog/secret")!.Draft);
    }

    [Fact]
    public void Front_matter_is_read_and_removed_from_the_body()
    {
        var about = Service().GetPage("about")!;
        Assert.Equal("Hello\n", about.Markdown);
        Assert.True(about.Menu);
        Assert.Equal("No front matter at all", Service().GetPage("notes/one")!.Markdown);
    }

    [Fact]
    public void A_folder_lists_its_pages_newest_first_without_drafts()
    {
        var blog = Service().GetPage("blog")!;
        Assert.Equal(["blog/newer", "blog/older"], blog.Children!.Select(c => c.Path));
    }

    [Fact]
    public void All_pages_skips_ignored_files_and_folders_without_pages()
    {
        var paths = Service().AllPages().Select(p => p.Path).ToList();
        Assert.Equal("about", paths[0]); // order: 1 comes first
        Assert.Contains("blog/older", paths);
        Assert.Contains("notes/one", paths);
        Assert.DoesNotContain(paths, p => p.Contains("private") || p.Contains("secret") || p == "images");
    }

    [Fact]
    public async Task The_api_serves_pages_and_files_when_enabled()
    {
        using var app = new TestApp().WithWebHostBuilder(b =>
        {
            b.UseSetting("Cms:Enabled", "true");
            b.UseSetting("Cms:ContentPath", _root);
        });
        var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/content/page?path=about");
        Assert.Equal("About us", page.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/content/page?path=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/content/images/cat.svg")).StatusCode);
        // The markdown files themselves (drafts included) are never served raw.
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/content/blog/secret.md")).StatusCode);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("cms").GetBoolean());
    }

    [Fact]
    public async Task The_api_is_404_when_the_cms_is_off()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/content/pages")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("cms").GetBoolean());
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = "/";
        public string WebRootPath { get; set; } = "/";
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
