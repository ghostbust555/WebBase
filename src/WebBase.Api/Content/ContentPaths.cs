using System.Text.RegularExpressions;
using WebBase.FileStorage;

namespace WebBase.Api.Content;

/// <summary>The CMS's rules for names, in one place. (Rules every file store keeps: FilePaths.)</summary>
public static partial class ContentPaths
{
    // Page addresses are lower-case words, digits, - and _, separated by /.
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    public static partial Regex Segment();

    /// <summary>
    /// Whether a file may be handed out under /content/: no .., no hidden or
    /// private names (starting with . or _) anywhere in the path, and never
    /// the markdown itself (drafts live there).
    /// </summary>
    public static bool IsPublicFile(string path) =>
        FilePaths.IsSafeRelative(path) && !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
        path.Split('/').All(s => s.Length > 0 && s[0] != '.' && s[0] != '_');
}
