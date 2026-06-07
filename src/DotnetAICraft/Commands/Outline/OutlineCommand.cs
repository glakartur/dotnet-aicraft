using System.CommandLine;
using DotnetAICraft.Commands.Outline;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class OutlineCommand
{
    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "Source file whose top-level types to outline (alternative to --symbol)"
        };

        var symbolOpt = new Option<string?>("--symbol")
        {
            Description = "Fully-qualified type name to outline (alternative to --file)"
        };

        var publicOnlyOpt = new Option<bool>("--public-only")
        {
            Description = "List only the consumable/extensible surface: public, internal, protected, protected internal (excludes private and private protected)"
        };

        var includeInheritedOpt = new Option<bool>("--include-inherited")
        {
            Description = "Also list inherited members from the base-class chain, grouped by declaring type"
        };

        var cmd = new Command("outline",
            "List the members a type or file declares, as flat located lines (declared-only by default)")
        {
            solutionOption,
            projectOption,
            fileOpt,
            symbolOpt,
            publicOnlyOpt,
            includeInheritedOpt,
            idleTimeoutOption
        };

        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var file = parseResult.GetValue(fileOpt);
            var symbol = parseResult.GetValue(symbolOpt);
            var publicOnly = parseResult.GetValue(publicOnlyOpt);
            var includeInherited = parseResult.GetValue(includeInheritedOpt);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ExecuteAsync(solutionPath, file, symbol, publicOnly, includeInherited, idleTimeout, format);
        });

        return cmd;
    }
}
