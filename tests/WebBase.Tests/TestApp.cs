using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WebBase.Api.Contracts;
using WebBase.Testing.InMemory;

namespace WebBase.Tests;

/// <summary>
/// The whole API, started in memory with an in-memory database instead of
/// PostgreSQL (WebBase.Testing/InMemory, which keeps the same store contract).
/// Tests talk to it over HTTP exactly as the browser does. The real databases
/// are tested by their modules' own test projects.
/// </summary>
public class TestApp : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("App:Environment", "development");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-0123456789");
        // Lots of sign-ins happen in a test run; don't let the limiter fail them.
        builder.UseSetting("RateLimits:AuthPerMinute", "10000");

        builder.ConfigureServices(services =>
        {
            // Swap PostgreSQL for an in-memory database, fresh for each TestApp.
            InMemoryDatabaseModule.Register(services);
        });
    }

    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

    /// <summary>
    /// The first account ever made is an author, which would make whichever test
    /// runs first an author by accident. So the helpers below first make one
    /// throwaway account, and everyone after it is an ordinary user. (A test of
    /// the first-account rule itself uses a TestApp it hasn't called these on.)
    /// </summary>
    private async Task SeedFirstAccountAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            _seeded = true;
            (await CreateClient().PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("first-account@example.com", "correct horse battery", "First"))).EnsureSuccessStatusCode();
        }
        finally { _seedLock.Release(); }
    }

    /// <summary>A client signed in as a brand-new, ordinary (not an author) account.</summary>
    public async Task<HttpClient> SignedInClientAsync(string? email = null)
    {
        await SeedFirstAccountAsync();
        return await RegisterAsync(email);
    }

    /// <summary>A client signed in as a brand-new account that is an author.</summary>
    public async Task<HttpClient> AuthorClientAsync(string? email = null)
    {
        await SeedFirstAccountAsync();
        var client = await RegisterAsync(email);
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var user = (await users.FindByIdAsync(me!.Id))!;
        user.IsAuthor = true;
        await users.UpdateAsync(user);
        return client;
    }

    private async Task<HttpClient> RegisterAsync(string? email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email ?? $"{Guid.NewGuid():N}@example.com", "correct horse battery", "Tester"));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }
}
