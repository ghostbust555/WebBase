using System.Text.RegularExpressions;

namespace WebBase.Tests;

/// <summary>
/// The bundled per-user example is documented in exactly one file,
/// docs/EXAMPLE_NOTES.md, so that deleting that file leaves every other guide
/// reading correctly. These tests keep it that way (and keep the one file honest).
/// </summary>
public class ExampleDocIsolationTests
{
    private const string ExampleDoc = "docs/EXAMPLE_NOTES.md";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "compose.dev.yaml"))) return dir.FullName;
        throw new InvalidOperationException("Can't find the repository root from " + AppContext.BaseDirectory);
    }

    private static readonly string[] SkippedFolders = [".git", "node_modules", ".build", "bin", "obj", "dist", ".docker-data"];

    /// <summary>Every markdown file we own: the guides, the READMEs, CLAUDE.md, the modules' READMEs, the CMS content.</summary>
    private static IEnumerable<string> MarkdownFiles(string dir)
    {
        foreach (var file in Directory.GetFiles(dir, "*.md")) yield return file;
        foreach (var sub in Directory.GetDirectories(dir))
            if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                foreach (var file in MarkdownFiles(sub)) yield return file;
    }

    [Fact]
    public void No_other_guide_readme_or_claude_md_mentions_the_example()
    {
        var root = RepoRoot();
        var found = new List<string>();
        foreach (var file in MarkdownFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative == ExampleDoc) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // A link to the file is the one allowed mention; its name is not the example being discussed.
                var text = lines[i].Replace("EXAMPLE_NOTES", "");
                if (Regex.IsMatch(text, "note", RegexOptions.IgnoreCase))
                    found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.True(found.Count == 0,
            "Only docs/EXAMPLE_NOTES.md may talk about the bundled example (and, to be safe, nothing else may use the word). " +
            "Use a neutral example (\"Recipes\", or the pages from docs/PAGES.md), or link to EXAMPLE_NOTES.md in a sentence that still reads " +
            "correctly without it:\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_example_guide_names_files_that_exist()
    {
        var root = RepoRoot();
        var guide = Path.Combine(root, ExampleDoc);
        if (!File.Exists(guide)) return; // deleted on purpose: nothing to check

        var missing = new List<string>();
        foreach (Match m in Regex.Matches(File.ReadAllText(guide), @"`((?:src|modules|tests|client|scripts|docs)/[A-Za-z0-9_./-]+)`"))
        {
            var path = m.Groups[1].Value;
            // "Recipes" is the make-believe feature the copy table names; the removal list names the real ones.
            if (path.Contains("Recipe", StringComparison.Ordinal)) continue;
            if (!File.Exists(Path.Combine(root, path)) && !Directory.Exists(Path.Combine(root, path))) missing.Add(path);
        }
        Assert.True(missing.Count == 0, ExampleDoc + " names files that don't exist:\n" + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void Deleting_the_example_guide_breaks_nothing_that_reads_the_docs()
    {
        // The website lists guides by what is in docs/, and ORDER only ranks names, so a missing file is fine.
        var docs = File.ReadAllText(Path.Combine(RepoRoot(), "client/src/lib/docs.ts"));
        Assert.Contains("import.meta.glob", docs);
        Assert.Contains("ORDER.indexOf(name)", docs); // a name in ORDER with no file is simply never looked up
        // ...and no code depends on the file existing.
        var code = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(RepoRoot(), "client/src"), "*.*", SearchOption.AllDirectories).Where(f => !f.EndsWith("docs.ts")))
            .Concat(Directory.GetFiles(Path.Combine(RepoRoot(), "scripts"), "*.sh", SearchOption.AllDirectories))
            .Where(f => !f.EndsWith("Note.cs") && !f.EndsWith("NotesView.vue")) // the example's own files may say what to delete with them
            .Where(f => File.ReadAllText(f).Contains("EXAMPLE_NOTES"));
        // smoke.sh names it only in a comment, to say what to delete with it.
        Assert.All(code.Where(f => !f.EndsWith("smoke.sh")), f => Assert.Fail($"{f} depends on {ExampleDoc}"));
    }
}
