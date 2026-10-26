namespace WebBase.FileStorage;

/// <summary>Path rules every file store follows, in one place.</summary>
public static class FilePaths
{
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
    /// the store's folder (the Azure Blob SDK does, and reading Google Cloud
    /// Storage through its test stand-in did too; the contract tests catch it).
    /// </summary>
    public static bool IsSafeRelative(string path) =>
        path.Length > 0 && !path.Contains('\\') &&
        path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");
}
