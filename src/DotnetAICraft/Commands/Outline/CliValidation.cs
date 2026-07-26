namespace DotnetAICraft.Commands.Outline;

internal static class CliValidation
{
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
}
