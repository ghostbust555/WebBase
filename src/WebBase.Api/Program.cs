using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using WebBase.Api.Infrastructure;
using WebBase.Api.Services;

// ============================================================================
// The whole server is wired together here, top to bottom:
//   1. read settings      2. register services      3. build the app
//   4. the request pipeline (what every request passes through, in order)
//   5. bring the database up to date      6. run
// See docs/HOW_IT_FITS_TOGETHER.md for the picture.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- 1. Settings -----------------------------------------------------------
// Settings come from appsettings.json, then environment variables on top
// (Jwt__Key=... sets Jwt:Key). In Docker those come from .env.local.

builder.Services.Configure<AppOptions>(config.GetSection(AppOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(config.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<CmsOptions>(config.GetSection(CmsOptions.SectionName));
var appOptions = config.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();

// Cloud hosts (Cloud Run, Render, Railway, Heroku...) say which port to listen
// on with PORT. Without it the container listens on 8080.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port))
    builder.WebHost.UseUrls($"http://+:{port}");

// The signing key. Development may run without one so a fresh clone just works;
// anywhere else a missing or short key stops the server, because a guessable
// key means anyone can sign in as anyone.
var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) && appOptions.IsDevelopment) jwt.Key = JwtOptions.DevelopmentKey;
if (jwt.Key.Length < JwtOptions.MinimumKeyLength || (!appOptions.IsDevelopment && jwt.Key == JwtOptions.DevelopmentKey))
    throw new InvalidOperationException(
        $"Jwt:Key must be set to a random secret of at least {JwtOptions.MinimumKeyLength} characters. " +
        "Run ./setup.sh, or set the Jwt__Key environment variable.");
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(jwt));

// The database: PostgreSQL unless Database:Provider says otherwise. The app
// uses it only through IUserStore, INoteStore and IDatabase (Data/), so the
// implementation can change without touching anything else (docs/DATABASE.md).
builder.Services.AddDatabase(config);

// ---- 2. Services -----------------------------------------------------------
// Anything a controller asks for in its constructor has to be registered here.

builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IGoogleIdentity, GoogleIdentity>();
builder.Services.AddSingleton<ContentService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep "sub" as "sub"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt.Key),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAppRateLimits(config);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Behind Cloudflare, a cloud load balancer or any other proxy, the visitor's
// real address and https-ness arrive in X-Forwarded-* headers. In prod the app
// is only reachable through such a proxy, so they are trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---- 3. Build --------------------------------------------------------------
var app = builder.Build();

// ---- 4. The request pipeline -----------------------------------------------
// Order matters: each line sees the request before the ones below it.

app.UseForwardedHeaders();
app.UseSecurityHeaders();

if (appOptions.IsDevelopment)
{
    // Browsable API docs at /scalar (development only).
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// In prod the built website lives in wwwroot (the Dockerfile puts it there)
// and this same server hands it out. In development the Vite dev server serves
// the website instead, with hot reload, and wwwroot is empty.
app.UseDefaultFiles();
app.UseStaticFiles();

// The CMS's images and other files: content/images/cat.jpg is served at
// /content/images/cat.jpg. Never the .md files themselves - drafts live there.
var content = app.Services.GetRequiredService<ContentService>();
if (content.Enabled && Directory.Exists(content.Root))
{
    var types = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
    types.Mappings.Remove(".md");
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(content.Root),
        RequestPath = "/content",
        ContentTypeProvider = types,
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// An /api address that matches nothing is a JSON 404, never the website.
app.Map("/api/{**rest}", () => Results.NotFound(new { error = "No such API endpoint." }));

// Every other address is a page of the single-page app: hand back index.html
// and let the browser-side router (client/src/router.ts) decide what to show.
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");

// ---- 5. Database -----------------------------------------------------------
// Bring the database up to date (Postgres: apply new migrations), so deploying
// a new version is just "start it".
string databaseProvider;
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IDatabase>();
    databaseProvider = database.Provider;
    if (config.GetValue("Database:MigrateOnStartup", true)) await database.PrepareAsync();
}

app.Logger.LogInformation("{Name} starting: environment={Environment} build={Build} database={Database} google-sign-in={Google} cms={Cms}",
    appOptions.Name, appOptions.IsDevelopment ? "development" : "prod",
    string.IsNullOrEmpty(appOptions.Build) ? "local" : appOptions.Build,
    databaseProvider,
    string.IsNullOrWhiteSpace(config["Auth:Google:ClientId"]) ? "off" : "on",
    content.Enabled ? $"on ({content.Root})" : "off");

// ---- 6. Run ----------------------------------------------------------------
app.Run();

// Lets the test project start the whole app in memory (WebApplicationFactory).
public partial class Program;
