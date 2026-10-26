using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using WebBase.Api.Infrastructure;

namespace WebBase.Tests;

/// <summary>client/src/api-types.ts is generated from the C# types; these keep it honest.</summary>
public class TypeScriptContractsTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "compose.dev.yaml"))) return dir.FullName;
        throw new InvalidOperationException("Can't find the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>WRITE_TYPESCRIPT=1 (./scripts/gen-types.sh) writes the file instead of checking it.</summary>
    [Fact]
    public void The_committed_typescript_matches_the_csharp_types()
    {
        var path = Path.Combine(RepoRoot(), TypeScriptContracts.RelativePath);
        if (Environment.GetEnvironmentVariable("WRITE_TYPESCRIPT") == "1")
        {
            TypeScriptContracts.WriteIfChanged(path, typeof(Program).Assembly);
            return;
        }
        var generated = TypeScriptContracts.Generate(typeof(Program).Assembly);
        var committed = File.Exists(path) ? File.ReadAllText(path) : "(missing)";
        Assert.True(generated == committed,
            $"{TypeScriptContracts.RelativePath} is out of date with the C# types. Run ./scripts/gen-types.sh " +
            "(./dev.sh also rewrites it while it runs), then commit it.");
    }

    [Fact]
    public void Shapes_follow_the_json_the_api_sends()
    {
        var ts = TypeScriptContracts.Generate([typeof(SampleController)]);

        Assert.Contains("export type Mood = 'Happy' | 'Sad'", ts);
        Assert.Contains("""
            export interface SampleOut {
              id: string
              born: string | null
              mood: Mood
              tags: (string | null)[]
              counts: Record<string, number>
              x_y: string
              numbers: number[]
              maybeNested: Inner | null
            }
            """.Replace("\r", ""), ts);
        // Only ever sent: nullable fields may be left out.
        Assert.Contains("""
            export interface SampleIn {
              name: string
              nick?: string | null
              age?: number | null
            }
            """.Replace("\r", ""), ts);
        Assert.Contains("export interface ErrorResponse", ts); // always: every error has this shape
    }

    [Fact]
    public void An_action_must_say_what_it_returns()
    {
        var untyped = Assert.Throws<InvalidOperationException>(() => TypeScriptContracts.Generate([typeof(UntypedController)]));
        Assert.Contains("UntypedController.Get", untyped.Message);
        var anonymous = Assert.Throws<InvalidOperationException>(() => TypeScriptContracts.Generate([typeof(ObjectController)]));
        Assert.Contains("returns object", anonymous.Message);
    }

    public enum Mood { Happy, Sad }

    public record SampleIn(string Name, string? Nick, int? Age);

    public record SampleOut(
        Guid Id, DateOnly? Born, Mood Mood, List<string?> Tags, Dictionary<string, int> Counts,
        [property: JsonPropertyName("x_y")] string Renamed, int[] Numbers, Inner? MaybeNested);

    public record Inner(string Value);

    [ApiController, Route("sample")]
    public class SampleController : ControllerBase
    {
        [HttpPost] public ActionResult<SampleOut> Post(SampleIn body) => throw new NotImplementedException();
        [HttpGet] public Task<List<SampleOut>> List() => throw new NotImplementedException();
        [HttpDelete, ProducesResponseType(204)] public IActionResult Delete() => NoContent();
    }

    [ApiController, Route("untyped")]
    public class UntypedController : ControllerBase
    {
        [HttpGet] public IActionResult Get() => Ok(new { secret = 1 });
    }

    [ApiController, Route("object")]
    public class ObjectController : ControllerBase
    {
        [HttpGet] public object Get() => new { secret = 1 };
    }
}
