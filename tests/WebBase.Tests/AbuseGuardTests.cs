using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using WebBase.Api.Infrastructure;

namespace WebBase.Tests;

/// <summary>Too many requests from one address get it banned; a flood from many is capped in total.</summary>
public class AbuseGuardTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (AbuseTracker, Clock) Tracker(AbuseOptions options)
    {
        var clock = new Clock();
        return (new AbuseTracker(options, clock, NullLogger<AbuseTracker>.Instance), clock);
    }

    [Fact]
    public void An_address_over_the_limit_is_banned_until_the_ban_ends()
    {
        var (tracker, clock) = Tracker(new AbuseOptions { PerIpPerMinute = 3, BanMinutes = 10 });
        for (var i = 0; i < 3; i++) Assert.Equal(Verdict.Allowed, tracker.Check("1.1.1.1"));
        Assert.Equal(Verdict.Banned, tracker.Check("1.1.1.1"));

        clock.Now += TimeSpan.FromMinutes(9);
        Assert.Equal(Verdict.Banned, tracker.Check("1.1.1.1"));      // still inside the ban
        Assert.InRange(tracker.RetryAfter("1.1.1.1"), 1, 60);

        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(Verdict.Allowed, tracker.Check("1.1.1.1"));     // the ban is over
    }

    [Fact]
    public void Other_addresses_are_not_affected_by_a_ban()
    {
        var (tracker, _) = Tracker(new AbuseOptions { PerIpPerMinute = 2 });
        for (var i = 0; i < 3; i++) tracker.Check("1.1.1.1");
        Assert.Equal(Verdict.Allowed, tracker.Check("2.2.2.2"));
    }

    [Fact]
    public void The_count_starts_over_every_minute()
    {
        var (tracker, clock) = Tracker(new AbuseOptions { PerIpPerMinute = 3 });
        for (var minute = 0; minute < 5; minute++)
        {
            for (var i = 0; i < 3; i++) Assert.Equal(Verdict.Allowed, tracker.Check("1.1.1.1"));
            clock.Now += TimeSpan.FromMinutes(1);
        }
    }

    [Fact]
    public void A_flood_from_many_addresses_hits_the_total_cap_without_banning_anyone()
    {
        var (tracker, clock) = Tracker(new AbuseOptions { PerIpPerMinute = 100, TotalPerMinute = 10 });
        for (var i = 0; i < 10; i++) Assert.Equal(Verdict.Allowed, tracker.Check($"10.0.0.{i}"));
        Assert.Equal(Verdict.Busy, tracker.Check("10.0.0.50"));
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(Verdict.Allowed, tracker.Check("10.0.0.50"));   // nobody was banned for it
    }

    [Fact]
    public void Memory_is_bounded_when_addresses_never_repeat()
    {
        var (tracker, _) = Tracker(new AbuseOptions { MaxTracked = 5, TotalPerMinute = 0 });
        for (var i = 0; i < 1000; i++) Assert.Equal(Verdict.Allowed, tracker.Check($"10.1.{i / 250}.{i % 250}"));
    }

    private static WebApplicationFactory<Program> AppWith(params (string, string)[] settings)
    {
        var app = new TestApp();
        return app.WithWebHostBuilder(b =>
        {
            b.UseSetting("Abuse:Enabled", "true");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });
    }

    [Fact]
    public async Task Over_the_limit_the_site_answers_429_with_retry_after()
    {
        using var app = AppWith(("Abuse:PerIpPerMinute", "5"));
        var client = app.CreateClient();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);

        var banned = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.TooManyRequests, banned.StatusCode);
        Assert.True(banned.Headers.RetryAfter?.Delta?.TotalSeconds > 0);
        Assert.Contains("Too many requests", await banned.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/config")).StatusCode);
    }

    [Fact]
    public async Task A_header_anyone_can_send_does_not_change_who_a_visitor_is()
    {
        using var app = AppWith(("Abuse:PerIpPerMinute", "3"));
        var client = app.CreateClient();
        HttpStatusCode last = default;
        for (var i = 0; i < 6; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
            request.Headers.Add("CF-Connecting-IP", $"9.9.9.{i}");
            last = (await client.SendAsync(request)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task When_a_header_is_named_it_tells_visitors_apart()
    {
        using var app = AppWith(("Abuse:PerIpPerMinute", "2"), ("RateLimits:ClientIpHeader", "CF-Connecting-IP"));
        var client = app.CreateClient();
        async Task<HttpStatusCode> Get(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
            request.Headers.Add("CF-Connecting-IP", ip);
            return (await client.SendAsync(request)).StatusCode;
        }
        await Get("9.9.9.1"); await Get("9.9.9.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get("9.9.9.1"));
        Assert.Equal(HttpStatusCode.OK, await Get("9.9.9.2"));
    }

    [Fact]
    public async Task Development_leaves_it_off_unless_asked()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        for (var i = 0; i < 300; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }
}
