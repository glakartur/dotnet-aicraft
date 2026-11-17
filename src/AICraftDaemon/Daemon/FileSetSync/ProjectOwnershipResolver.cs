using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Daemon.FileSetSync;

/// <summary>Which project a new <c>.cs</c> file belongs to.</summary>
internal abstract record OwnershipDecision
{
    /// <summary>Add the file to every project instance in <see cref="ProjectIds"/> (multi-targeting).</summary>
    public sealed record Owned(string ProjectFilePath, IReadOnlyList<ProjectId> ProjectIds) : OwnershipDecision;

    /// <summary>No project compiles the file. Ignore it, no reload.</summary>
    public sealed record NotOwned(string Reason) : OwnershipDecision;

    /// <summary>Ownership is not certain. The daemon must do a full reload.</summary>
    public sealed record Fallback(string Reason) : OwnershipDecision;
}

/// <summary>
/// Finds the owner project of a new <c>.cs</c> file without MSBuild evaluation.
/// The rules are conservative: when a project can include or exclude files in a
/// way the default SDK globs do not explain, the result is a fallback.
/// Create one instance per batch: the policy of each project file is read once
/// per instance, never cached between batches.
/// </summary>
internal sealed class ProjectOwnershipResolver(Solution solution)
{
    private static readonly HashSet<string> CompilePolicyOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "Compile",
        "EnableDefaultCompileItems",
        "EnableDefaultItems",
        "DefaultItemExcludes",
        "DefaultExcludesInProjectFolder",
        // Output paths decide where generated .cs files go; outside obj/ and bin/
        // they could be picked up as source.
        "BaseIntermediateOutputPath",
        "BaseOutputPath",
        "UseArtifactsOutput",
        "ArtifactsPath",
    };

    private static readonly string[] DirectoryBuildFileNames = ["Directory.Build.props", "Directory.Build.targets"];

    private readonly Dictionary<string, string?> _policyViolationByProjectFile = new(FileSetPaths.OsComparer);
    private readonly Dictionary<string, string?> _violationByImportFile = new(FileSetPaths.OsComparer);

    public OwnershipDecision Resolve(string filePath)
    {
        var projectFiles = solution.Projects
            .Select(p => p.FilePath)
            .OfType<string>()
            .Distinct(FileSetPaths.OsComparer)
            .ToList();

        var candidates = projectFiles
            .Where(projectFile => FileSetPaths.IsUnderDirectory(Path.GetDirectoryName(projectFile)!, filePath))
            .ToList();

        return candidates.Count switch
        {
            0 => ResolveWithoutCandidate(projectFiles),
            1 => ResolveSingleCandidate(candidates[0]),
            _ => new OwnershipDecision.Fallback(
                $"overlapping projects: {string.Join(", ", candidates.Select(Path.GetFileName))}"),
        };
    }

    private OwnershipDecision ResolveWithoutCandidate(IEnumerable<string> projectFiles)
    {
        foreach (var projectFile in projectFiles)
        {
            if (GetPolicyViolation(projectFile) is { } violation)
            {
                return new OwnershipDecision.Fallback(
                    $"no owning project and {Path.GetFileName(projectFile)} may include it ({violation})");
            }
        }

        return new OwnershipDecision.NotOwned("outside all project directories");
    }

    private OwnershipDecision ResolveSingleCandidate(string projectFile)
    {
        if (!projectFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return new OwnershipDecision.NotOwned($"{Path.GetFileName(projectFile)} is not a C# project");

        if (GetPolicyViolation(projectFile) is { } violation)
            return new OwnershipDecision.Fallback($"{Path.GetFileName(projectFile)}: {violation}");

        var projectIds = solution.Projects
            .Where(p => string.Equals(p.FilePath, projectFile, FileSetPaths.OsComparison))
            .Select(p => p.Id)
            .ToList();

        return new OwnershipDecision.Owned(projectFile, projectIds);
    }

    /// <summary>
    /// Returns why the project does not have a safe Compile policy, or null when
    /// it does: SDK-style, no explicit Compile items and no property that changes
    /// the default globs, in the project and in every Directory.Build.props/targets
    /// from the project directory up to the file system root.
    /// </summary>
    private string? GetPolicyViolation(string projectFile)
    {
        if (_policyViolationByProjectFile.TryGetValue(projectFile, out var cached))
            return cached;

        var violation = ReadProjectViolation(projectFile) ?? ReadDirectoryBuildViolation(projectFile);
        _policyViolationByProjectFile[projectFile] = violation;
        return violation;
    }

    private static string? ReadProjectViolation(string projectFile)
    {
        var document = TryLoadXml(projectFile, out var error);
        if (document?.Root is not { } root)
            return error;

        if (!IsSdkStyle(root))
            return "not an SDK-style project";

        return FindOverride(root) is { } name ? $"defines {name}" : null;
    }

    private string? ReadDirectoryBuildViolation(string projectFile)
    {
        for (var directory = Path.GetDirectoryName(projectFile); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            foreach (var fileName in DirectoryBuildFileNames)
            {
                var importFile = Path.Combine(directory, fileName);
                if (GetImportViolation(importFile) is { } violation)
                    return violation;
            }
        }

        return null;
    }

    private string? GetImportViolation(string importFile)
    {
        if (_violationByImportFile.TryGetValue(importFile, out var cached))
            return cached;

        string? violation = null;
        if (File.Exists(importFile))
        {
            var document = TryLoadXml(importFile, out var error);
            violation = document?.Root is { } root
                ? FindOverride(root) is { } name ? $"{importFile} defines {name}" : null
                : $"{importFile}: {error}";
        }

        _violationByImportFile[importFile] = violation;
        return violation;
    }

    private static bool IsSdkStyle(XElement root)
        => root.Attribute("Sdk") is not null ||
           root.Elements().Any(e => e.Name.LocalName == "Sdk") ||
           root.Elements().Any(e => e.Name.LocalName == "Import" && e.Attribute("Sdk") is not null);

    private static string? FindOverride(XElement root)
        => root.Descendants()
            .Select(e => e.Name.LocalName)
            .FirstOrDefault(CompilePolicyOverrides.Contains);

    private static XDocument? TryLoadXml(string path, out string error)
    {
        try
        {
            error = string.Empty;
            return XDocument.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            error = $"cannot read project file ({ex.GetType().Name})";
            return null;
        }
    }
}
