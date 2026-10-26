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

    /// <summary>A client signed in as a brand-new account.</summary>
    public async Task<HttpClient> SignedInClientAsync(string? email = null)
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
