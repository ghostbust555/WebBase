using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WebBase.Api.Contracts;
using WebBase.Api.Data.Postgres;

namespace WebBase.Tests;

/// <summary>
/// The whole API, started in memory with an in-memory database instead of
/// PostgreSQL. Tests talk to it over HTTP exactly as the browser does.
///
/// EF's in-memory database doesn't enforce unique indexes or run migrations,
/// so the stores themselves are tested against real PostgreSQL instead, in
/// StoreContract/.
/// </summary>
public class TestApp : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("App:Environment", "development");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-0123456789");
        // Lots of sign-ins happen in a test run; don't let the limiter fail them.
        builder.UseSetting("RateLimits:AuthPerMinute", "10000");

        builder.ConfigureServices(services =>
        {
            // Swap PostgreSQL for an in-memory database.
            foreach (var d in services.Where(d =>
                         d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                         d.ServiceType == typeof(DbContextOptions) ||
                         d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>)).ToList())
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_databaseName));
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
