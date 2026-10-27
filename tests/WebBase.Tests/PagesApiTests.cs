using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using WebBase.Api.Contracts;
using WebBase.Api.Infrastructure;
using WebBase.Api.Pages;

namespace WebBase.Tests;

/// <summary>The authors' pages (docs/PAGES.md): writing them, who can see drafts, and the rules.</summary>
public class PagesApiTests(TestApp app) : IClassFixture<TestApp>
{
    private static SavePageRequest Draft(string title = "About us", string? path = null, string body = "Hello *world*") =>
        new(title, path, body, ShowInMenu: false, Order: 0, Published: false);

    private static async Task<PageDto> Create(HttpClient author, SavePageRequest request)
    {
        var response = await author.PostAsJsonAsync("/api/editor/pages", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PageDto>())!;
    }

    private static string Unique(string word = "page") => $"{word}-{Guid.NewGuid():N}";

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Error;

    [Fact]
    public async Task An_author_can_write_publish_edit_unpublish_and_delete_a_page()
    {
        var author = await app.AuthorClientAsync();
        var path = Unique();

        var page = await Create(author, Draft("Welcome", path, "# Hi"));
        Assert.False(page.Published);
        Assert.Equal(path, page.Path);

        // Publish, keeping the same page.
        var published = await (await author.PutAsJsonAsync($"/api/editor/pages/{page.Id}",
            Draft("Welcome", path, "# Hi there") with { Published = true })).Content.ReadFromJsonAsync<PageDto>();
        Assert.True(published!.Published);
        Assert.Equal("# Hi there", published.Body);
        var firstPublished = published.PublishedAtUtc;

        // Saving it again doesn't move the published time.
        var again = await (await author.PutAsJsonAsync($"/api/editor/pages/{page.Id}",
            Draft("Welcome!", path, "# Hi there") with { Published = true })).Content.ReadFromJsonAsync<PageDto>();
        Assert.Equal(firstPublished, again!.PublishedAtUtc);

        var visitor = app.CreateClient();
        var shown = await visitor.GetFromJsonAsync<PublicPageDto>($"/api/pages/published?path={path}");
        Assert.Equal("Welcome!", shown!.Title);
        Assert.Equal("# Hi there", shown.Markdown);

        // Unpublish: gone for visitors, still there for the author.
        await author.PutAsJsonAsync($"/api/editor/pages/{page.Id}", Draft("Welcome!", path) with { Published = false });
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/pages/published?path={path}")).StatusCode);
        Assert.Contains((await author.GetFromJsonAsync<List<PageSummaryDto>>("/api/editor/pages"))!, p => p.Id == page.Id && !p.Published);

        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"/api/editor/pages/{page.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/editor/pages/{page.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.DeleteAsync($"/api/editor/pages/{page.Id}")).StatusCode);
    }

    [Fact]
    public async Task Drafts_are_invisible_to_visitors_and_to_the_menu()
    {
        var author = await app.AuthorClientAsync();
        var path = Unique("secret");
        await Create(author, Draft("Secret plans", path) with { ShowInMenu = true });

        var visitor = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/pages/published?path={path}")).StatusCode);
        Assert.DoesNotContain((await visitor.GetFromJsonAsync<List<MenuItemDto>>("/api/pages/menu"))!, m => m.Path == path);
        // Not even for a signed-in non-author.
        var user = await app.SignedInClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/pages/published?path={path}")).StatusCode);
    }

    [Fact]
    public async Task The_menu_lists_published_pages_marked_for_it_in_order()
    {
        // Its own app: the menu is the whole site's, so other tests' pages mustn't be in it.
        using var fresh = new TestApp();
        var author = await fresh.AuthorClientAsync();
        await Create(author, Draft("Zebra", "zebra") with { ShowInMenu = true, Order = 1, Published = true });
        await Create(author, Draft("Apple", "apple") with { ShowInMenu = true, Order = 2, Published = true });
        await Create(author, Draft("Mango", "mango") with { ShowInMenu = true, Order = 1, Published = true });
        await Create(author, Draft("Hidden", "hidden") with { ShowInMenu = false, Published = true });
        await Create(author, Draft("Unpublished", "unpublished") with { ShowInMenu = true, Order = 0 });

        var menu = await fresh.CreateClient().GetFromJsonAsync<List<MenuItemDto>>("/api/pages/menu");

        // By order, then by title.
        Assert.Equal(["mango", "zebra", "apple"], menu!.Select(m => m.Path).ToArray());
    }

    [Fact]
    public async Task The_path_is_made_from_the_title_when_empty()
    {
        var author = await app.AuthorClientAsync();
        var title = $"Café & the \"Big\" Sale {Guid.NewGuid():N}";
        var page = await Create(author, Draft(title, path: "  "));
        Assert.Matches(new Regex("^cafe-the-big-sale-[0-9a-f]{32}$"), page.Path);
    }

