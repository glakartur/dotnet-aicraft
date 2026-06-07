using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Outline;

internal static class OutputMapping
{
    internal static OutlineResult Map(
        INamedTypeSymbol container,
        string solutionDir,
        bool publicOnly,
        bool includeInherited)
    {
        var declaredSymbols = new List<(ISymbol Symbol, INamedTypeSymbol DeclaringType)>();
        CollectDeclared(container, publicOnly, declaredSymbols);

        var declared = declaredSymbols
            .Select(d => MapDeclared(d.Symbol, d.DeclaringType, container, solutionDir))
            .OfType<OutlineMember>()
            .OrderBy(m => m.File, StringComparer.Ordinal)
            .ThenBy(m => m.Line)
            .ThenBy(m => m.Col)
            .ToList();

        // Override/new-shadow comparison is against the container's OWN members only — a nested type's
        // member that happens to share a name with a base member must not mask it.
        var inherited = includeInherited
            ? CollectInherited(
                container,
                publicOnly,
                declaredSymbols
                    .Where(d => SymbolEqualityComparer.Default.Equals(d.DeclaringType, container))
                    .Select(d => d.Symbol)
                    .ToList())
            : [];

        return new OutlineResult(
            Container: container.ToDisplayString(),
            Kind: container.GetKindName(),
            PublicOnly: publicOnly,
            IncludeInherited: includeInherited,
            Declared: declared,
            Inherited: inherited);
    }

    private static OutlineMember? MapDeclared(
        ISymbol symbol,
        INamedTypeSymbol declaringType,
        INamedTypeSymbol container,
        string solutionDir)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null)
            return null; // a declared-but-source-less member is not addressable as a located line

        var (file, line, col) = location.GetFileLineColRelative(solutionDir);
        return new OutlineMember(
            File: file,
            Line: line,
            Col: col,
            DeclaringType: declaringType.ToDisplayString(),
            Signature: SignatureFor(symbol),
            Tag: null);
    }

    private static void CollectDeclared(
        INamedTypeSymbol container,
        bool publicOnly,
        List<(ISymbol, INamedTypeSymbol)> accumulator)
    {
        foreach (var member in container.GetMembers())
        {
            if (!IsListable(member))
                continue;
            if (publicOnly && !PassesPublicOnly(member))
                continue;

            accumulator.Add((member, container));

            // R8: recurse into nested types so their members are listed too.
            if (member is INamedTypeSymbol nested)
                CollectDeclared(nested, publicOnly, accumulator);
        }
    }

    // D10: walk the base-class chain only; group members under their declaring type; suppress members
    // already overridden by a declared member; tag `new`-shadowed members. System.Object lands last.
    private static List<OutlineInheritedGroup> CollectInherited(
        INamedTypeSymbol container,
        bool publicOnly,
        IReadOnlyList<ISymbol> declared)
    {
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var member in declared)
            for (var baseSymbol = OverriddenSymbol(member); baseSymbol is not null; baseSymbol = OverriddenSymbol(baseSymbol))
                overridden.Add(baseSymbol.OriginalDefinition);

        var declaredKeys = new HashSet<string>(StringComparer.Ordinal);
        var declaredOverrideKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in declared)
        {
            var key = SignatureKey(member);
            declaredKeys.Add(key);
            if (member.IsOverride)
                declaredOverrideKeys.Add(key);
        }

        var groups = new List<OutlineInheritedGroup>();
        for (var baseType = container.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var members = new List<OutlineInheritedMember>();
            foreach (var member in baseType.GetMembers())
            {
                if (!IsListable(member) || member is INamedTypeSymbol)
                    continue;
                if (publicOnly && !PassesPublicOnly(member))
                    continue;
                if (overridden.Contains(member.OriginalDefinition))
                    continue; // shown as the declared override

                var key = SignatureKey(member);
                var tag = declaredKeys.Contains(key) && !declaredOverrideKeys.Contains(key)
                    ? "hidden by new"
                    : null;

                members.Add(new OutlineInheritedMember(SignatureFor(member), tag));
            }

            if (members.Count > 0)
            {
                var fromSource = baseType.Locations.Any(l => l.IsInSource);
                groups.Add(new OutlineInheritedGroup(
                    DeclaringType: baseType.ToDisplayString(),
                    Assembly: fromSource ? null : baseType.ContainingAssembly?.Name,
                    Members: members));
            }
        }

        return groups;
    }

    private static string SignatureFor(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } d => SymbolDisplayFormats.FormatDelegateSignature(d),
        INamedTypeSymbol type => SymbolDisplayFormats.FormatTypeHeader(type),
        _ => SymbolDisplayFormats.FormatMemberSignature(symbol, SymbolDisplayFormats.OutlineMemberFormat)
    };

    private static bool IsListable(ISymbol member)
    {
        if (member.IsImplicitlyDeclared)
            return false;

        return member switch
        {
            IMethodSymbol method => method.MethodKind is not (
                MethodKind.PropertyGet or MethodKind.PropertySet or
                MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise),
            IFieldSymbol field => field.AssociatedSymbol is null, // skip property/event backing fields
            IPropertySymbol or IEventSymbol or INamedTypeSymbol => true,
            _ => false
        };
    }

    private static bool PassesPublicOnly(ISymbol member) => member.DeclaredAccessibility is
        Accessibility.Public or Accessibility.Internal or
        Accessibility.Protected or Accessibility.ProtectedOrInternal;

    private static ISymbol? OverriddenSymbol(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.OverriddenMethod,
        IPropertySymbol property => property.OverriddenProperty,
        IEventSymbol @event => @event.OverriddenEvent,
        _ => null
    };

    private static string SignatureKey(ISymbol symbol) => symbol is IMethodSymbol method
        ? $"{method.Name}({string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString()))})"
        : symbol.Name;
}
