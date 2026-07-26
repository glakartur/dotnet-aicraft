using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Shared;

internal static class AnalysisCommandMetadata
{
    internal const string DiagnosticsSeverityAcceptedValues = "all | error | warning | info | hidden";
    internal const string SymbolsKindAcceptedValues = "all | type | member | namespace | class | interface | struct | enum | delegate | method | constructor | property | field | event";
    internal const string UnusedKindAcceptedValues = SymbolsKindAcceptedValues;
    internal const string CallGraphDirectionAcceptedValues = "incoming | outgoing | both";
    internal const string CallGraphDefaultDirection = "incoming";
    internal const int CallGraphDefaultDepth = 1;
    internal const int SymbolsDefaultLimit = 200;
    internal const int SymbolsDefaultOffset = 0;
    internal const int SymbolsMaxLimit = 2000;

    private static readonly HashSet<string> AcceptedSymbolKinds =
    [
        "all", "type", "member", "namespace", "class", "interface", "struct", "enum",
        "delegate", "method", "constructor", "property", "field", "event"
    ];

    internal static bool TryParseCallGraphDirection(string? raw, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(raw)
            ? CallGraphDefaultDirection
            : raw.Trim().ToLowerInvariant();

        return normalized is "incoming" or "outgoing" or "both";
    }

    internal static bool TryNormalizeCallGraphDepth(
        int? depth,
        out int normalizedDepth,
        out ErrorInfo? error)
    {
        normalizedDepth = depth ?? CallGraphDefaultDepth;

        if (normalizedDepth < 1)
        {
            error = new ErrorInfo(
                "INVALID_PARAMS",
                "Parameter 'depth' must be greater than or equal to 1.",
                new { min = 1, @default = CallGraphDefaultDepth });
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryParseDiagnosticsSeverity(
        string? raw,
        out DiagnosticSeverity? severity,
        out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(raw)
            ? "all"
            : raw.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "all":
                severity = null;
                return true;
            case "error":
                severity = DiagnosticSeverity.Error;
                return true;
            case "warning":
                severity = DiagnosticSeverity.Warning;
                return true;
            case "info":
                severity = DiagnosticSeverity.Info;
                return true;
            case "hidden":
                severity = DiagnosticSeverity.Hidden;
                return true;
            default:
                severity = null;
                return false;
        }
    }

    internal static bool TryNormalizeSymbolsKind(string? raw, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(raw)
            ? "all"
            : raw.Trim().ToLowerInvariant();

        return AcceptedSymbolKinds.Contains(normalized);
    }

    internal static bool TryNormalizeUnusedKind(string? raw, out string normalized)
        => TryNormalizeSymbolsKind(raw, out normalized);

    internal static bool TryNormalizeSymbolsPagination(
        int? limit,
        int? offset,
        out int normalizedLimit,
        out int normalizedOffset,
        out ErrorInfo? error)
    {
        normalizedLimit = limit ?? SymbolsDefaultLimit;
        normalizedOffset = offset ?? SymbolsDefaultOffset;

        if (normalizedLimit <= 0)
        {
            error = new ErrorInfo(
                "INVALID_PARAMS",
                "Parameter 'limit' must be greater than 0.",
                new { min = 1, max = SymbolsMaxLimit, @default = SymbolsDefaultLimit });
            return false;
        }

        if (normalizedOffset < 0)
        {
            error = new ErrorInfo(
                "INVALID_PARAMS",
                "Parameter 'offset' must be greater than or equal to 0.",
                new { min = 0, @default = SymbolsDefaultOffset });
            return false;
        }

        if (normalizedLimit > SymbolsMaxLimit)
            normalizedLimit = SymbolsMaxLimit;

        error = null;
        return true;
    }
}
