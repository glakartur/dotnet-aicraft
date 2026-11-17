namespace DotnetAICraft.Daemon.FileSetSync;

/// <summary>
/// Path rules shared by the file watchers and the file-set reconciler.
/// </summary>
internal static class FileSetPaths
{
    private static readonly string[] ArtifactDirectoryNames = ["obj", "bin"];

    /// <summary>
    /// Comparison the host file system uses for paths: ordinal on Linux,
    /// case-insensitive on Windows and macOS.
    /// </summary>
    public static StringComparison OsComparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer OsComparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool IsCSharpFile(string? path)
        => !string.IsNullOrEmpty(path) && path.EndsWith(".cs", OsComparison);

    /// <summary>
    /// True for build outputs (<c>obj/</c>, <c>bin/</c>) and generated code
    /// (<c>*.g.cs</c>, <c>*.g.i.cs</c>). These regenerate during the MSBuild
    /// design-time build and do not represent user edits.
    /// </summary>
    public static bool IsBuildArtifactPath(string fullPath)
    {
        foreach (var segment in DirectorySegments(fullPath))
        {
            if (IsArtifactDirectoryName(segment))
                return true;
        }

        return IsGeneratedFileName(Path.GetFileName(fullPath));
    }

    /// <summary>
    /// True when the file set sync must not track the path: build artifacts,
    /// generated code, and paths under a hidden directory (a segment that starts
    /// with '.', which the SDK default globs exclude). Only the part below
    /// <paramref name="rootDirectory"/> is checked, so a solution that itself
    /// lives under a hidden or <c>bin</c> directory still works.
    /// Paths outside <paramref name="rootDirectory"/> are ignored.
    /// </summary>
    public static bool IsIgnored(string rootDirectory, string fullPath)
    {
        if (!IsUnderDirectory(rootDirectory, fullPath))
            return true;

        var relative = Path.GetRelativePath(rootDirectory, fullPath);
        foreach (var segment in DirectorySegments(relative))
        {
            if (IsArtifactDirectoryName(segment) || segment.StartsWith('.'))
                return true;
        }

        return IsGeneratedFileName(Path.GetFileName(fullPath));
    }

    /// <summary>
    /// Same rule as <see cref="IsIgnored"/> for a directory: the directory itself
    /// may be <c>obj</c>, <c>bin</c> or hidden.
    /// </summary>
    public static bool IsIgnoredDirectory(string rootDirectory, string directory)
        => IsIgnored(rootDirectory, Path.Combine(directory, "_"));

    public static bool IsSkippedDirectoryName(string name)
        => IsArtifactDirectoryName(name) || name.StartsWith('.');

    /// <summary>
    /// True when <paramref name="path"/> is strictly below <paramref name="directory"/>.
    /// The prefix always ends with a separator, so <c>src/Foo</c> does not cover <c>src/FooBar</c>.
    /// </summary>
    public static bool IsUnderDirectory(string directory, string path)
    {
        var prefix = WithTrailingSeparator(directory);
        return path.Length > prefix.Length && path.StartsWith(prefix, OsComparison);
    }

    /// <summary>
    /// Exact match of two file paths: the directory part uses the OS rule, the
    /// file name is always ordinal. A rename that changes only the letter case
    /// of the file name is then a real change on every OS.
    /// </summary>
    public static bool IsSameFile(string left, string right)
        => string.Equals(Path.GetFileName(left), Path.GetFileName(right), StringComparison.Ordinal) &&
           string.Equals(Path.GetDirectoryName(left), Path.GetDirectoryName(right), OsComparison);

    public static string WithTrailingSeparator(string directory)
        => Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;

    private static bool IsArtifactDirectoryName(string segment)
    {
        foreach (var name in ArtifactDirectoryNames)
        {
            if (string.Equals(segment, name, OsComparison))
                return true;
        }

        return false;
    }

    private static bool IsGeneratedFileName(string name)
        => name.EndsWith(".g.cs", OsComparison) || name.EndsWith(".g.i.cs", OsComparison);

    private static IEnumerable<string> DirectorySegments(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            return [];

        return directory.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }
}
