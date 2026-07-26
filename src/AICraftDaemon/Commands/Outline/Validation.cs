namespace DotnetAICraft.Commands.Outline;

internal static class Validation
{
    internal static void ValidateDaemonArgs(string? symbol, string? file, int? line, int? col)
    {
        if (line is not null || col is not null)
            throw new ArgumentException(
                "outline does not accept 'line'/'col'. Use 'symbol' or 'file'.");

        var hasSymbol = !string.IsNullOrWhiteSpace(symbol);
        var hasFile = !string.IsNullOrWhiteSpace(file);

        if (hasSymbol == hasFile)
            throw new ArgumentException(
                "Provide exactly one input mode: either 'symbol' OR 'file'.");
    }
}
