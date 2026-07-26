using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;

namespace DotnetAICraft.Commands.Unused;

internal static class Validation
{
    internal static bool TryNormalizeKind(string? raw, out string normalizedKind, out ErrorInfo? error)
    {
        if (AnalysisCommandMetadata.TryNormalizeUnusedKind(raw, out normalizedKind))
        {
            error = null;
            return true;
        }

        error = new ErrorInfo(
            "INVALID_PARAMS",
            "Invalid 'kind' parameter.",
            new { acceptedValues = AnalysisCommandMetadata.UnusedKindAcceptedValues });
        return false;
    }
}
