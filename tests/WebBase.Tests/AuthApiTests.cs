using System.Net;
using System.Net.Http.Json;
using WebBase.Api.Contracts;

namespace WebBase.Tests;

public class AuthApiTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Register_then_me_returns_the_new_account()
    {
        var client = await app.SignedInClientAsync("Someone@Example.com ");

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");

        Assert.Equal("someone@example.com", me!.Email); // trimmed and lower-cased
        Assert.True(me.HasPassword);
        Assert.False(me.HasGoogle);
    }

    [Fact]
    public async Task Login_with_the_right_password_works_and_the_wrong_one_does_not()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await app.SignedInClientAsync(email);
        var client = app.CreateClient();

        var good = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "correct horse battery"));
        var bad = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong password"));
        var nobody = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "whatever1"));

        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        // The same answer for "no account" as for "wrong password".
        Assert.Equal((await bad.Content.ReadFromJsonAsync<ErrorResponse>())!.Error,
                     (await nobody.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }

    [Fact]
    public async Task The_same_email_cannot_register_twice()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await app.SignedInClientAsync(email);

        var again = await app.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email.ToUpperInvariant(), "another password", null));

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Short_passwords_are_refused_with_a_message()
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("short@example.com", "1234567", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("8", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }

    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        var response = await app.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_forged_token_is_401()
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.bad");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_hands_back_a_working_token()
    {
        var client = await app.SignedInClientAsync();

        var refreshed = await (await client.PostAsync("/api/auth/refresh", null)).Content.ReadFromJsonAsync<AuthResponse>();
        var fresh = app.CreateClient();
        fresh.DefaultRequestHeaders.Authorization = new("Bearer", refreshed!.Token);

        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_is_404_when_not_configured()
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("x"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
