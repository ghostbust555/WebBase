namespace WebBase.Database.Postgres.Tests;

/// <summary>DATABASE_URL, as cloud hosts hand it out, read as a .NET connection string.</summary>
public class DatabaseUrlTests
{
    [Theory]
    [InlineData("postgres://u:p%40ss@db.example.com:6543/app?sslmode=require",
                "Host=db.example.com;Port=6543;Database=app;Username=u;Password=p@ss;SSL Mode=Require")]
    [InlineData("postgresql://u:p@localhost/app", "Host=localhost;Port=5432;Database=app;Username=u;Password=p")]
    [InlineData("postgres://u:p@localhost/app?host=/cloudsql/proj:region:db",
                "Host=/cloudsql/proj:region:db;Port=5432;Database=app;Username=u;Password=p")]
    // As Neon hands it out: extra options it doesn't need are ignored.
    [InlineData("postgresql://u:p@ep-x-123.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require",
                "Host=ep-x-123.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=u;Password=p;SSL Mode=Require")]
    public void Database_urls_become_connection_strings(string url, string expected)
    {
        Assert.Equal(expected, DatabaseUrl.ToConnectionString(url));
    }

}