    [Fact]
    public async Task A_path_that_cannot_be_made_is_a_400_with_a_message()
    {
        var author = await app.AuthorClientAsync();
        var response = await author.PostAsJsonAsync("/api/editor/pages", Draft("日本語", path: null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("address", await ErrorOf(response));
    }

    [Theory]
    [InlineData("Has Spaces")]
    [InlineData("under/../score")]
    [InlineData("a/b/c/d/e")]
    [InlineData("with.dot")]
    [InlineData("")]
    public async Task Bad_addresses_are_refused(string path)
    {
        var author = await app.AuthorClientAsync();
        // An empty path is made from the title, so give that a title with nothing to make one from.
        var response = await author.PostAsJsonAsync("/api/editor/pages", Draft(path.Length == 0 ? "???" : "Title", path));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await ErrorOf(response)));
    }

    [Fact]
    public async Task A_duplicate_path_is_a_409_with_a_human_message_on_create_and_on_save()
    {
        var author = await app.AuthorClientAsync();
        var path = Unique();
        await Create(author, Draft(path: path));

        var dup = await author.PostAsJsonAsync("/api/editor/pages", Draft("Another", path));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Contains("already uses that address", await ErrorOf(dup));

        var other = await Create(author, Draft("Other", Unique()));
        var move = await author.PutAsJsonAsync($"/api/editor/pages/{other.Id}", Draft("Other", path));
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);

