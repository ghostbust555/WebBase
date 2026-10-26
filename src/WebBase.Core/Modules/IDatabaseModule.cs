using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebBase.Data;

namespace WebBase.Modules;

/// <summary>
/// A database implementation, packaged as its own project
/// (modules/WebBase.Database.*). The app lists the ones it's built with in
/// src/WebBase.Api/Modules.cs, and uses the one Database:Provider names.
/// See docs/MODULES.md.
/// </summary>
public interface IDatabaseModule
{
    /// <summary>The Database:Provider value that selects it, e.g. "postgres".</summary>
    string Name { get; }

    /// <summary>
    /// Register <see cref="IDatabase"/>, <see cref="IUserStore"/> and
    /// <see cref="INoteStore"/> (and whatever they need), reading this module's
    /// own settings from <paramref name="config"/>. Throw a readable
    /// InvalidOperationException if a required setting is missing.
    /// </summary>
    void Register(IServiceCollection services, IConfiguration config);
}
