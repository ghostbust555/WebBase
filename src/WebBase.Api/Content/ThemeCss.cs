using System.Text;
using System.Text.RegularExpressions;

namespace WebBase.Api.Content;

/// <summary>
/// The site's colours and other style settings from the CMS: content/theme.css.
///
/// Only CSS variables are taken from it - the same ones at the top of
/// client/src/style.css - one set for everyone and, optionally, one for dark
/// mode:
///
///   :root { --accent: #0f766e; --radius: 6px; }
///   @media (prefers-color-scheme: dark) { :root { --accent: #5eead4; } }
///
/// Anything else in the file (selectors, rules) is left out, so the file can
/// change how the site looks but not how it works. Served at
/// /api/content/theme.css, which index.html always links.
/// </summary>
public static partial class ThemeCss
{
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"@media\s*\(\s*prefers-color-scheme\s*:\s*dark\s*\)\s*\{\s*:root\s*\{(?<body>[^{}]*)\}\s*\}")]
    private static partial Regex DarkBlock();

    [GeneratedRegex(@"(?<![\w-]):root\s*\{(?<body>[^{}]*)\}")]
    private static partial Regex RootBlock();

    [GeneratedRegex(@"^--[a-zA-Z0-9_-]+$")]
    private static partial Regex VariableName();

    /// <summary>The variables of the file, as safe CSS (empty if there are none).</summary>
    public static string Build(string source)
    {
        var css = Comments().Replace(source, "");
        var dark = DarkBlock().Matches(css).SelectMany(m => Variables(m.Groups["body"].Value)).ToList();
        var light = RootBlock().Matches(DarkBlock().Replace(css, "")).SelectMany(m => Variables(m.Groups["body"].Value)).ToList();

        // :root:root outranks style.css's own :root, wherever each file loads.
        var output = new StringBuilder();
        if (light.Count > 0) output.Append(":root:root {\n").AppendJoin("", light.Select(v => $"  {v};\n")).Append("}\n");
        if (dark.Count > 0)
            output.Append("@media (prefers-color-scheme: dark) {\n  :root:root {\n")
                  .AppendJoin("", dark.Select(v => $"    {v};\n")).Append("  }\n}\n");
        return output.ToString();
    }

    private static IEnumerable<string> Variables(string block)
    {
        foreach (var declaration in block.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0) continue;
            var name = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();
            if (!VariableName().IsMatch(name) || value.Length == 0 || value.Length > 500) continue;
            // Nothing that could close the block or the stylesheet early.
            if (value.IndexOfAny(['{', '}', '<', '>', '\\']) >= 0) continue;
            yield return $"{name}: {value}";
        }
    }
}
