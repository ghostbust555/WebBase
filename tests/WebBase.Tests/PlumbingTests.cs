using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using WebBase.Api.Infrastructure;

namespace WebBase.Tests;

public class PlumbingTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Config_says_development_and_google_off()
    {
        var config = await app.CreateClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.Equal("development", config.GetProperty("environment").GetString());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("googleClientId").ValueKind);
    }

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await app.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_api_path_is_a_json_404()
    {
        var response = await app.CreateClient().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Security_headers_are_on_every_response()
    {
        var response = await app.CreateClient().GetAsync("/api/health");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public void Prod_refuses_to_start_without_a_real_signing_key()
    {
        using var prod = new TestApp().WithWebHostBuilder(b =>
        {
            b.UseSetting("App:Environment", "prod");
            b.UseSetting("Jwt:Key", "");
        });
        var error = Assert.ThrowsAny<Exception>(() => prod.CreateClient());
        Assert.Contains("Jwt:Key", error.ToString());
    }
}
