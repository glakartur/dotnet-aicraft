using System.Runtime.InteropServices;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Shared;

internal static class SolutionPathResolver
{
    private static readonly string[] DiscoveryTiers = { "*.slnx", "*.sln", "*.csproj" };
    private static readonly string[] SearchedExtensions = { ".slnx", ".sln", ".csproj" };

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static string? Resolve(
        FileInfo? solution,
        FileInfo? project,
        OutputFormat format,
        string? cwdOverride = null)
    {
        if (solution is not null && project is not null)
        {
            var solutionFull = Path.GetFullPath(solution.FullName);
            var projectFull = Path.GetFullPath(project.FullName);
            if (!string.Equals(solutionFull, projectFull, PathComparison))
            {
                CommandHelpers.WriteError(
                    format,
                    "CONFLICTING_PATH_ARGUMENTS",
                    "--solution and --project were both provided with different paths. Pass only one, or use the same path for both.",
                    new { solution = solutionFull, project = projectFull });
                return null;
            }
            return solutionFull;
        }

        if (solution is not null)
            return Path.GetFullPath(solution.FullName);

        if (project is not null)
            return Path.GetFullPath(project.FullName);

        var cwd = cwdOverride ?? Directory.GetCurrentDirectory();

        foreach (var pattern in DiscoveryTiers)
        {
            string[] matches;
            try
            {
                matches = Directory.GetFiles(cwd, pattern);
            }
            catch (DirectoryNotFoundException)
            {
                matches = Array.Empty<string>();
            }

            if (matches.Length == 0)
                continue;

            if (matches.Length == 1)
                return Path.GetFullPath(matches[0]);

            Array.Sort(matches, StringComparer.Ordinal);
            CommandHelpers.WriteError(
                format,
                "SOLUTION_AMBIGUOUS",
                $"Multiple {pattern} files found in '{cwd}'. Pass --solution or --project to disambiguate.",
                new
                {
                    cwd,
                    tier = pattern,
                    candidates = matches.Select(Path.GetFullPath).ToArray()
                });
            return null;
        }

        CommandHelpers.WriteError(
            format,
            "SOLUTION_NOT_FOUND",
            $"No .slnx, .sln, or .csproj file found in '{cwd}'. Pass --solution or --project, or cd into a folder containing one.",
            new
            {
                cwd,
                searched = SearchedExtensions
            });
        return null;
    }
}
