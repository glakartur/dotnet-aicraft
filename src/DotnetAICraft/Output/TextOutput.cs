using System.Globalization;
using System.Text.Json;
using DotnetAICraft.Models;

namespace DotnetAICraft.Output;

public static class TextOutput
{
    private static string OneLine(string? s)
        => s is null ? string.Empty : s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

    // ── Envelope header ──────────────────────────────────────────────────────
    public static void WriteSolutionRootHeader(string absoluteSolutionDir)
    {
        Console.Out.WriteLine($"SolutionRoot: {absoluteSolutionDir}");
        Console.Out.WriteLine();
    }

    // Header printed before each matched symbol's section. A parameterless fully-qualified name can
    // match several overloads, so refs/impls/callers/definition render one labelled group per match.
    public static void WriteMatchHeader(string symbol, string kind)
        => Console.Out.WriteLine($"match: {kind} {symbol}");

    private static void WriteSectionLabel(string label, string? annotation)
    {
        if (annotation is null)
            Console.Out.WriteLine($"{label}:");
        else
            Console.Out.WriteLine($"{label}: ({annotation})");
    }

    // ── Refs ─────────────────────────────────────────────────────────────────
    public static void WriteRefs(IReadOnlyList<ReferenceResult> items, string target, string solution)
    {
        _ = target; _ = solution;
        if (items.Count == 0)
        {
            WriteSectionLabel("references", "no results");
            return;
        }
        WriteSectionLabel("references", null);
        foreach (var r in items)
            Console.Out.WriteLine($"{r.File}:{r.Line}:{r.Col}: {OneLine(r.Context)}");
    }

    // ── Impls ────────────────────────────────────────────────────────────────
    public static void WriteImpls(IReadOnlyList<SymbolResult> items, string target, string solution)
    {
        _ = target; _ = solution;
        if (items.Count == 0)
        {
            WriteSectionLabel("implementations", "no results");
            return;
        }
        WriteSectionLabel("implementations", null);
        foreach (var s in items)
            Console.Out.WriteLine($"{s.File}:{s.Line}:{s.Col}: {s.Kind} {s.FullName}");
    }

    // ── Callers ──────────────────────────────────────────────────────────────
    public static void WriteCallers(CallGraphResult result, string target, string solution)
    {
        _ = target; _ = solution;
        var nodeById = result.Nodes.ToDictionary(n => n.Id);
        var dir = result.Direction ?? "incoming";

        if (string.Equals(dir, "outgoing", StringComparison.OrdinalIgnoreCase))
        {
            WriteCallSection(result, nodeById, "callees", "outgoing");
        }
        else if (string.Equals(dir, "both", StringComparison.OrdinalIgnoreCase))
        {
            WriteCallSection(result, nodeById, "callers", "incoming");
            WriteCallSection(result, nodeById, "callees", "outgoing");
        }
        else
        {
            WriteCallSection(result, nodeById, "callers", "incoming");
        }
    }

    private static void WriteCallSection(
        CallGraphResult result,
        Dictionary<string, CallGraphNode> nodeById,
        string label,
        string relation)
    {
        // Build parent -> children map from edges matching this relation only.
        // For "outgoing" edges, parent = From; for "incoming", parent = To
        // (the node closer to the root). Edges are pre-sorted in DaemonServer,
        // so insertion order is deterministic.
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in result.Edges)
        {
            if (!string.Equals(e.Relation, relation, StringComparison.OrdinalIgnoreCase))
                continue;

            string parentId, childId;
            if (string.Equals(relation, "outgoing", StringComparison.OrdinalIgnoreCase))
            {
                parentId = e.From;
                childId = e.To;
            }
            else
            {
                parentId = e.To;
                childId = e.From;
            }

            if (!children.TryGetValue(parentId, out var list))
            {
                list = new List<string>();
                children[parentId] = list;
            }
            list.Add(childId);
        }

        var rows = new List<string>();
        var descendantCount = 0;
        if (nodeById.ContainsKey(result.RootId) &&
            children.TryGetValue(result.RootId, out var rootChildren))
        {
            // pathIds starts with the root so a child pointing back to the root
            // is detected as a cycle and rendered with a marker.
            var pathIds = new HashSet<string>(StringComparer.Ordinal) { result.RootId };
            foreach (var childId in rootChildren)
                WriteCallersDfs(childId, 0, nodeById, children, pathIds, rows, ref descendantCount);
        }

