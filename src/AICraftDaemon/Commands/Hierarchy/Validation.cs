namespace DotnetAICraft.Commands.Hierarchy;

internal static class Validation
{
    internal static void ValidateDaemonModeArgs(string? symbol, string? file, int? line, int? col)
    {
        if (string.IsNullOrWhiteSpace(symbol) && (string.IsNullOrWhiteSpace(file) || line is null || col is null))
        {
            throw new ArgumentException("Provide either 'symbol' OR all of 'file'+'line'+'col'.");
        }
    }

}
