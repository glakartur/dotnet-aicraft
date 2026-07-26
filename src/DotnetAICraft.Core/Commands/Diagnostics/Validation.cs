using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;

namespace DotnetAICraft.Commands.Diagnostics;

internal static class Validation
{
    internal static bool TryNormalizeSeverity(
        string severity,
        string acceptedSeverities,
        out string normalizedSeverity,
        out ErrorInfo? error)
    {
        if (AnalysisCommandMetadata.TryParseDiagnosticsSeverity(severity, out _, out normalizedSeverity))
        {
            error = null;
            return true;
        }

        error = new ErrorInfo(
            "INVALID_PARAMS",
            "Invalid 'severity' parameter.",
            new { acceptedValues = acceptedSeverities });
        return false;
    }
}
