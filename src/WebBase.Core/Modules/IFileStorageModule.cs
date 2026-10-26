using Microsoft.Extensions.Configuration;
using WebBase.FileStorage;

namespace WebBase.Modules;

/// <summary>
/// A kind of file storage, packaged as its own project
/// (modules/WebBase.FileStorage.*). The app lists the ones it's built with in
/// src/WebBase.Api/Modules.cs. See docs/MODULES.md.
/// </summary>
public interface IFileStorageModule
{
    /// <summary>The name that selects it, e.g. "s3" in Cms:Source.</summary>
    string Name { get; }

    /// <summary>
    /// A store from its settings: the configuration section named after the
    /// module, e.g. Cms:S3 (environment variables Cms__S3__Bucket...).
    /// Throw a readable InvalidOperationException if a required one is missing.
    /// </summary>
    IFileStore Create(IConfiguration settings);
}
