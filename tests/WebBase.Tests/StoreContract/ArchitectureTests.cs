using WebBase.Api.Data;

namespace WebBase.Tests.StoreContract;

/// <summary>
/// Keeps the database swappable: outside Data/, nothing may ask for a
/// particular database's types - only for the store interfaces.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void Only_the_data_folder_depends_on_a_particular_database()
    {
        var app = typeof(IUserStore).Assembly;
        var offenders =
            from type in app.GetTypes()
            where type.Namespace?.StartsWith("WebBase.Api.Data") != true
            from ctor in type.GetConstructors()
            from parameter in ctor.GetParameters()
            let dependency = parameter.ParameterType
            where IsDatabaseSpecific(dependency)
            select $"{type.Name} asks for {dependency.Name}";

        Assert.Empty(offenders);
    }

    private static bool IsDatabaseSpecific(Type type) =>
        type.Namespace != null &&
        (type.Namespace.StartsWith("Microsoft.EntityFrameworkCore") ||
         type.Namespace.StartsWith("Npgsql") ||
         (type.Namespace.StartsWith("WebBase.Api.Data.") && !type.IsInterface));
}