        // The path is compared after cleaning, so case and slashes can't sneak round it.
        var sneaky = await author.PostAsJsonAsync("/api/editor/pages", Draft("Sneaky", $"/{path.ToUpperInvariant()}/"));
        Assert.Equal(HttpStatusCode.Conflict, sneaky.StatusCode);
    }

    [Fact]
    public async Task The_apps_own_addresses_cannot_be_taken()
    {
        var author = await app.AuthorClientAsync();
        foreach (var word in new[] { "login", "docs", "write", "api", "content", "register", "account", "authors", "index", "docs/anything" })
        {
            var response = await author.PostAsJsonAsync("/api/editor/pages", Draft("Mine", word));
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"/{word} should be reserved but gave {response.StatusCode}");
            Assert.Contains("site itself", await ErrorOf(response));
        }
    }

    [Fact]
    public async Task Every_page_of_the_client_router_is_reserved()
    {
        // client/src/router.ts lists the app's own pages; a new one must be reserved too, or a
        // database page could be written that the router never lets a visitor reach.
        var router = File.ReadAllText(Path.Combine(RepoRoot(), "client/src/router.ts"));
        var firstWords = Regex.Matches(router, @"path:\s*'/([a-z0-9-]*)")
            .Select(m => m.Groups[1].Value).Where(w => w.Length > 0).Distinct().ToList();
        Assert.Contains("login", firstWords); // the pattern still finds routes
        Assert.All(firstWords, word => Assert.Contains(word, PageRules.Reserved));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "compose.dev.yaml"))) return dir.FullName;
        throw new InvalidOperationException("Can't find the repository root");
    }

    [Fact]
    public async Task Titles_and_text_have_limits()
    {
        var author = await app.AuthorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsJsonAsync("/api/editor/pages", Draft("   ", Unique()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await author.PostAsJsonAsync("/api/editor/pages", Draft(new string('t', PageRules.MaxTitle + 1), Unique()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await author.PostAsJsonAsync("/api/editor/pages", Draft(path: Unique(), body: new string('x', PageRules.MaxBody + 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await author.PostAsJsonAsync("/api/editor/pages", Draft(path: Unique(), body: "bell\u0007"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await author.PostAsJsonAsync("/api/editor/pages", Draft(path: Unique()) with { Order = 1_000_000 })).StatusCode);

        // Exactly at the limit is fine.
        var full = await author.PostAsJsonAsync("/api/editor/pages", Draft(path: Unique(), body: new string('x', PageRules.MaxBody)));
        Assert.Equal(HttpStatusCode.Created, full.StatusCode);
    }

    [Fact]
    public void The_biggest_page_fits_the_request_body_limit()
    {
        // A character is at most 3 bytes of UTF-8 (control characters are refused), so the largest
        // valid request is this many bytes; Abuse:MaxBodyKb must allow it, or a long page could be
        // written in the editor but never saved.
        var limit = new AbuseOptions().MaxBodyKb * 1024;
        Assert.True(PageRules.MaxRequestBytes <= limit, $"A full page can need {PageRules.MaxRequestBytes} bytes but Abuse:MaxBodyKb allows {limit}.");
        Assert.True(PageRules.MaxRequestBytes > limit / 2, "MaxBody has drifted far below what Abuse:MaxBodyKb allows: raise one or lower the other on purpose.");
    }

    [Fact]
    public async Task The_biggest_page_in_the_widest_characters_goes_through_the_real_request_limit()
    {
        // The same arithmetic, but measured: 80,000 three-byte characters really is under 256 KB as JSON.
        var body = new string('語', PageRules.MaxBody);
        var json = System.Text.Json.JsonSerializer.Serialize(Draft("Wide", Unique(), body), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= new AbuseOptions().MaxBodyKb * 1024);
        var author = await app.AuthorClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await author.PostAsJsonAsync("/api/editor/pages", Draft("Wide", Unique(), body))).StatusCode);
    }

    [Fact]
    public async Task Only_authors_can_use_the_editor_api()
    {
        var anonymous = app.CreateClient();
        var user = await app.SignedInClientAsync();
        var author = await app.AuthorClientAsync();
        var page = await Create(author, Draft(path: Unique()));

        var calls = new (string Method, string Url, object? Body)[]
        {
            ("GET", "/api/editor/pages", null),
            ("GET", $"/api/editor/pages/{page.Id}", null),
            ("POST", "/api/editor/pages", Draft(path: Unique())),
            ("PUT", $"/api/editor/pages/{page.Id}", Draft(path: page.Path)),
            ("DELETE", $"/api/editor/pages/{page.Id}", null),
        };
        foreach (var (method, url, body) in calls)
        {
            HttpRequestMessage Request() => new(new HttpMethod(method), url) { Content = body == null ? null : JsonContent.Create(body) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.SendAsync(Request())).StatusCode);
        }
        // The refused delete deleted nothing.
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/api/editor/pages/{page.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_missing_page_is_a_404()
    {
        var author = await app.AuthorClientAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/editor/pages/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.PutAsJsonAsync($"/api/editor/pages/{id}", Draft(path: Unique()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient().GetAsync("/api/pages/published?path=")).StatusCode);
    }

    [Fact]
    public async Task The_editor_records_who_saved_last_and_when()
    {
        var author = await app.AuthorClientAsync();
        var me = await author.GetFromJsonAsync<UserDto>("/api/auth/me");
        var before = DateTime.UtcNow.AddSeconds(-1);
        var page = await Create(author, Draft(path: Unique()));
        Assert.Equal(me!.Id, page.UpdatedById);
        Assert.True(page.UpdatedAtUtc > before);
    }

    [Fact]
    public async Task A_page_whose_author_loses_the_role_stays()
    {
        var boss = await app.AuthorClientAsync();
        var temp = await app.AuthorClientAsync();
        var page = await Create(temp, Draft("Written by temp", Unique()) with { Published = true });
        var tempId = (await temp.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id;

        await boss.DeleteAsync($"/api/authors/{tempId}");

        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync($"/api/pages/published?path={page.Path}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await temp.PostAsJsonAsync("/api/editor/pages", Draft(path: Unique()))).StatusCode);
    }

    // ---- Files from the CMS win ---------------------------------------------

    [Fact]
    public async Task A_file_in_the_cms_wins_so_no_page_can_be_made_at_its_address()
    {
        var dir = Directory.CreateTempSubdirectory("webbase-pages-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "taken.md"), "---\ntitle: From a file\n---\nFile text");
            Directory.CreateDirectory(Path.Combine(dir, "blog"));
            File.WriteAllText(Path.Combine(dir, "blog", "post.md"), "A post");

            using var withFiles = new TestApp().WithWebHostBuilder(b =>
            {
                b.UseSetting("Cms:Enabled", "true");
                b.UseSetting("Cms:ContentPath", dir);
                b.UseSetting("Cms:RefreshSeconds", "0");
            });
            var client = withFiles.CreateClient();
            // The first account is an author (docs/AUTHORS.md).
            var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("boss@example.com", "correct horse battery", null));
            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.Token);

            foreach (var path in new[] { "taken", "blog" }) // a file page, and a folder the files list
            {
                var refused = await client.PostAsJsonAsync("/api/editor/pages", Draft("Mine", path));
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                Assert.Contains("file", await ErrorOf(refused));
            }

            // Elsewhere is fine, including beside a file's folder.
            var fine = await client.PostAsJsonAsync("/api/editor/pages", Draft("Mine", "blog-extra"));
            Assert.Equal(HttpStatusCode.Created, fine.StatusCode);
            var page = (await fine.Content.ReadFromJsonAsync<PageDto>())!;

            // Moving a page onto a file's address is refused the same way...
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PutAsJsonAsync($"/api/editor/pages/{page.Id}", Draft("Mine", "taken"))).StatusCode);
            // ...but a page can always be saved where it already is.
            Assert.Equal(HttpStatusCode.OK,
                (await client.PutAsJsonAsync($"/api/editor/pages/{page.Id}", Draft("Mine again", "blog-extra"))).StatusCode);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Pages_work_with_the_cms_switched_off()
    {
        // The default: no CMS, and the authors' pages still work.
        var author = await app.AuthorClientAsync();
        var page = await Create(author, Draft("No cms needed", Unique()) with { Published = true });
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync($"/api/pages/published?path={page.Path}")).StatusCode);
    }
}
