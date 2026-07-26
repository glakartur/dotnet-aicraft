using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotnetAICraft.Commands.Hierarchy;

/// <summary>
/// Recursively builds the nested <see cref="HierarchyNode"/> tree by finding the <em>direct</em>
/// relations of a type in the requested direction and recursing (plan D4). Parent/child structure
/// and <c>--max-depth</c> fall out of the recursion naturally. Inheritance is acyclic, so no cycle
/// guard is needed (D8); multi-path (diamond) relations are emitted faithfully once per path.
/// </summary>
internal static class OutputMapping
{
    internal static async Task<HierarchyNode> BuildNodeAsync(
        Solution solution,
        INamedTypeSymbol type,
        string direction,
        bool includeFramework,
        int maxDepth,
        int depth,
        string solutionDir,
        CancellationToken ct)
    {
        var relations = await DirectRelationsAsync(solution, type, direction, includeFramework, ct);

        // Depth cap (D9): a node at the cap that would have children in the full tree is emitted
        // truncated with its children elided; a genuine leaf carries Truncated = false.
        if (depth >= maxDepth)
            return Locate(type, solutionDir, truncated: relations.Count > 0, children: []);

        // Deterministic order for stable output, matching the determinism callers relies on.
        var ordered = relations
            .OrderBy(r => r.ToDisplayString(), StringComparer.Ordinal)
            .ToList();

        var children = new List<HierarchyNode>(ordered.Count);
        foreach (var relation in ordered)
            children.Add(await BuildNodeAsync(
                solution, relation, direction, includeFramework, maxDepth, depth + 1, solutionDir, ct));

        return Locate(type, solutionDir, truncated: false, children: children);
    }

    /// <summary>
    /// The immediate base or derived types of <paramref name="type"/> in the requested direction
    /// (plan D4). <c>up</c> applies the framework gate (D6); <c>down</c> within the solution never
    /// reaches metadata types.
    /// </summary>
    private static async Task<IReadOnlyList<INamedTypeSymbol>> DirectRelationsAsync(
        Solution solution,
        INamedTypeSymbol type,
        string direction,
        bool includeFramework,
        CancellationToken ct)
    {
        if (direction == "down")
        {
            // R8: interface down = derived interfaces only (implementing classes remain `impls`).
            if (type.TypeKind == TypeKind.Interface)
            {
                var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(
                    type, solution, transitive: false, projects: null, ct);
                return derivedInterfaces.ToList();
            }

            // R7: class/struct/record down = derived types. Direct-only here (transitive: false);
            // the recursion in BuildNodeAsync supplies transitivity. (Structs are sealed → empty.)
            var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(
                type, solution, transitive: false, projects: null, ct);
            return derivedClasses.ToList();
        }

        // up
        if (type.TypeKind == TypeKind.Interface)
            // R9: interfaces this interface extends (constructed where applicable, D7/R13).
            return ApplyFrameworkGate(type.Interfaces, includeFramework);

        // R7/D5: class/struct/record up = base-class chain only (not implemented interfaces).
        // BaseType is the constructed base (e.g. Box<int>), giving R13 node identity for free.
        return type.BaseType is { } baseType
            ? ApplyFrameworkGate([baseType], includeFramework)
            : [];
    }

    /// <summary>
    /// Framework/BCL omission (D6, R10/R11): a base with no in-source location is metadata. By default
    /// it (and everything above it) is omitted; <c>--include-framework</c> keeps it so the chain walks
    /// up to <c>object</c>, those metadata nodes rendered location-less.
    /// </summary>
    private static IReadOnlyList<INamedTypeSymbol> ApplyFrameworkGate(
        IEnumerable<INamedTypeSymbol> relations, bool includeFramework)
        => includeFramework
            ? relations.ToList()
            : relations.Where(IsInSource).ToList();

    private static bool IsInSource(ISymbol symbol)
        => symbol.Locations.Any(l => l.IsInSource);

    private static HierarchyNode Locate(
        INamedTypeSymbol type,
        string solutionDir,
        bool truncated,
        IReadOnlyList<HierarchyNode> children)
    {
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);
        var (file, line, col) = location is not null
            ? location.GetFileLineColRelative(solutionDir)
            : ("", 0, 0);

        return new HierarchyNode(
            Name: type.Name,
            FullName: type.ToDisplayString(),
            Kind: type.GetKindName(),
            File: file,
            Line: line,
            Col: col,
            ContainingType: type.ContainingType?.ToDisplayString(),
            ContainingNamespace: type.ContainingNamespace?.ToDisplayString(),
            Truncated: truncated,
            Children: children);
    }
}
