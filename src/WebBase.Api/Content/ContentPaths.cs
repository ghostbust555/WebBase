using System.Text.RegularExpressions;

namespace WebBase.Api.Content;

/// <summary>The rules for names in the content store, in one place.</summary>
public static partial class ContentPaths
{
    // Page addresses are lower-case words, digits, - and _, separated by /.
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    public static partial Regex Segment();

    /// <summary>"site", "/site/", "site/" all become "site/"; empty stays empty.</summary>
    public static string NormalizePrefix(string? prefix)
    {
        var p = (prefix ?? "").Trim().Trim('/');
        return p.Length == 0 ? "" : p + "/";
    }

    /// <summary>
    /// A plain relative path: no "..", ".", empty segments, backslashes or
    /// leading "/". Every store checks this itself rather than trusting its
    /// caller: some clients quietly turn "site/../other" into "other", outside
    /// the store's folder (the Azure Blob SDK does; the tests catch it).
    /// </summary>
    public static bool IsSafeRelative(string path) =>
        path.Length > 0 && !path.Contains('\\') &&
        path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

    /// <summary>
    /// Whether a file may be handed out under /content/: no .., no hidden or
    /// private names (starting with . or _) anywhere in the path, and never
    /// the markdown itself (drafts live there).
    /// </summary>
    public static bool IsPublicFile(string path) =>
        IsSafeRelative(path) && !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
        path.Split('/').All(s => s.Length > 0 && s[0] != '.' && s[0] != '_');
}
