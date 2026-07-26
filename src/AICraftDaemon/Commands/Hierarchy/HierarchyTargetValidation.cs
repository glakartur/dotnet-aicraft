using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Hierarchy;

internal static class HierarchyTargetValidation
{
    internal const string AcceptedTargetKinds = "class, struct, interface, record";

    internal static INamedTypeSymbol EnsureTargetKind(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface } named)
            return named;

        throw new DaemonValidationException(new ErrorInfo(
            "INVALID_TARGET_KIND",
            $"hierarchy targets a class, struct, interface, or record, but '{symbol.ToDisplayString()}' is a {symbol.GetKindName()}.",
            new { resolvedKind = symbol.GetKindName(), acceptedKinds = AcceptedTargetKinds }));
    }
}
