namespace DotnetAICraft.Commands.Outline;

internal static class Validation
{
    // D7: outline diverges from the shared location contract — it accepts --symbol <type> XOR a bare
    // --file <path>, and rejects --line/--col.
    internal static void ValidateCliArgs(FileInfo? file, int? line, int? col, string? symbol)
    {
        if (line is not null || col is not null)
            throw new ArgumentException(
                "outline does not accept --line/--col. Use --symbol <type> or --file <path>.");

        var hasSymbol = !string.IsNullOrWhiteSpace(symbol);
        var hasFile = file is not null;

        if (hasSymbol == hasFile)
            throw new ArgumentException(
                "Provide exactly one input mode: either --symbol <type> OR --file <path>.");
    }

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
