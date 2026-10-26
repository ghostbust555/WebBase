using Microsoft.EntityFrameworkCore;
using WebBase.Api.Data;
using WebBase.Api.Services;

namespace WebBase.Tests;

/// <summary>The rules for matching a Google sign-in to an account (AuthService), with no Google involved.</summary>
public class GoogleSignInTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task A_new_google_account_makes_a_passwordless_user()
    {
        await using var db = NewDb();
        var result = await new AuthService(db).SignInWithGoogleAsync(new("sub-1", "New@Example.com", true, "New Person"));

        Assert.NotNull(result.User);
        Assert.Equal("new@example.com", result.User!.Email);
        Assert.Null(result.User.PasswordHash);
        Assert.Equal("New Person", result.User.DisplayName);
    }

    [Fact]
    public async Task A_verified_email_links_to_the_existing_account_and_keeps_its_password()
    {
        await using var db = NewDb();
        var auth = new AuthService(db);
        var original = (await auth.RegisterAsync("me@example.com", "my password", "Me")).User!;

        var viaGoogle = (await auth.SignInWithGoogleAsync(new("sub-2", "me@example.com", true, "Me"))).User!;

        Assert.Equal(original.Id, viaGoogle.Id);
        Assert.NotNull((await auth.LoginAsync("me@example.com", "my password")).User);
    }

    [Fact]
    public async Task An_unverified_email_is_never_matched_to_an_existing_account()
    {
        await using var db = NewDb();
        var auth = new AuthService(db);
        await auth.RegisterAsync("victim@example.com", "their password", null);

        var result = await auth.SignInWithGoogleAsync(new("attacker", "victim@example.com", false, null));

        Assert.Null(result.User);
    }

    [Fact]
    public async Task The_google_account_wins_even_if_its_email_changed()
    {
        await using var db = NewDb();
        var auth = new AuthService(db);
        var first = (await auth.SignInWithGoogleAsync(new("sub-3", "old@example.com", true, null))).User!;

        var later = (await auth.SignInWithGoogleAsync(new("sub-3", "new@example.com", true, null))).User!;

        Assert.Equal(first.Id, later.Id);
    }
}
