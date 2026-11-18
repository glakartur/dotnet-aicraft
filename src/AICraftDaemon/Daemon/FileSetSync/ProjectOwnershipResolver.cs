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
        // SDK input folded into DefaultExcludesInProjectFolder (DefaultItems.targets).
        "DefaultItemExcludesInProjectFolder",
        // Output paths decide where generated .cs files go; outside obj/ and bin/
        // they could be picked up as source.
        "BaseIntermediateOutputPath",
        "BaseOutputPath",
        "OutputPath",
        "IntermediateOutputPath",
        "UseArtifactsOutput",
        "ArtifactsPath",
    };

    private static readonly string[] DirectoryBuildFileNames = ["Directory.Build.props", "Directory.Build.targets"];

    private readonly Dictionary<string, string?> _policyViolationByProjectFile = new(FileSetPaths.OsComparer);
    private readonly Dictionary<string, string?> _externalCompileByProjectFile = new(FileSetPaths.OsComparer);
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
            1 => ResolveSingleCandidate(candidates[0], projectFiles),
            _ => new OwnershipDecision.Fallback(
                $"overlapping projects: {string.Join(", ", candidates.Select(Path.GetFileName))}"),
        };
    }

    private OwnershipDecision ResolveWithoutCandidate(IEnumerable<string> projectFiles)
    {
        foreach (var projectFile in projectFiles)
        {
            if (GetExternalCompileViolation(projectFile) is { } violation)
            {
                return new OwnershipDecision.Fallback(
                    $"no owning project and {Path.GetFileName(projectFile)} may include it ({violation})");
            }
        }

        return new OwnershipDecision.NotOwned("outside all project directories");
    }

    private OwnershipDecision ResolveSingleCandidate(string projectFile, IEnumerable<string> projectFiles)
    {
        if (!projectFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return new OwnershipDecision.NotOwned($"{Path.GetFileName(projectFile)} is not a C# project");

        if (GetPolicyViolation(projectFile) is { } violation)
            return new OwnershipDecision.Fallback($"{Path.GetFileName(projectFile)}: {violation}");

        // Another project can link the file (e.g. <Compile Include="..\A\Shared.cs" />);
        // the directory owner alone does not prove it is the only one.
        foreach (var other in projectFiles)
        {
            if (string.Equals(other, projectFile, FileSetPaths.OsComparison))
                continue;

            if (GetExternalCompileViolation(other) is { } otherViolation)
            {
                return new OwnershipDecision.Fallback(
                    $"{Path.GetFileName(other)} may also include it ({otherViolation})");
            }
        }

        var projectIds = solution.Projects
            .Where(p => string.Equals(p.FilePath, projectFile, FileSetPaths.OsComparison))
            .Select(p => p.Id)
            .ToList();

        return new OwnershipDecision.Owned(projectFile, projectIds);
    }

    /// <summary>
    /// For a project that does not own the file by directory: returns why it may
    /// still compile the file, or null. Only an explicit <c>&lt;Compile Include&gt;</c>
    /// (also with <c>Link</c>) whose Include points outside the project directory
    /// counts. <c>Compile Update/Remove</c>, default-glob properties and output-path
    /// properties of such a project do not make it a candidate.
    /// </summary>
    private string? GetExternalCompileViolation(string projectFile)
    {
        if (_externalCompileByProjectFile.TryGetValue(projectFile, out var cached))
            return cached;

        var violation = FindExternalCompileInclude(projectFile);
        _externalCompileByProjectFile[projectFile] = violation;
        return violation;
    }

    private static string? FindExternalCompileInclude(string projectFile)
    {
        var document = TryLoadXml(projectFile, out _);
        if (document?.Root is not { } root)
            return null;

        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        foreach (var item in root.Descendants().Where(e => e.Name.LocalName == "Compile"))
        {
            if (item.Attribute("Include")?.Value is not { Length: > 0 } include)
                continue;

            foreach (var part in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!PointsInsideDirectory(projectDirectory, part))
                    return $"includes {part} outside its directory";
            }
        }

        return null;
    }

    private static bool PointsInsideDirectory(string directory, string include)
    {
        // An MSBuild property or item reference cannot be resolved here: treat as outside.
        if (include.Contains("$(") || include.Contains("@("))
            return false;

        try
        {
            var normalized = include.Replace('\\', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(directory, normalized));
            return FileSetPaths.IsUnderDirectory(directory, full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
