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

    /// <summary>
    /// What talking about the example looks like: its name as a feature ("Notes"), its
    /// types and files, its addresses. Ordinary English is left alone: "Note:" in a
    /// guide, or "notes" on a site's own About page, is not the example.
    /// </summary>
    private static readonly Regex Mention = new(
        @"\bNotes\b" +                                                    // the feature's name
        @"|\bI?Notes?(Store|View|Controller|Contracts|Dto|ApiTests)\b" +    // its types and files
        @"|\bNote\.cs\b|\bnotesApi\b|/api/notes\b|`/notes`" +             // ...and its code and addresses
        @"|(?i:\bnotes? (example|feature|page|table|container)\b|\badd a note\b)",
        RegexOptions.Compiled);

    /// <summary>True if this line of a guide talks about the example (a link to its guide doesn't count).</summary>
    public static bool MentionsExample(string line) => Mention.IsMatch(line.Replace("EXAMPLE_NOTES", ""));

    /// <summary>
    /// The project's own guides: docs/, the READMEs (the root and each module's) and
    /// CLAUDE.md. Not content/: that's the site owner's own writing.
    /// </summary>
    private static IEnumerable<string> Guides(string root) =>
        Directory.GetFiles(Path.Combine(root, "docs"), "*.md")
            .Concat(Directory.GetFiles(Path.Combine(root, "modules"), "README.md", SearchOption.AllDirectories))
            .Append(Path.Combine(root, "README.md"))
            .Append(Path.Combine(root, "CLAUDE.md"));

    [Theory]
    [InlineData("Try the **Notes** page: what you type is saved.")]
    [InlineData("Copy `INoteStore` for your store.")]
    [InlineData("Open the address, sign up, add a note.")]
    [InlineData("It sends `POST /api/notes` with the text.")]
    [InlineData("The notes table is created by the first migration.")]
    [InlineData("See client/src/views/NotesView.vue.")]
    public void These_lines_mention_the_example(string line) => Assert.True(MentionsExample(line));

    [Theory]
    [InlineData("Note: the first account is an author.")]
    [InlineData("Operating notes for AI coding assistants.")]
    [InlineData("Names starting with _ are ignored (notes to yourself).")]
    [InlineData("Every guide links [EXAMPLE_NOTES.md](EXAMPLE_NOTES.md) once.")]
    [InlineData("A footnote, denoted by a star.")]
    public void These_lines_do_not(string line) => Assert.False(MentionsExample(line));

    [Fact]
    public void No_other_guide_readme_or_claude_md_mentions_the_example()
    {
        var root = RepoRoot();
        var found = new List<string>();
        foreach (var file in Guides(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative == ExampleDoc) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (MentionsExample(lines[i])) found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(found.Count == 0,
            "Only docs/EXAMPLE_NOTES.md may talk about the bundled example. " +
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
