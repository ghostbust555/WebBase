using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebBase.Data;
using WebBase.Models;
using WebBase.Modules;

namespace WebBase.Testing;

/// <summary>
/// What every database module must do, written once. Each module's test
/// project has a small subclass that names the module and says how to make an
/// empty database for it (tests/WebBase.Database.Postgres.Tests/PostgresStoreTests.cs);
/// every test below then runs against it. A new database module
/// (docs/DATABASE.md) is finished when these pass.
///
/// The stores are built exactly as the app builds them (the module's Register),
/// and each Store&lt;T&gt;() call is a fresh scope, the way two separate
/// requests would see the database.
/// </summary>
public abstract class StoreContractTests : IAsyncLifetime
{
    private ServiceProvider? _services;
    private readonly List<IServiceScope> _scopes = [];

    /// <summary>The module under test.</summary>
    protected abstract IDatabaseModule Module { get; }

    /// <summary>
    /// Make a new, empty database and return the settings that point the
    /// module at it (its connection settings).
    /// Call Assert.Skip if this implementation can't be tested here.
    /// </summary>
    protected abstract Task<Dictionary<string, string?>> CreateDatabaseAsync();

    protected abstract Task DropDatabaseAsync();

    public async ValueTask InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(await CreateDatabaseAsync()).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        Module.Register(services, config);
        _services = services.BuildServiceProvider();
        await Store<IDatabase>().PrepareAsync();
        // Preparing must be safe to repeat: it runs at every startup.
        await Store<IDatabase>().PrepareAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var scope in _scopes) scope.Dispose();
        if (_services != null) await _services.DisposeAsync();
        await DropDatabaseAsync();
        GC.SuppressFinalize(this);
    }

    protected T Store<T>() where T : notnull
    {
        var scope = _services!.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    private static User NewUser(string? email = null, string? googleSubject = null) => new()
    {
        Email = email ?? $"{Guid.NewGuid():N}@example.com",
        DisplayName = "Someone",
        PasswordHash = "hash",
        GoogleSubject = googleSubject,
    };

    private static void SameUser(User expected, User? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual!.Id);
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.PasswordHash, actual.PasswordHash);
        Assert.Equal(expected.GoogleSubject, actual.GoogleSubject);
        Assert.Equal(expected.IsAuthor, actual.IsAuthor);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, actual.CreatedAtUtc.Kind);
    }

    // ---- Users -------------------------------------------------------------

    [Fact]
    public async Task A_saved_user_can_be_found_by_id_email_and_google_subject()
    {
        var user = NewUser(googleSubject: "google-1");
        await Store<IUserStore>().AddAsync(user);

        SameUser(user, await Store<IUserStore>().FindByIdAsync(user.Id));
        SameUser(user, await Store<IUserStore>().FindByEmailAsync(user.Email));
        SameUser(user, await Store<IUserStore>().FindByGoogleSubjectAsync("google-1"));
    }

    [Fact]
    public async Task Any_is_false_in_an_empty_database()
    {
        Assert.False(await Store<IUserStore>().AnyAsync());
        Assert.Empty(await Store<IUserStore>().ListAuthorsAsync());
    }

    [Fact]
    public async Task IsAuthor_is_saved_and_can_be_changed()
    {
        var user = NewUser();
        user.IsAuthor = true;
        await Store<IUserStore>().AddAsync(user);
        SameUser(user, await Store<IUserStore>().FindByIdAsync(user.Id));

        var found = await Store<IUserStore>().FindByIdAsync(user.Id);
        found!.IsAuthor = false;
        await Store<IUserStore>().UpdateAsync(found);
        SameUser(found, await Store<IUserStore>().FindByIdAsync(user.Id));
    }

    [Fact]
    public async Task Any_is_true_once_a_user_exists()
    {
        await Store<IUserStore>().AddAsync(NewUser());
        Assert.True(await Store<IUserStore>().AnyAsync());
    }

    [Fact]
    public async Task ListAuthors_returns_only_authors_oldest_first()
    {
        var users = Store<IUserStore>();
        var older = NewUser(); older.IsAuthor = true; older.CreatedAtUtc = DateTime.UtcNow.AddDays(-2);
        var newer = NewUser(); newer.IsAuthor = true; newer.CreatedAtUtc = DateTime.UtcNow.AddDays(-1);
        var reader = NewUser();
        await users.AddAsync(newer);
        await users.AddAsync(reader);
        await users.AddAsync(older);

        // Other tests may have left authors in a shared database: look only at ours.
        var mine = (await users.ListAuthorsAsync()).Where(u => u.Id == older.Id || u.Id == newer.Id || u.Id == reader.Id).ToList();
        Assert.Equal([older.Id, newer.Id], mine.Select(u => u.Id).ToArray());
        SameUser(older, mine[0]);

        // Taking the author flag away takes the user off the list.
        var demoted = await users.FindByIdAsync(newer.Id);
        demoted!.IsAuthor = false;
        await users.UpdateAsync(demoted);
        Assert.DoesNotContain(await users.ListAuthorsAsync(), u => u.Id == newer.Id);
    }

    [Fact]
    public async Task Finding_nobody_returns_null()
    {
        var users = Store<IUserStore>();
        Assert.Null(await users.FindByIdAsync(Guid.NewGuid()));
        Assert.Null(await users.FindByEmailAsync("nobody@example.com"));
        Assert.Null(await users.FindByGoogleSubjectAsync("nobody"));
    }

    [Fact]
    public async Task Two_users_cannot_share_an_email()
    {
        var first = NewUser();
        await Store<IUserStore>().AddAsync(first);
        await Assert.ThrowsAsync<DuplicateKeyException>(() => Store<IUserStore>().AddAsync(NewUser(first.Email)));
    }

    [Fact]
    public async Task Two_users_cannot_share_a_google_subject_but_many_can_have_none()
    {
        await Store<IUserStore>().AddAsync(NewUser(googleSubject: "google-2"));
        await Assert.ThrowsAsync<DuplicateKeyException>(() => Store<IUserStore>().AddAsync(NewUser(googleSubject: "google-2")));

        await Store<IUserStore>().AddAsync(NewUser());
        await Store<IUserStore>().AddAsync(NewUser()); // no Google subject twice is fine
    }

    [Fact]
    public async Task Racing_registrations_for_one_email_let_exactly_one_through()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            try { await Store<IUserStore>().AddAsync(NewUser(email)); return true; }
            catch (DuplicateKeyException) { return false; }
        });
        Assert.Single(await Task.WhenAll(attempts), won => won);
    }

    [Fact]
    public async Task Update_saves_every_field()
    {
        var user = NewUser();
        await Store<IUserStore>().AddAsync(user);

        var found = (await Store<IUserStore>().FindByIdAsync(user.Id))!;
        found.DisplayName = "Renamed";
        found.GoogleSubject = "google-3";
        found.PasswordHash = null;
        found.Email = $"new-{user.Email}";
        await Store<IUserStore>().UpdateAsync(found);

        SameUser(found, await Store<IUserStore>().FindByIdAsync(user.Id));
        Assert.Null(await Store<IUserStore>().FindByEmailAsync(user.Email));
    }

    [Fact]
    public async Task Update_cannot_take_someone_elses_email()
    {
        var mine = NewUser();
        var theirs = NewUser();
        await Store<IUserStore>().AddAsync(mine);
        await Store<IUserStore>().AddAsync(theirs);

        mine.Email = theirs.Email;
        await Assert.ThrowsAsync<DuplicateKeyException>(() => Store<IUserStore>().UpdateAsync(mine));
    }

    [Fact]
    public async Task Updating_a_missing_user_is_an_error()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Store<IUserStore>().UpdateAsync(NewUser()));
    }

    [Fact]
    public async Task Changing_a_returned_user_saves_nothing_until_update()
    {
        var users = Store<IUserStore>();
        var notes = Store<INoteStore>();
        var user = NewUser();
        await users.AddAsync(user);
        var found = (await users.FindByIdAsync(user.Id))!;

        user.DisplayName = "Changed after add";
        found.DisplayName = "Changed after find";
        // Another write through the same scope must not carry those changes along.
        await notes.AddAsync(new Note { UserId = user.Id, Text = "hello" });
        await users.AddAsync(NewUser());

        Assert.Equal("Someone", (await Store<IUserStore>().FindByIdAsync(user.Id))!.DisplayName);
    }

    // ---- Notes -------------------------------------------------------------

    private async Task<User> SavedUser()
    {
        var user = NewUser();
        await Store<IUserStore>().AddAsync(user);
        return user;
    }

    [Fact]
    public async Task Notes_list_newest_first_and_only_the_owners()
    {
        var me = await SavedUser();
        var someoneElse = await SavedUser();
        var now = DateTime.UtcNow;
        await Store<INoteStore>().AddAsync(new Note { UserId = me.Id, Text = "older", CreatedAtUtc = now.AddMinutes(-5) });
        await Store<INoteStore>().AddAsync(new Note { UserId = me.Id, Text = "newer", CreatedAtUtc = now });
        await Store<INoteStore>().AddAsync(new Note { UserId = someoneElse.Id, Text = "not mine", CreatedAtUtc = now.AddMinutes(1) });

        var mine = await Store<INoteStore>().ListAsync(me.Id);
        Assert.Equal(["newer", "older"], mine.Select(n => n.Text));
        Assert.All(mine, n => Assert.Equal(DateTimeKind.Utc, n.CreatedAtUtc.Kind));
        Assert.Equal(now, mine[0].CreatedAtUtc, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_user_with_no_notes_gets_an_empty_list()
    {
        Assert.Empty(await Store<INoteStore>().ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Removing_works_only_for_the_owner()
    {
        var me = await SavedUser();
        var someoneElse = await SavedUser();
        var note = new Note { UserId = me.Id, Text = "mine" };
        await Store<INoteStore>().AddAsync(note);

        Assert.False(await Store<INoteStore>().RemoveAsync(someoneElse.Id, note.Id));
        Assert.Single(await Store<INoteStore>().ListAsync(me.Id));

        Assert.True(await Store<INoteStore>().RemoveAsync(me.Id, note.Id));
        Assert.Empty(await Store<INoteStore>().ListAsync(me.Id));
        Assert.False(await Store<INoteStore>().RemoveAsync(me.Id, note.Id));
    }

    // ---- The database ------------------------------------------------------

    [Fact]
    public async Task It_reports_that_it_can_connect()
    {
        Assert.True(await Store<IDatabase>().CanConnectAsync());
    }

    // ---- Pages (site-owned: no owner id) -----------------------------------

    private static Page NewPage(string? path = null, bool published = false)
    {
        var now = DateTime.UtcNow;
        return new Page
        {
            Path = path ?? $"page-{Guid.NewGuid():N}",
            Title = "A title",
            Body = "# Hello\n\nSome *markdown*, with \"quotes\", unicode ✓ and 日本語.",
            ShowInMenu = true,
            Order = 3,
            PublishedAtUtc = published ? now : null,
            CreatedAtUtc = now.AddMinutes(-1),
            UpdatedAtUtc = now,
            UpdatedById = Guid.NewGuid(),
        };
    }

    private static void SamePage(Page expected, Page? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual!.Id);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Body, actual.Body);
        Assert.Equal(expected.ShowInMenu, actual.ShowInMenu);
        Assert.Equal(expected.Order, actual.Order);
        Assert.Equal(expected.UpdatedById, actual.UpdatedById);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, actual.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, actual.UpdatedAtUtc.Kind);
        Assert.Equal(expected.PublishedAtUtc.HasValue, actual.PublishedAtUtc.HasValue);
        if (expected.PublishedAtUtc is { } published)
        {
            Assert.Equal(published, actual.PublishedAtUtc!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Equal(DateTimeKind.Utc, actual.PublishedAtUtc.Value.Kind);
        }
    }

    [Fact]
    public async Task A_saved_page_can_be_found_by_id_and_by_path()
    {
        var page = NewPage(published: true);
        await Store<IPageStore>().AddAsync(page);

        SamePage(page, await Store<IPageStore>().FindByIdAsync(page.Id));
        SamePage(page, await Store<IPageStore>().FindByPathAsync(page.Path));
        Assert.Null(await Store<IPageStore>().FindByIdAsync(Guid.NewGuid()));
        Assert.Null(await Store<IPageStore>().FindByPathAsync("no/such/page"));
    }

    [Fact]
    public async Task A_draft_has_no_published_time_and_is_left_out_of_the_published_list()
    {
        var draft = NewPage();
        var live = NewPage("blog/live", published: true);
        await Store<IPageStore>().AddAsync(draft);
        await Store<IPageStore>().AddAsync(live);

        SamePage(draft, await Store<IPageStore>().FindByIdAsync(draft.Id));
        Assert.Null((await Store<IPageStore>().FindByIdAsync(draft.Id))!.PublishedAtUtc);
        Assert.Equal(new[] { draft.Id, live.Id }.Order(), (await Store<IPageStore>().ListAsync()).Select(p => p.Id).Order());
        Assert.Equal([live.Id], (await Store<IPageStore>().ListPublishedAsync()).Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task Pages_can_be_updated_published_unpublished_and_moved_to_a_new_path()
    {
        var page = NewPage("first");
        await Store<IPageStore>().AddAsync(page);

        var found = await Store<IPageStore>().FindByIdAsync(page.Id);
        found!.Title = "Changed";
        found.Path = "second";
        found.PublishedAtUtc = DateTime.UtcNow;
        found.ShowInMenu = false;
        found.Order = -4;
        await Store<IPageStore>().UpdateAsync(found);

        SamePage(found, await Store<IPageStore>().FindByIdAsync(page.Id));
        Assert.Null(await Store<IPageStore>().FindByPathAsync("first"));
        Assert.Single(await Store<IPageStore>().ListPublishedAsync());

        found.PublishedAtUtc = null;
        await Store<IPageStore>().UpdateAsync(found);
        Assert.Empty(await Store<IPageStore>().ListPublishedAsync());
    }

    [Fact]
    public async Task Two_pages_cannot_share_a_path()
    {
        await Store<IPageStore>().AddAsync(NewPage("taken"));
        await Assert.ThrowsAsync<DuplicateKeyException>(() => Store<IPageStore>().AddAsync(NewPage("taken")));

        var other = NewPage("free");
        await Store<IPageStore>().AddAsync(other);
        var found = await Store<IPageStore>().FindByIdAsync(other.Id);
        found!.Path = "taken";
        await Assert.ThrowsAsync<DuplicateKeyException>(() => Store<IPageStore>().UpdateAsync(found));

        // The failed update changed nothing.
        Assert.Equal("free", (await Store<IPageStore>().FindByIdAsync(other.Id))!.Path);
    }

    [Fact]
    public async Task A_page_can_be_saved_again_without_changing_its_path()
    {
        var page = NewPage("same");
        await Store<IPageStore>().AddAsync(page);
        var found = await Store<IPageStore>().FindByIdAsync(page.Id);
        found!.Body = "new text";
        await Store<IPageStore>().UpdateAsync(found); // its own path is not a duplicate
        Assert.Equal("new text", (await Store<IPageStore>().FindByIdAsync(page.Id))!.Body);
    }

    [Fact]
    public async Task Updating_a_missing_page_throws_KeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Store<IPageStore>().UpdateAsync(NewPage()));
    }

    [Fact]
    public async Task Removing_a_page_frees_its_path_and_removing_it_twice_is_false()
    {
        var page = NewPage("gone");
        await Store<IPageStore>().AddAsync(page);

        Assert.True(await Store<IPageStore>().RemoveAsync(page.Id));
        Assert.False(await Store<IPageStore>().RemoveAsync(page.Id));
        Assert.Null(await Store<IPageStore>().FindByPathAsync("gone"));
        await Store<IPageStore>().AddAsync(NewPage("gone")); // free to use again
    }

    [Fact]
    public async Task Reads_of_pages_return_copies()
    {
        var page = NewPage("copy");
        await Store<IPageStore>().AddAsync(page);

        var found = await Store<IPageStore>().FindByIdAsync(page.Id);
        found!.Title = "changed but not saved";

        Assert.Equal("A title", (await Store<IPageStore>().FindByIdAsync(page.Id))!.Title);
    }
}
