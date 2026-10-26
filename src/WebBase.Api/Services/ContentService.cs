using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Services;

/// <summary>
/// The optional CMS: a folder of markdown files, where the folder layout is
/// the website's addresses.
///
///   content/index.md              →  /
///   content/about.md              →  /about
///   content/blog/index.md         →  /blog       (a folder without one gets a list of what's in it)
///   content/blog/first-post.md    →  /blog/first-post
///
/// Each file may start with a few settings between --- lines:
///
///   ---
///   title: First post
///   description: One line for search engines and lists
///   date: 2026-10-25
///   order: 1          (lower first in lists; pages without one come after, newest date first)
///   menu: true        (show in the site's header)
///   draft: true       (development only, until you remove this)
///   ---
///
/// Names starting with _ or . are ignored (notes to yourself). Other files
/// (images...) are served as they are under /content/.... See docs/CMS.md.
/// </summary>
public partial class ContentService(IOptions<CmsOptions> cms, IOptions<AppOptions> app, IWebHostEnvironment env)
{
    public bool Enabled => cms.Value.Enabled;

    /// <summary>The content folder, as a full path.</summary>
    public string Root => Path.GetFullPath(Path.Combine(env.ContentRootPath, cms.Value.ContentPath));

    private bool ShowDrafts => app.Value.IsDevelopment;

    // Addresses are lower-case words, digits, - and _, separated by /.
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex Segment();

    /// <summary>The page at an address, or null if there isn't one.</summary>
    public ContentPage? GetPage(string? path)
    {
        var segments = Split(path);
        if (segments == null || !Directory.Exists(Root)) return null;

        var relative = string.Join('/', segments);
        var basePath = Path.Combine([Root, .. segments]);

        // /about is about.md; /blog is blog/index.md, or a list of blog/ if it has no index.
        var file = segments.Length == 0 ? null : basePath + ".md";
        if (file != null && File.Exists(file)) return Load(file, relative, children: null);

        var folder = segments.Length == 0 ? Root : basePath;
        if (!Directory.Exists(folder)) return null;

        var index = Path.Combine(folder, "index.md");
        var children = ListFolder(folder, relative);
        if (File.Exists(index)) return Load(index, relative, children);

        // A folder with no index.md: a plain list of what's in it.
        if (segments.Length == 0) return null; // no home page from the CMS; the site's own one shows
        if (children.Count == 0) return null; // images/ and the like: no pages to list
        return new ContentPage(relative, Titleize(segments[^1]), null, null, null, false, false, "", children);
    }

    /// <summary>Every page, for menus and site maps.</summary>
    public List<ContentSummary> AllPages()
    {
        if (!Directory.Exists(Root)) return [];
        var pages = new List<ContentSummary>();
        Walk(Root, "", pages); // each folder's pages follow the folder itself
        return pages;
    }

    private void Walk(string folder, string relative, List<ContentSummary> into)
    {
        foreach (var child in ListFolder(folder, relative))
        {
            into.Add(child);
            var sub = Path.Combine(Root, child.Path.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(sub)) Walk(sub, child.Path, into);
        }
    }

    /// <summary>The pages directly inside a folder: its .md files and its sub-folders.</summary>
    private List<ContentSummary> ListFolder(string folder, string relative)
    {
        var prefix = relative.Length == 0 ? "" : relative + "/";
        var items = new List<ContentSummary>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == "index" || !Segment().IsMatch(name)) continue;
            var page = Load(file, prefix + name, children: null, summaryOnly: true);
            if (page != null) items.Add(page.Summary());
        }

        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(sub);
            if (!Segment().IsMatch(name)) continue;
            var index = Path.Combine(sub, "index.md");
            var page = File.Exists(index) ? Load(index, prefix + name, null, summaryOnly: true) : null;
            if (File.Exists(index) && page == null) continue; // a draft folder
            if (page == null && !ListFolder(sub, prefix + name).Any()) continue; // images/ and the like
            items.Add(page?.Summary() ?? new ContentSummary(prefix + name, Titleize(name), null, null, null, false));
        }

        return Sort(items);
    }

    private static List<ContentSummary> Sort(List<ContentSummary> items) =>
        items.OrderBy(i => i.Order ?? int.MaxValue) // pages with an order first
             .ThenByDescending(i => i.Date ?? DateOnly.MinValue)
             .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
             .ToList();

    private ContentPage? Load(string file, string relative, List<ContentSummary>? children, bool summaryOnly = false)
    {
        var (meta, body) = FrontMatter.Parse(File.ReadAllText(file));
        var draft = Flag(meta, "draft");
        if (draft && !ShowDrafts) return null;

        var name = relative.Length == 0 ? "home" : relative.Split('/')[^1];
        return new ContentPage(
            Path: relative,
            Title: meta.GetValueOrDefault("title") ?? Titleize(name),
            Description: meta.GetValueOrDefault("description"),
            Date: DateOnly.TryParse(meta.GetValueOrDefault("date"), out var d) ? d : null,
            Order: int.TryParse(meta.GetValueOrDefault("order"), out var o) ? o : null,
            Menu: Flag(meta, "menu"),
            Draft: draft,
            Markdown: summaryOnly ? "" : body,
            Children: children);
    }

    /// <summary>
    /// Clean an address into folder names, or null if it isn't a valid one.
    /// Only plain names are allowed, so nobody can ask for "../../etc/passwd".
    /// </summary>
    internal static string[]? Split(string? path)
    {
        var segments = (path ?? "").Trim('/').ToLowerInvariant()
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && segments[^1] == "index") segments = segments[..^1];
        return segments.All(s => Segment().IsMatch(s)) ? segments : null;
    }

    private static bool Flag(Dictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>"first-post" → "First post", for pages without a title.</summary>
    private static string Titleize(string name)
    {
        var words = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }
}

/// <summary>Reads the "--- key: value ---" block at the top of a markdown file.</summary>
public static class FrontMatter
{
    public static (Dictionary<string, string> Meta, string Body) Parse(string text)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n")) return (meta, text);

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (meta, text);

        foreach (var line in text[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var value = line[(colon + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];
            meta[line[..colon].Trim()] = value;
        }

        var bodyStart = text.IndexOf('\n', end + 4);
        return (meta, bodyStart < 0 ? "" : text[(bodyStart + 1)..]);
    }
}

public record ContentSummary(string Path, string Title, string? Description, DateOnly? Date, int? Order, bool Menu);

public record ContentPage(
    string Path, string Title, string? Description, DateOnly? Date, int? Order, bool Menu, bool Draft,
    string Markdown, List<ContentSummary>? Children)
{
    public ContentSummary Summary() => new(Path, Title, Description, Date, Order, Menu);
}
