using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Hierarchy;

internal static class Validation
{
    internal const string DirectionAcceptedValues = "up | down";

    /// <summary>
    /// Sentinel for an absent <c>--max-depth</c>: traverse the full (finite, acyclic) inheritance
    /// graph. Inheritance depth never approaches <see cref="int.MaxValue"/>, so the cap never fires.
    /// </summary>
    internal const int UnboundedMaxDepth = int.MaxValue;

    internal const string AcceptedTargetKinds = "class, struct, interface, record";

    internal static void ValidateCliModeArgs(FileInfo? file, int? line, int? col, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) && (file is null || line is null || col is null))
        {
            throw new ArgumentException(
                "Provide either --symbol OR all of --file --line --col");
        }
    }

    internal static void ValidateDaemonModeArgs(string? symbol, string? file, int? line, int? col)
    {
        if (string.IsNullOrWhiteSpace(symbol) && (string.IsNullOrWhiteSpace(file) || line is null || col is null))
        {
            throw new ArgumentException("Provide either 'symbol' OR all of 'file'+'line'+'col'.");
        }
    }

    /// <summary>
    /// Parses <c>--direction</c> for <c>hierarchy</c>: required, case-insensitive <c>up</c>/<c>down</c>
    /// (unlike <c>callers</c>, which defaults to <c>incoming</c>). Null/empty/unknown → INVALID_PARAMS.
    /// </summary>
    internal static bool TryParseDirection(string? raw, out string normalized, out ErrorInfo? error)
    {
        normalized = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is "up" or "down")
        {
            error = null;
            return true;
        }

        normalized = string.Empty;
        error = new ErrorInfo(
            "INVALID_PARAMS",
            "Invalid 'direction' parameter.",
            new { acceptedValues = DirectionAcceptedValues });
        return false;
    }

    /// <summary>
    /// Normalizes <c>--max-depth</c>: <c>null</c> → <see cref="UnboundedMaxDepth"/>; values <c>&lt; 1</c>
    /// → INVALID_PARAMS; otherwise passthrough.
    /// </summary>
    internal static bool TryNormalizeMaxDepth(int? raw, out int normalized, out ErrorInfo? error)
    {
        normalized = raw ?? UnboundedMaxDepth;

        if (normalized < 1)
        {
            error = new ErrorInfo(
                "INVALID_PARAMS",
                "Parameter 'max-depth' must be greater than or equal to 1.",
                new { min = 1 });
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Accepts only named types whose kind is class/struct/interface (records surface as class). Any
    /// other resolved symbol (enum, delegate, namespace, member) throws
    /// <see cref="DaemonValidationException"/> with <c>INVALID_TARGET_KIND</c> naming the resolved kind
    /// and the accepted kinds — never an empty tree. See plan D10.
    /// </summary>
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