        if (descendantCount == 0)
        {
            WriteSectionLabel(label, "no results");
            return;
        }
        WriteSectionLabel(label, null);
        foreach (var row in rows)
            Console.Out.WriteLine(row);
    }

    private static void WriteCallersDfs(
        string nodeId,
        int depth,
        Dictionary<string, CallGraphNode> nodeById,
        Dictionary<string, List<string>> children,
        HashSet<string> pathIds,
        List<string> rows,
        ref int descendantCount)
    {
        if (!nodeById.TryGetValue(nodeId, out var node)) return;

        var indent = new string(' ', depth * 2);
        var line = $"{indent}{node.File}:{node.Line}:{node.Col}: {node.Kind} {node.FullName}";

        // Cycle: node already on the active root-to-here path. Emit the row
        // with a marker and stop descending.
        if (pathIds.Contains(nodeId))
        {
            rows.Add(line + " (cycle)");
            descendantCount++;
            return;
        }

        rows.Add(line);
        descendantCount++;

        if (!children.TryGetValue(nodeId, out var childIds)) return;

        pathIds.Add(nodeId);
        foreach (var childId in childIds)
            WriteCallersDfs(childId, depth + 1, nodeById, children, pathIds, rows, ref descendantCount);
        pathIds.Remove(nodeId);
    }

    // ── Symbols ──────────────────────────────────────────────────────────────
    public static void WriteSymbols(SymbolsResultPage page, string pattern, string solution)
    {
        _ = pattern; _ = solution;
        if (page.Items.Count == 0)
        {
            WriteSectionLabel("symbols", "no results");
            return;
        }
        WriteSectionLabel("symbols", page.HasMore ? "more available — use --offset to continue" : null);
        foreach (var s in page.Items)
            Console.Out.WriteLine($"{s.File}:{s.Line}:{s.Col}: {s.Kind} {s.FullName}");
    }

    // ── Unused ───────────────────────────────────────────────────────────────
    public static void WriteUnused(UnusedScanSummary summary, string solution)
    {
        _ = solution;
        var publicOnly = summary.PublicOnly ? "true" : "false";
        var includeGenerated = summary.IncludeGenerated ? "true" : "false";
        var filters = $"scanned {summary.Scanned}, publicOnly={publicOnly}, includeGenerated={includeGenerated}";
        if (summary.Items.Count == 0)
        {
            WriteSectionLabel("unused", $"no results, {filters}");
            return;
        }
        WriteSectionLabel("unused", filters);
        foreach (var u in summary.Items)
        {
            var conf = u.Confidence.ToString("0.##", CultureInfo.InvariantCulture);
            Console.Out.WriteLine($"{u.File}:{u.Line}:{u.Col}: {u.Kind} {u.Symbol} [confidence={conf}] ({u.Reason})");
        }
    }

    // ── Definition ───────────────────────────────────────────────────────────
    public static void WriteDefinition(DefinitionResult def, string solution)
    {
        _ = solution;
        WriteSectionLabel("definition", null);
        if (def.File is not null && def.Line is not null && def.Col is not null)
            Console.Out.WriteLine($"{def.File}:{def.Line}:{def.Col}: {def.Kind} {def.FullName}");
        else
            Console.Out.WriteLine($"{def.Kind} {def.FullName}");
    }

    // ── Outline ────────────────────────────────────────────────────────────────
    public static void WriteOutlineEmpty()
        => WriteSectionLabel("outline", "no results");

    public static void WriteOutline(OutlineResult result, string solution)
    {
        _ = solution;

        var filters = new List<string>();
        if (result.PublicOnly) filters.Add("publicOnly");
        if (result.IncludeInherited) filters.Add("includeInherited");
        var annotation = filters.Count == 0
            ? (result.Declared.Count == 0 ? "no declared members" : null)
            : string.Join(", ", filters) + (result.Declared.Count == 0 ? ", no declared members" : "");

        WriteSectionLabel("outline", annotation);

        var containerName = result.Container;
        foreach (var member in result.Declared)
        {
            // R9: each declared member is a flat located line; nested members carry their declaring type.
            var origin = string.Equals(member.DeclaringType, containerName, StringComparison.Ordinal)
                ? string.Empty
                : $"  [{member.DeclaringType}]";
            var tag = member.Tag is null ? string.Empty : $"  ({member.Tag})";
            Console.Out.WriteLine($"{member.File}:{member.Line}:{member.Col}: {member.Signature}{origin}{tag}");
        }

        foreach (var group in result.Inherited)
        {
            var assembly = group.Assembly is null ? string.Empty : $" [{group.Assembly}]";
            Console.Out.WriteLine($"inherited from {group.DeclaringType}{assembly}:");
            foreach (var member in group.Members)
            {
                var tag = member.Tag is null ? string.Empty : $"  ({member.Tag})";
                Console.Out.WriteLine($"  {member.Signature}{tag}");
            }
        }
    }

    // ── Source ─────────────────────────────────────────────────────────────────
    public static void WriteSource(SourceResult result, string solution)
    {
        _ = solution;
        if (!result.HasSource)
        {
            WriteSectionLabel("source", result.Note ?? "no source available");
            return;
        }

        WriteSectionLabel("source", result.Blocks.Count > 1 ? $"{result.Blocks.Count} parts" : null);
        for (var i = 0; i < result.Blocks.Count; i++)
        {
            var block = result.Blocks[i];
            Console.Out.WriteLine($"{block.File}:{block.StartLine}-{block.EndLine}:");
            Console.Out.WriteLine(block.Text.Trim('\r', '\n'));
            if (i < result.Blocks.Count - 1)
                Console.Out.WriteLine();
        }
    }

    // ── Describe ───────────────────────────────────────────────────────────────
    public static void WriteDescribe(DescribeCard card, string solution)
    {
        _ = solution;
        WriteSectionLabel("describe", null);
        Console.Out.WriteLine(card.Signature);

        if (card.File is not null && card.Line is not null && card.Col is not null)
            Console.Out.WriteLine($"  location: {card.File}:{card.Line}:{card.Col}");
        else if (card.Assembly is not null)
            Console.Out.WriteLine($"  location: <metadata> {card.Assembly}");

        if (card.ReturnType is not null)
            Console.Out.WriteLine($"  returns: {card.ReturnType}");

        if (card.Parameters is { Count: > 0 })
        {
            Console.Out.WriteLine("  params:");
            foreach (var p in card.Parameters)
            {
                var def = p.DefaultValue is null ? string.Empty : $" = {p.DefaultValue}";
                Console.Out.WriteLine($"    {p.Type} {p.Name}{def}");
            }
        }

        if (card.ConstantValue is not null)
            Console.Out.WriteLine($"  constant: {card.ConstantValue}");

        if (card.Modifiers is { Count: > 0 })
            Console.Out.WriteLine($"  modifiers: {string.Join(" ", card.Modifiers)}");

        if (card.Attributes is { Count: > 0 })
            Console.Out.WriteLine($"  attributes: {string.Join(", ", card.Attributes)}");

        if (!string.IsNullOrEmpty(card.Documentation))
        {
            Console.Out.WriteLine("  doc:");
            foreach (var docLine in card.Documentation.Split('\n'))
                Console.Out.WriteLine($"    {docLine}");
        }

        if (card.Siblings is { Count: > 0 })
        {
            Console.Out.WriteLine("  siblings:");
            foreach (var sibling in card.Siblings)
                Console.Out.WriteLine($"    {sibling}");
        }
    }

    // ── Diagnostics ──────────────────────────────────────────────────────────
    public static void WriteDiagnostics(IReadOnlyList<DiagnosticResult> items, string solution)
    {
        _ = solution;
        if (items.Count == 0)
        {
            WriteSectionLabel("diagnostics", "no results");
            return;
        }
        WriteSectionLabel("diagnostics", null);
        foreach (var d in items)
        {
            var sev = d.Severity?.ToLowerInvariant() ?? string.Empty;
            if (d.File is not null && d.Line is not null && d.Col is not null)
                Console.Out.WriteLine($"{sev} {d.File}:{d.Line}:{d.Col} [{d.Id}]: {OneLine(d.Message)}");
            else
                Console.Out.WriteLine($"{sev} {d.Project} [{d.Id}]: {OneLine(d.Message)}");
        }
    }

    // ── Rename ───────────────────────────────────────────────────────────────
    public static void WriteRename(RenameResult result, string solution)
    {
        var word = result.Changes.Count == 1 ? "change" : "changes";
        var status = result.Applied ? "applied" : "dry-run";
        Console.Out.WriteLine(
            $"{result.Changes.Count} {word} for {result.Symbol} -> {result.NewName} ({status}) in {solution}");
        if (result.Changes.Count == 0) return;
        Console.Out.WriteLine();
        foreach (var c in result.Changes)
            Console.Out.WriteLine($"{c.File}:{c.Line}:{c.Col}: {c.OldText} -> {c.NewText}");
    }

    // ── Server status ────────────────────────────────────────────────────────
    public static void WriteServerStatus(DaemonStatus status)
    {
        Console.Out.WriteLine($"{status.SolutionPath} [{status.LoadState}]");
        Console.Out.WriteLine($"Running: {(status.Running ? "true" : "false")}");
        Console.Out.WriteLine($"Projects: {status.Projects}");
        Console.Out.WriteLine($"Documents: {status.Documents}");
        Console.Out.WriteLine($"LoadedAt: {status.LoadedAt:O}");
        Console.Out.WriteLine($"Uptime: {status.Uptime}");
        if (status.LastLoadAttemptAt is not null)
            Console.Out.WriteLine($"LastLoadAttemptAt: {status.LastLoadAttemptAt:O}");
        if (status.LastLoadErrorCode is not null || status.LastLoadErrorMessage is not null)
            Console.Out.WriteLine($"LastLoadError: {status.LastLoadErrorCode}: {status.LastLoadErrorMessage}");
    }

    // ── Error ────────────────────────────────────────────────────────────────
    public static void WriteError(string code, string message, object? details)
    {
        Console.Out.WriteLine($"error {code}: {message}");
        if (details is null) return;

        // Try to extract a "hint" property first, regardless of carrier shape.
        var json = JsonOutput.Serialize(details);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("hint", out var hint) && hint.ValueKind == JsonValueKind.String)
                {
                    Console.Out.WriteLine($"hint: {hint.GetString()}");
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "hint", StringComparison.Ordinal))
                        continue;
                    string value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "null",
                        _ => prop.Value.GetRawText()
                    };
                    Console.Out.WriteLine($"  {prop.Name}: {value}");
                }
            }
        }
        catch
        {
            // ignore; fall back silently
        }
    }
}
