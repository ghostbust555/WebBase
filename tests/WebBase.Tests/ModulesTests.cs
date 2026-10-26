using Microsoft.AspNetCore.Hosting;

namespace WebBase.Tests;

/// <summary>Settings that name a module the app isn't built with fail at startup, saying what to do.</summary>
public class ModulesTests
{
    [Theory]
    // Names no module has, so the test holds whichever modules a project adds.
    [InlineData("Database:Provider", "no-such-database")]
    [InlineData("Cms:Source", "no-such-storage")]
    public void Naming_a_module_that_isnt_built_in_says_how_to_add_it(string setting, string value)
    {
        using var app = new TestApp().WithWebHostBuilder(b =>
        {
            b.UseSetting("Cms:Enabled", "true");
            b.UseSetting(setting, value);
        });
        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains($"{setting} is '{value}'", error.ToString());
        Assert.Contains("docs/MODULES.md", error.ToString());
    }
}
