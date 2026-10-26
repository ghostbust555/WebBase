using System.Reflection;
using WebBase.Api.Content;

namespace WebBase.Tests;

/// <summary>
/// Keeps the app free of any particular database or cloud: those are
/// modules (modules/, docs/MODULES.md), and only modules may use their SDKs.
/// </summary>
public class ArchitectureTests
{
    /// <summary>Assembly names that belong in a module, never in the app.</summary>
    private static readonly string[] ModuleOnly =
    [
        "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Azure.Cosmos", "AWSSDK", "Azure.",
        "Google.Cloud.", "Google.Apis.Storage",
    ];

    private static readonly Assembly Api = typeof(ContentService).Assembly;
    private static readonly Assembly Core = typeof(IUserStore).Assembly;

    [Fact]
    public void Core_uses_nothing_but_dotnet_itself()
    {
        var references = Core.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.All(references, name => Assert.True(name.StartsWith("System") || name.StartsWith("Microsoft.Extensions."), name));
    }

    [Fact]
    public void The_app_uses_no_database_or_cloud_sdk_directly()
    {
        var offenders = Api.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(name => ModuleOnly.Any(name.StartsWith));
        Assert.Empty(offenders);
    }

    [Fact]
    public void Nothing_in_the_app_asks_for_a_module_type_only_for_the_interfaces()
    {
        var offenders =
            from type in Api.GetTypes()
            from ctor in type.GetConstructors()
            from parameter in ctor.GetParameters()
            let assembly = parameter.ParameterType.Assembly.GetName().Name!
            where assembly.StartsWith("WebBase.Database.") || assembly.StartsWith("WebBase.FileStorage.")
                  || ModuleOnly.Any(assembly.StartsWith)
            select $"{type.Name} asks for {parameter.ParameterType.Name}";
        Assert.Empty(offenders);
    }
}
