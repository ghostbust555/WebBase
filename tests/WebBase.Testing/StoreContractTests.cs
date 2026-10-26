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
}
