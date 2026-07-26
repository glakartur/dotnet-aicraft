using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;

namespace DotnetAICraft.Commands.Symbols;

internal static class Validation
{
    internal static bool TryNormalizeKind(string? raw, out string normalizedKind, out ErrorInfo? error)
    {
        if (AnalysisCommandMetadata.TryNormalizeSymbolsKind(raw, out normalizedKind))
        {
            error = null;
            return true;
        }

        error = new ErrorInfo(
            "INVALID_PARAMS",
            "Invalid 'kind' parameter.",
            new { acceptedValues = AnalysisCommandMetadata.SymbolsKindAcceptedValues });
        return false;
    }

    internal static bool TryNormalizePagination(
        int? limit,
        int? offset,
        out int normalizedLimit,
        out int normalizedOffset,
        out ErrorInfo? error)
        => AnalysisCommandMetadata.TryNormalizeSymbolsPagination(limit, offset, out normalizedLimit, out normalizedOffset, out error);
}
