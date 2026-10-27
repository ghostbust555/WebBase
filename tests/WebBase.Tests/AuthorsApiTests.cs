using System.Net;
using System.Net.Http.Json;
using WebBase.Api.Contracts;
using WebBase.Api.Services;
using WebBase.Testing.InMemory;

namespace WebBase.Tests;

/// <summary>The author permission: who gets it, what it guards, and that taking it away is immediate.</summary>
public class AuthorsApiTests(TestApp app) : IClassFixture<TestApp>
{
    private static Task<HttpResponseMessage> Register(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "correct horse battery", null));

    [Fact]
    public async Task The_first_account_ever_is_an_author_and_the_second_is_not()
    {
        using var fresh = new TestApp(); // nothing has been registered on this one
        var client = fresh.CreateClient();

        var first = await (await Register(client, "one@example.com")).Content.ReadFromJsonAsync<AuthResponse>();
        var second = await (await Register(client, "two@example.com")).Content.ReadFromJsonAsync<AuthResponse>();

        Assert.True(first!.User.IsAuthor);
        Assert.False(second!.User.IsAuthor);
    }

    [Fact]
    public async Task Ordinary_users_are_not_authors()
    {
        var client = await app.SignedInClientAsync();
        Assert.False((await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.IsAuthor);
    }

    [Fact]
    public async Task Author_endpoints_answer_401_to_the_signed_out_403_to_users_and_200_to_authors()
    {
        var anonymous = app.CreateClient();
        var user = await app.SignedInClientAsync();
        var author = await app.AuthorClientAsync();

        foreach (var (method, url) in new[] { ("GET", "/api/authors"), ("POST", "/api/authors"), ("DELETE", $"/api/authors/{Guid.NewGuid()}") })
        {
            HttpRequestMessage Request() => new(new HttpMethod(method), url)
            { Content = method == "POST" ? JsonContent.Create(new AddAuthorRequest("x@example.com")) : null };

            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request())).StatusCode);
            var refused = await user.SendAsync(Request());
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await refused.Content.ReadFromJsonAsync<ErrorResponse>())!.Error));
            Assert.NotEqual(HttpStatusCode.Forbidden, (await author.SendAsync(Request())).StatusCode);
        }
    }

    [Fact]
    public async Task An_author_can_add_an_existing_account_by_email_and_remove_them_again()
    {
        var author = await app.AuthorClientAsync();
        var other = await app.SignedInClientAsync("Newcomer@Example.com");
        var id = (await other.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id;

        var added = await author.PostAsJsonAsync("/api/authors", new AddAuthorRequest("  newcomer@example.com "));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Contains((await author.GetFromJsonAsync<List<AuthorDto>>("/api/authors"))!, a => a.Id == id);
        Assert.True((await other.GetFromJsonAsync<UserDto>("/api/auth/me"))!.IsAuthor);

        Assert.Equal(HttpStatusCode.OK, (await author.PostAsJsonAsync("/api/authors", new AddAuthorRequest("newcomer@example.com"))).StatusCode); // twice is fine
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"/api/authors/{id}")).StatusCode);
        Assert.DoesNotContain((await author.GetFromJsonAsync<List<AuthorDto>>("/api/authors"))!, a => a.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await author.DeleteAsync($"/api/authors/{id}")).StatusCode);
    }

    [Fact]
    public async Task Adding_an_email_nobody_has_signed_up_with_is_a_404_with_a_message()
    {
        var author = await app.AuthorClientAsync();
        var response = await author.PostAsJsonAsync("/api/authors", new AddAuthorRequest("nobody-here@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("sign up", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }

    [Fact]
    public async Task Nobody_can_remove_themselves()
    {
        var author = await app.AuthorClientAsync();
        var me = await author.GetFromJsonAsync<UserDto>("/api/auth/me");

        var response = await author.DeleteAsync($"/api/authors/{me!.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await author.GetFromJsonAsync<UserDto>("/api/auth/me"))!.IsAuthor);
    }

    [Fact]
    public async Task Removing_an_author_takes_effect_on_their_very_next_request_with_the_same_token()
    {
        var boss = await app.AuthorClientAsync();
        var temp = await app.AuthorClientAsync(); // holds a token issued while they were an author
        Assert.Equal(HttpStatusCode.OK, (await temp.GetAsync("/api/authors")).StatusCode);

        var id = (await temp.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id;
        await boss.DeleteAsync($"/api/authors/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, (await temp.GetAsync("/api/authors")).StatusCode);
    }

    // ---- Auth:AuthorEmails: only for an email the provider has verified ----------

    private static AuthService Auth(InMemoryData db, string authorEmails) =>
        new(new InMemoryUserStore(db), Microsoft.Extensions.Options.Options.Create(new WebBase.Api.Infrastructure.AuthOptions { AuthorEmails = authorEmails }));

    [Fact]
    public async Task A_verified_google_email_in_AuthorEmails_becomes_an_author_but_only_after_the_first_account()
    {
        var db = new InMemoryData();
        var auth = Auth(db, "Boss@Example.com, other@example.com");
        await auth.RegisterAsync("first@example.com", "correct horse battery", null); // takes the "first account" slot

        var listed = (await auth.SignInWithGoogleAsync(new("g-1", "boss@example.com", true, null))).User!;
        var unlisted = (await auth.SignInWithGoogleAsync(new("g-2", "someone@example.com", true, null))).User!;

        Assert.True(listed.IsAuthor);
        Assert.False(unlisted.IsAuthor);
    }

    [Fact]
    public async Task A_password_sign_up_with_a_listed_email_does_not_become_an_author()
    {
        var db = new InMemoryData();
        var auth = Auth(db, "boss@example.com");
        await auth.RegisterAsync("first@example.com", "correct horse battery", null);

        // Anyone can type any address into a sign-up form: it proves nothing.
        var squatter = (await auth.RegisterAsync("boss@example.com", "correct horse battery", null)).User!;

        Assert.False(squatter.IsAuthor);
    }

    [Fact]
    public async Task A_listed_password_account_becomes_an_author_when_it_later_signs_in_with_verified_google()
    {
        var db = new InMemoryData();
        var auth = Auth(db, "boss@example.com");
        await auth.RegisterAsync("first@example.com", "correct horse battery", null);
        var account = (await auth.RegisterAsync("boss@example.com", "correct horse battery", null)).User!;
        Assert.False(account.IsAuthor);

        var viaGoogle = (await auth.SignInWithGoogleAsync(new("g-3", "boss@example.com", true, null))).User!;
        Assert.Equal(account.Id, viaGoogle.Id);
        Assert.True(viaGoogle.IsAuthor);

        // ...and a Google sign-in the next day finds the flag saved.
        Assert.True((await new InMemoryUserStore(db).FindByIdAsync(account.Id))!.IsAuthor);
    }

    [Fact]
    public async Task The_first_google_account_ever_is_an_author_too()
    {
        var auth = Auth(new InMemoryData(), "");
        var first = (await auth.SignInWithGoogleAsync(new("g-9", "a@example.com", true, null))).User!;
        var second = (await auth.SignInWithGoogleAsync(new("g-10", "b@example.com", true, null))).User!;
        Assert.True(first.IsAuthor);
        Assert.False(second.IsAuthor);
    }
}
