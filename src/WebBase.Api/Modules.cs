using WebBase.Database.Postgres;
using WebBase.Modules;
// (module usings)

namespace WebBase.Api;

/// <summary>
/// The optional pieces this app is built with. A database or a kind of file
/// storage is a module: its own project in modules/, with its own packages,
/// so the app itself never depends on a particular database or cloud.
///
/// To add one: reference its project in WebBase.Api.csproj (the "Modules"
/// group) and add it to the list below. ./scripts/module.sh add &lt;name&gt; does
/// both. Each module's README.md says what it needs. See docs/MODULES.md.
/// </summary>
public static class Modules
{
    /// <summary>Databases, chosen by Database:Provider. The first is the default.</summary>
    public static IReadOnlyList<IDatabaseModule> Databases { get; } =
    [
        new PostgresModule(),
        // (database modules)
    ];

    /// <summary>File storage, chosen for the CMS by Cms:Source ("files", a folder, is built in).</summary>
    public static IReadOnlyList<IFileStorageModule> FileStorage { get; } =
    [
        // (file storage modules)
    ];

    /// <summary>
    /// Registers the database Database:Provider names. Everything else in the
    /// app asks for IUserStore, INoteStore and IDatabase and never learns
    /// which one it got.
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        var name = (config["Database:Provider"] ?? "").Trim().ToLowerInvariant();
        var module = name.Length == 0 ? Databases[0] : Databases.FirstOrDefault(m => m.Name == name);
        if (module == null)
            throw new InvalidOperationException(
                $"Database:Provider is '{name}', but this app is built with: {string.Join(", ", Databases.Select(m => m.Name))}. " +
                "Another database is a module to add first: see docs/MODULES.md.");
        module.Register(services, config);
        return services;
    }

    /// <summary>The file storage module called <paramref name="name"/>, or a readable error.</summary>
    public static IFileStorageModule FileStorageNamed(string name, string setting) =>
        FileStorage.FirstOrDefault(m => m.Name == name)
        ?? throw new InvalidOperationException(
            $"{setting} is '{name}', but this app is built with: {string.Join(", ", FileStorage.Select(m => m.Name).Prepend("files"))}. " +
            "Cloud storage is a module to add first (s3, azure, gcs): see docs/MODULES.md.");
}
