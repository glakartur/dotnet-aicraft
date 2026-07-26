using DotnetAICraft.Models;

namespace DotnetAICraft.Commands.Hierarchy;

internal static class CliValidation
{
    internal const string DirectionAcceptedValues = "up | down";

    internal const int UnboundedMaxDepth = int.MaxValue;

    internal static void ValidateCliModeArgs(FileInfo? file, int? line, int? col, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) && (file is null || line is null || col is null))
        {
            throw new ArgumentException(
                "Provide either --symbol OR all of --file --line --col");
        }
    }

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
}
