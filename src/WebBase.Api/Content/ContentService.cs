using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Content;

/// <summary>
/// The optional CMS: a tree of markdown files, where the folder layout is the
/// website's addresses. The files can be anywhere an IContentStore reaches: a
/// folder (the default), an S3 bucket, an Azure Blob container.
///
///   index.md              →  /
///   about.md              →  /about
///   blog/index.md         →  /blog       (a folder without one gets a list of what's in it)
///   blog/first-post.md    →  /blog/first-post
///   theme.css             →  the site's colours (ThemeCss.cs)
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
///
/// Reading a bucket on every request would be slow and cost money, so the
/// whole tree is kept in memory and checked for changes every
/// Cms:RefreshSeconds. Only files whose size or date changed are read again.
/// </summary>
public class ContentService(IContentStore store, IOptions<CmsOptions> cms, IOptions<AppOptions> app,
    ILogger<ContentService> log, TimeProvider time)
{
    public const string ThemeFile = "theme.css";

    public bool Enabled => cms.Value.Enabled;

    /// <summary>Where the content comes from, for logs.</summary>
    public string Source => store.Description;

    private bool ShowDrafts => app.Value.IsDevelopment;

    // ---- The snapshot: everything the CMS knows, swapped in whole ----------

    private sealed record Parsed(Dictionary<string, string> Meta, string Body);

    private sealed record Snapshot(
        DateTimeOffset TakenAt,
        Dictionary<string, ContentEntry> Files,
        HashSet<string> Folders,
        Dictionary<string, Parsed> Markdown,
        string Theme);

    private Snapshot? _snapshot;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    // What each text file said last time, so an unchanged file isn't read again.
    private readonly ConcurrentDictionary<string, (ContentEntry Entry, string Text)> _texts = new();

    private async Task<Snapshot> CurrentAsync(CancellationToken ct)
    {
        var current = _snapshot;
        if (current != null && IsFresh(current)) return current;

        await _refresh.WaitAsync(ct);
        try
        {
            if (_snapshot != null && IsFresh(_snapshot)) return _snapshot;
            return _snapshot = await TakeAsync(ct);
        }
        catch (ContentUnavailableException e) when (_snapshot != null)
        {
            // Keep serving what we had; try again next time round.
            log.LogWarning("CMS content can't be refreshed, still serving the last copy: {Error}", e.Message);
            return _snapshot = _snapshot with { TakenAt = time.GetUtcNow() };
        }
        finally
        {
            _refresh.Release();
        }
    }

    private bool IsFresh(Snapshot s) =>
        cms.Value.RefreshSeconds > 0 && time.GetUtcNow() - s.TakenAt < TimeSpan.FromSeconds(cms.Value.RefreshSeconds);

    private async Task<Snapshot> TakeAsync(CancellationToken ct)
    {
        var entries = await store.ListAsync(ct);
        var files = entries.ToDictionary(e => e.Path, StringComparer.Ordinal);

        var folders = new HashSet<string>(StringComparer.Ordinal) { "" };
        foreach (var path in files.Keys)
            for (var slash = path.IndexOf('/'); slash > 0; slash = path.IndexOf('/', slash + 1))
                folders.Add(path[..slash]);

        var pages = files.Values.Where(e => IsPageFile(e.Path)).ToList();
        var texts = new ConcurrentDictionary<string, string>();
        await Parallel.ForEachAsync(pages.Append(files.GetValueOrDefault(ThemeFile)).OfType<ContentEntry>(),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (entry, token) => texts[entry.Path] = await ReadTextAsync(entry, token));

        foreach (var gone in _texts.Keys.Except(texts.Keys)) _texts.TryRemove(gone, out _);

        return new Snapshot(time.GetUtcNow(), files, folders,
            pages.ToDictionary(e => e.Path, e => Parse(texts[e.Path])),
            texts.TryGetValue(ThemeFile, out var theme) ? ThemeCss.Build(theme) : "");
    }

    private async Task<string> ReadTextAsync(ContentEntry entry, CancellationToken ct)
    {
        if (_texts.TryGetValue(entry.Path, out var known) && known.Entry == entry) return known.Text;
        await using var stream = await store.OpenAsync(entry.Path, ct);
        if (stream == null) return ""; // deleted between listing and reading
        var text = await new StreamReader(stream).ReadToEndAsync(ct);
        _texts[entry.Path] = (entry, text);
        return text;
    }

    private static Parsed Parse(string text)
    {
        var (meta, body) = FrontMatter.Parse(text);
        return new Parsed(meta, body);
    }

    /// <summary>A .md file that could be a page: every folder and its own name a valid address word.</summary>
    private static bool IsPageFile(string path)
    {
        if (!path.EndsWith(".md", StringComparison.Ordinal)) return false;
        var segments = path[..^3].Split('/');
        return segments.All(s => ContentPaths.Segment().IsMatch(s));
    }

    // ---- Questions the rest of the app asks ---------------------------------

    /// <summary>The page at an address, or null if there isn't one.</summary>
    public async Task<ContentPage?> GetPageAsync(string? path, CancellationToken ct = default)
    {
        var segments = Split(path);
        if (segments == null) return null;
        var s = await CurrentAsync(ct);
        var relative = string.Join('/', segments);

        // /about is about.md; /blog is blog/index.md, or a list of blog/ if it has no index.
        if (segments.Length > 0 && Load(s, relative + ".md", relative, children: null) is { } file) return file;
        if (segments.Length > 0 && s.Markdown.ContainsKey(relative + ".md")) return null; // a draft

        if (!s.Folders.Contains(relative)) return null;
        var index = relative.Length == 0 ? "index.md" : relative + "/index.md";
        var children = ListFolder(s, relative);
        if (s.Markdown.ContainsKey(index)) return Load(s, index, relative, children);

        // A folder with no index.md: a plain list of what's in it.
        if (segments.Length == 0) return null; // no home page from the CMS; the site's own one shows
        if (children.Count == 0) return null;  // images/ and the like: no pages to list
        return new ContentPage(relative, Titleize(segments[^1]), null, null, null, false, false, "", children);
    }

    /// <summary>Every page, for menus and site maps. Each folder's pages follow the folder itself.</summary>
    public async Task<List<ContentSummary>> AllPagesAsync(CancellationToken ct = default)
    {
        var s = await CurrentAsync(ct);
        var pages = new List<ContentSummary>();
        Walk(s, "", pages);
        return pages;
    }

    /// <summary>The sanitised theme (empty if there's no theme.css, or the CMS is off).</summary>
    public async Task<string> ThemeAsync(CancellationToken ct = default) =>
        Enabled ? (await CurrentAsync(ct)).Theme : "";

    /// <summary>A file to hand out under /content/, or null if there's no such public file.</summary>
    public async Task<ContentEntry?> FindFileAsync(string path, CancellationToken ct = default)
    {
        if (!ContentPaths.IsPublicFile(path)) return null;
        return (await CurrentAsync(ct)).Files.GetValueOrDefault(path);
    }

    public Task<Stream?> OpenAsync(ContentEntry file, CancellationToken ct = default) => store.OpenAsync(file.Path, ct);

    private void Walk(Snapshot s, string folder, List<ContentSummary> into)
    {
        foreach (var child in ListFolder(s, folder))
        {
            into.Add(child);
            if (s.Folders.Contains(child.Path)) Walk(s, child.Path, into);
        }
    }

    /// <summary>The pages directly inside a folder: its .md files and its sub-folders.</summary>
    private List<ContentSummary> ListFolder(Snapshot s, string folder)
    {
        var prefix = folder.Length == 0 ? "" : folder + "/";
        var items = new List<ContentSummary>();

        foreach (var file in s.Markdown.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var name = file[prefix.Length..^3];
            if (name.Contains('/') || name == "index") continue;
            if (Load(s, file, prefix + name, children: null, summaryOnly: true) is { } page) items.Add(page.Summary());
        }

        foreach (var sub in s.Folders.Where(f => f.Length > prefix.Length && f.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var name = sub[prefix.Length..];
            if (name.Contains('/') || !ContentPaths.Segment().IsMatch(name)) continue;
            var index = sub + "/index.md";
            if (s.Markdown.ContainsKey(index))
            {
                // A folder whose index is a draft is hidden along with it.
                if (Load(s, index, sub, null, summaryOnly: true) is { } page) items.Add(page.Summary());
            }
            else if (ListFolder(s, sub).Count > 0) // skip images/ and the like
            {
                items.Add(new ContentSummary(sub, Titleize(name), null, null, null, false));
            }
        }

        return Sort(items);
    }

    private static List<ContentSummary> Sort(List<ContentSummary> items) =>
        items.OrderBy(i => i.Order ?? int.MaxValue) // pages with an order first
             .ThenByDescending(i => i.Date ?? DateOnly.MinValue)
             .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
             .ToList();

    private ContentPage? Load(Snapshot s, string file, string relative, List<ContentSummary>? children, bool summaryOnly = false)
    {
        if (!s.Markdown.TryGetValue(file, out var parsed)) return null;
        var meta = parsed.Meta;
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
            Markdown: summaryOnly ? "" : parsed.Body,
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
        return segments.All(s => ContentPaths.Segment().IsMatch(s)) ? segments : null;
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
