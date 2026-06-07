using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotnetAICraft.Roslyn;

/// <summary>
/// Converts the XML returned by <see cref="Microsoft.CodeAnalysis.ISymbol.GetDocumentationCommentXml(string,bool,System.Threading.CancellationToken)"/>
/// into cleaned plain text for the <c>describe</c> card. Walks child nodes rather than reading
/// <c>.Value</c> (which would drop <c>&lt;see cref&gt;</c> targets and paragraph structure), trims the
/// per-line <c>///</c> indentation, preserves <c>&lt;para&gt;</c>/<c>&lt;code&gt;</c>/<c>&lt;list&gt;</c>
/// structure, and renders crefs/paramrefs as their simple names. <c>&lt;inheritdoc/&gt;</c> is left as a
/// marker — Roslyn does not expand it. See plan decision D5.
/// </summary>
public static class XmlDocCleaner
{
    /// <summary>
    /// Returns cleaned plain text, <c>"(inherited documentation)"</c> when the only content is
    /// <c>&lt;inheritdoc/&gt;</c>, or <c>null</c> when there is no documentation. Never throws — malformed
    /// XML falls back to tag-stripped raw text.
    /// </summary>
    public static string? Clean(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            // GetDocumentationCommentXml wraps content in <member name="..."> (or <doc>); walking the
            // root's child nodes descends into that wrapper.
            var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);

            var sb = new StringBuilder();
            RenderNodes(root.Nodes(), sb);
            var text = Normalize(sb.ToString());

            if (text.Length == 0)
                return root.Descendants("inheritdoc").Any() ? "(inherited documentation)" : null;

            return text;
        }
        catch
        {
            // Strip well-formed tags and any trailing unterminated "<tag" (closing '>' optional).
            var stripped = AnyTag.Replace(xml, string.Empty).Trim();
            return stripped.Length == 0 ? null : stripped;
        }
    }

    private static void RenderNodes(IEnumerable<XNode> nodes, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;

                case XElement element:
                    RenderElement(element, sb);
                    break;
            }
        }
    }

    private static void RenderElement(XElement element, StringBuilder sb)
    {
        switch (element.Name.LocalName)
        {
            case "see":
            case "seealso":
                sb.Append(SimpleName(
                    element.Attribute("cref")?.Value
                    ?? element.Attribute("langword")?.Value
                    ?? element.Attribute("href")?.Value
                    ?? element.Value));
                break;

            case "paramref":
            case "typeparamref":
                sb.Append(element.Attribute("name")?.Value ?? element.Value);
                break;

            case "para":
                sb.Append("\n\n");
                RenderNodes(element.Nodes(), sb);
                sb.Append("\n\n");
                break;

            case "code":
                sb.Append("\n\n").Append(Sentinel).Append(Dedent(element.Value)).Append(Sentinel).Append("\n\n");
                break;

            case "list":
                sb.Append('\n');
                foreach (var item in element.Elements("item"))
                {
                    sb.Append("\n- ");
                    RenderNodes(item.Nodes(), sb);
                }
                sb.Append('\n');
                break;

            // Inline formatting elements (<c>, <b>, <i>, …): render their content in place with no break.
            case "c":
            case "b":
            case "i":
            case "em":
            case "strong":
                RenderNodes(element.Nodes(), sb);
                break;

            // Block-level sections (summary, remarks, returns, param, exception, typeparam, example, …):
            // render their content followed by a blank-line separator.
            default:
                RenderNodes(element.Nodes(), sb);
                sb.Append("\n\n");
                break;
        }
    }

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex BlankRuns = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex AnyTag = new("<[^>]*>?", RegexOptions.Compiled);

    // Marks the start/end of a verbatim <code> block so Normalize leaves its interior whitespace intact.
    private const string Sentinel = "\u0001"; // SOH — never present in real doc text

    private static string Normalize(string raw)
    {
        // Split into protected (code) and unprotected segments on the sentinel boundary.
        var segments = raw.Split(Sentinel);
        var sb = new StringBuilder();

        for (var i = 0; i < segments.Length; i++)
        {
            // Odd indices are code interiors (between a pair of sentinels) — keep verbatim.
            if (i % 2 == 1)
            {
                sb.Append(segments[i].TrimEnd('\n'));
                continue;
            }

            // Prose: collapse intra-line whitespace runs (newline + /// indentation) to single spaces,
            // preserving paragraph (blank-line) breaks.
            foreach (var paragraph in segments[i].Split("\n\n"))
            {
                var collapsed = WhitespaceRun.Replace(paragraph, " ").Trim();
                if (collapsed.Length > 0)
                    sb.Append(collapsed).Append("\n\n");
            }
        }

        return CollapseBlankRuns(sb.ToString()).Trim();
    }

    private static string CollapseBlankRuns(string text)
        => BlankRuns.Replace(text.Replace("\r\n", "\n"), "\n\n");

    /// <summary>Removes the common leading whitespace shared by all non-blank lines of a code block.</summary>
    private static string Dedent(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n')
            .SkipWhile(string.IsNullOrWhiteSpace)
            .ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0)
            return string.Empty;

        var minIndent = lines
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join("\n", lines.Select(l => l.Length >= minIndent ? l[minIndent..] : l.TrimStart()));
    }

    /// <summary>
    /// Reduces a doc-comment reference to its simple name: strips the <c>T:</c>/<c>M:</c>/etc. doc-ID
    /// prefix, any parameter list, and namespace/containing-type qualification.
    /// </summary>
    private static string SimpleName(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var value = reference.Trim();

        // Strip a doc-ID prefix like "T:" / "M:" / "P:" / "F:" / "E:" / "!:".
        if (value.Length > 2 && value[1] == ':')
            value = value[2..];

        // Drop a parameter signature.
        var paren = value.IndexOf('(');
        if (paren >= 0)
            value = value[..paren];

        // Take the last dotted segment.
        var lastDot = value.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < value.Length - 1)
            value = value[(lastDot + 1)..];

        return value;
    }
}
