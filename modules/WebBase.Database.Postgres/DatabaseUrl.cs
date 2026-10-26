using Npgsql;

namespace WebBase.Database.Postgres;

/// <summary>
/// Many hosts (Render, Railway, Fly, Heroku, DigitalOcean) hand an app its
/// database as one URL: postgres://user:password@host:5432/dbname. .NET wants
/// "Host=...;Username=..." instead. This converts the one to the other, so the
/// same image runs on those hosts with no extra configuration.
/// </summary>
public static class DatabaseUrl
{
    public static string? ToConnectionString(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
            throw new InvalidOperationException("DATABASE_URL must look like postgres://user:password@host:5432/dbname");

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        // Two query options hosts commonly add: ?sslmode=require, and ?host=/path
        // for a database reached through a socket (Google Cloud SQL's /cloudsql/...).
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = Uri.UnescapeDataString(kv[0]);
            var value = Uri.UnescapeDataString(kv[1]);
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                builder.SslMode = Enum.Parse<SslMode>(value.Replace("-", ""), ignoreCase: true);
            else if (key.Equals("host", StringComparison.OrdinalIgnoreCase))
                builder.Host = value;
        }
        return builder.ConnectionString;
    }
}
