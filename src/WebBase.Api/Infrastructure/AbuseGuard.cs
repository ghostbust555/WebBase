using System.Collections.Concurrent;
using System.Globalization;

namespace WebBase.Api.Infrastructure;

/// <summary>
/// Basic protection against a flood of requests, aimed at keeping the cloud
/// bill sane rather than at keeping the site pleasant under attack. Bound from
/// the "Abuse" section (docs/CONFIGURATION.md).
/// </summary>
public class AbuseOptions
{
    public const string SectionName = "Abuse";

    /// <summary>Off by default in development, where every request comes from one address.</summary>
    public bool? Enabled { get; set; }

    /// <summary>More requests than this from one address in a minute gets it banned. A page load is 10-30.</summary>
    public int PerIpPerMinute { get; set; } = 240;

    /// <summary>How long a ban lasts.</summary>
    public int BanMinutes { get; set; } = 30;

    /// <summary>
    /// All addresses together, per minute and per running copy of the app. Past
    /// it, requests get a quick 503 until the minute is over: the cap on what a
    /// flood from many addresses can cost. 0 = no cap.
    /// </summary>
    public int TotalPerMinute { get; set; } = 6000;

    /// <summary>The biggest request body accepted, in KB. Notes and sign-in are tiny.</summary>
    public int MaxBodyKb { get; set; } = 256;

    /// <summary>Addresses remembered at once; past it, new ones aren't tracked (the total cap still applies).</summary>
    public int MaxTracked { get; set; } = 50_000;

    public bool IsEnabled(bool development) => Enabled ?? !development;
}

public enum Verdict { Allowed, Banned, Busy }

/// <summary>
/// Counts requests per address per minute and bans the ones that go over. It
/// lives in memory, in each running copy of the app: restarting forgets the
/// bans, and two copies count separately. Enough to stop one machine
/// hammering the site, which is the cheap attack.
/// </summary>
public sealed class AbuseTracker(AbuseOptions options, TimeProvider clock, ILogger<AbuseTracker> log)
{
    private sealed class Entry { public long Minute; public int Count; public long BannedUntil; }

    private readonly ConcurrentDictionary<string, Entry> _ips = new();
    private readonly Lock _total = new();
    private long _totalMinute;
    private int _totalCount;
    private long _lastSweep;

    private long Now => clock.GetUtcNow().ToUnixTimeSeconds();

    /// <summary>Seconds until a banned address may try again (0 if it isn't banned).</summary>
    public int RetryAfter(string ip) =>
        _ips.TryGetValue(ip, out var e) ? (int)Math.Max(0, e.BannedUntil - Now) : 0;

    public Verdict Check(string ip)
    {
        var now = Now;
        var minute = now / 60;
        Sweep(now, minute);

        if (_ips.TryGetValue(ip, out var known) && Volatile.Read(ref known.BannedUntil) > now)
            return Verdict.Banned; // cheapest answer first: a banned address doesn't count toward the total

        if (_ips.TryGetValue(ip, out var entry) || _ips.Count < options.MaxTracked)
        {
            entry ??= _ips.GetOrAdd(ip, _ => new Entry());
            lock (entry)
            {
                if (entry.Minute != minute) { entry.Minute = minute; entry.Count = 0; }
                if (++entry.Count > options.PerIpPerMinute)
                {
                    entry.BannedUntil = now + options.BanMinutes * 60L;
                    log.LogWarning("Banned {Ip} for {Minutes} minutes: {Count} requests in a minute", ip, options.BanMinutes, entry.Count);
                    return Verdict.Banned;
                }
            }
        }

        if (options.TotalPerMinute > 0)
        {
            lock (_total)
            {
                if (_totalMinute != minute) { _totalMinute = minute; _totalCount = 0; }
                if (++_totalCount > options.TotalPerMinute) return Verdict.Busy;
            }
        }
        return Verdict.Allowed;
    }

    /// <summary>Once a minute, forget addresses that have been quiet and aren't banned.</summary>
    private void Sweep(long now, long minute)
    {
        var last = Volatile.Read(ref _lastSweep);
        if (now - last < 60 || Interlocked.CompareExchange(ref _lastSweep, now, last) != last) return;
        foreach (var (ip, e) in _ips)
            if (e.Minute < minute && e.BannedUntil <= now) _ips.TryRemove(ip, out _);
    }
}

/// <summary>Turns the tracker's verdict into a response, before anything else (even the website's files) does work.</summary>
public class AbuseGuardMiddleware(RequestDelegate next, AbuseTracker tracker, AbuseOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var ip = RateLimits.ClientIp(context);
        var verdict = tracker.Check(ip);
        if (verdict == Verdict.Allowed) { await next(context); return; }

        var wait = verdict == Verdict.Banned ? Math.Max(tracker.RetryAfter(ip), 1) : 10;
        context.Response.StatusCode = verdict == Verdict.Banned ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = wait.ToString(CultureInfo.InvariantCulture);
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(verdict == Verdict.Banned
            ? "{\"error\":\"Too many requests. Try again later.\"}"
            : "{\"error\":\"The site is very busy. Try again in a moment.\"}");
    }
}

public static class AbuseGuard
{
    public static AbuseOptions OptionsFrom(IConfiguration config) =>
        config.GetSection(AbuseOptions.SectionName).Get<AbuseOptions>() ?? new AbuseOptions();

    public static IServiceCollection AddAbuseGuard(this IServiceCollection services, AbuseOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AbuseTracker>();
        return services;
    }
}
