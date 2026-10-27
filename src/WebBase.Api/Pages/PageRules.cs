using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WebBase.Api.Content;

namespace WebBase.Api.Pages;

/// <summary>
/// The rules for the authors' pages (docs/PAGES.md): sizes, what an address may
/// look like, and which addresses belong to the app itself. Kept apart from the
/// controllers so the tests can state them directly.
/// </summary>
public static partial class PageRules
{
    public const int MaxTitle = 120;
    public const int MaxPath = 100;
    public const int MaxSegments = 4;

    /// <summary>
    /// The longest body, in characters (UTF-16 units). Chosen so that the biggest
    /// request still fits Abuse:MaxBodyKb's default of 256 KB: a character is at
    /// most 3 bytes of UTF-8 (control characters, which JSON would write as 6, are
    /// refused), and the rest of the request is small (<see cref="OverheadBytes"/>).
    /// A test keeps these in step (PagesApiTests).
    /// </summary>
    public const int MaxBody = 80_000;

    /// <summary>What a request carries besides the body: title, path, field names, quotes.</summary>
    public const int OverheadBytes = 4 * 1024;

    /// <summary>The biggest request a valid page can make.</summary>
    public const int MaxRequestBytes = MaxBody * 3 + OverheadBytes;

    public const int MaxOrder = 10_000;

    /// <summary>
    /// First words of an address that the app itself uses: its own pages (client/src/router.ts),
    /// the server's (/api, /content, /scalar...) and the built website's folders. A page there
    /// would be unreachable or would shadow the app. A test compares this list with the router.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "login", "register", "account", "notes", "authors", "write", "docs",
        "api", "content", "assets", "scalar", "openapi", "index", "health",
    };

    /// <summary>
    /// A page address from user input: trimmed, lower-cased, no outer slashes.
    /// Null (with the reason) if it isn't made of valid words.
    /// </summary>
    public static string? CleanPath(string? input, out string? problem)
    {
        problem = null;
        var path = (input ?? "").Trim().Trim('/').ToLowerInvariant();
        var segments = path.Split('/');
        if (path.Length == 0 || segments.Any(s => !ContentPaths.Segment().IsMatch(s)))
        {
            problem = "The address can only have lower-case letters, digits, - and _, with / between parts (for example blog/first-post).";
            return null;
        }
        if (path.Length > MaxPath || segments.Length > MaxSegments)
        {
            problem = $"The address can be at most {MaxPath} characters and {MaxSegments} parts.";
            return null;
        }
        if (Reserved.Contains(segments[0]))
        {
            problem = $"\"{segments[0]}\" is used by the site itself. Choose another address.";
            return null;
        }
        return path;
    }

    /// <summary>"What We Do — Café" → "what-we-do-cafe". Empty if the title has no letters or digits a-z.</summary>
    public static string SlugFromTitle(string title)
    {
        var plain = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        var slug = NotWord().Replace(plain.ToString().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > MaxPath ? slug[..MaxPath].Trim('-') : slug;
    }

    /// <summary>Control characters other than tab and line breaks have no place in markdown (and cost 6 bytes each in JSON).</summary>
    public static bool HasControlCharacters(string text) =>
        text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotWord();
}
