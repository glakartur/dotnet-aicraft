using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotnetAICraft.Daemon.FileSetSync;

/// <summary>
/// Changed paths collected by the watchers. <see cref="Files"/> are file paths;
/// <see cref="Directories"/> are directory markers (or any non-.cs path, because
/// a watcher event does not say if the path is a directory).
/// </summary>
internal sealed record FileSetBatch(IReadOnlyCollection<string> Files, IReadOnlyCollection<string> Directories);

internal enum ReconcileOutcome
{
    NoChange,
    Applied,
    Fallback,
}

internal sealed record AddedDocument(string Path, string ProjectName);

/// <param name="Solution">The new solution when <see cref="Outcome"/> is <see cref="ReconcileOutcome.Applied"/>.</param>
/// <param name="Skipped">New files that no project compiles, with the reason (log only).</param>
/// <param name="Retry">Paths that could not be read; the caller may queue them again once.</param>
/// <param name="FallbackPaths">New files whose owner could not be decided (empty for a mass-change fallback).</param>
internal sealed record ReconcileResult(
    ReconcileOutcome Outcome,
    Solution? Solution,
    IReadOnlyList<AddedDocument> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Retry,
    string? FallbackReason,
    IReadOnlyList<string> FallbackPaths)
{
    public static ReconcileResult ForFallback(string reason, IReadOnlyList<string>? paths = null)
        => new(ReconcileOutcome.Fallback, null, [], [], [], [], [], reason, paths ?? []);
}

/// <summary>
/// Brings the in-memory solution in line with the final on-disk state of a batch
/// of changed paths. One mechanism handles content changes, added and removed
/// files: for each path it compares "is on disk" with "is in the solution".
/// Because only the final state counts, the result does not depend on the order
/// or the number of watcher events (atomic saves, Created+Changed pairs, events
/// during a reload).
/// The reconciler changes the solution in memory only (no TryApplyChanges).
/// </summary>
internal sealed class SolutionFileSetReconciler(
    string rootDirectory,
    int massChangeThreshold = SolutionFileSetReconciler.DefaultMassChangeThreshold,
    bool? caseInsensitiveFileSystem = null)
{
    private readonly bool _caseInsensitive = caseInsensitiveFileSystem ?? FileSetPaths.IsCaseInsensitiveFileSystem;

    /// <summary>
    /// More added plus removed files than this in one batch (e.g. <c>git checkout</c>)
    /// gives a full reload. Content changes do not count.
    /// </summary>
    public const int DefaultMassChangeThreshold = 300;

    /// <param name="excludedByLastLoad">
    /// Files that were on disk during the last full load, but are not in the
    /// solution: MSBuild evaluation already decided they are not compiled. They
    /// are skipped, so saving such a file does not cause a full reload each time.
    /// </param>
    public async Task<ReconcileResult> ReconcileAsync(
        Solution solution,
        FileSetBatch batch,
        CancellationToken ct,
        IReadOnlySet<string>? excludedByLastLoad = null)
    {
        var paths = CollectPaths(solution, batch);
        var disk = new DiskFileNames();

        var removals = new List<(string Path, IReadOnlyList<DocumentId> Ids)>();
        var updates = new List<(string Path, IReadOnlyList<DocumentId> Ids)>();
        var additions = new List<string>();

        foreach (var path in paths)
        {
            var ids = FindDocuments(solution, path, disk, paths, _caseInsensitive);
            var onDisk = disk.Exists(path);

            if (onDisk && ids.Count > 0)
                updates.Add((path, ids));
            else if (onDisk)
                additions.Add(path);
            else if (ids.Count > 0)
                removals.Add((path, ids));
        }

        var fileSetChanges = additions.Count + removals.Count;
        if (fileSetChanges > massChangeThreshold)
            return ReconcileResult.ForFallback($"mass change: {fileSetChanges} files added or removed");

        var resolver = new ProjectOwnershipResolver(solution);
        var skipped = new List<string>();
        var ownedAdditions = new List<(string Path, OwnershipDecision.Owned Owner)>();
        var fallbacks = new List<(string Path, string Reason)>();
        foreach (var path in additions)
        {
            if (excludedByLastLoad?.Contains(path) == true)
            {
                skipped.Add($"{path}: not compiled according to the last full load");
                continue;
            }

            switch (resolver.Resolve(path))
            {
                case OwnershipDecision.Fallback fallback:
                    fallbacks.Add((path, fallback.Reason));
                    break;
                case OwnershipDecision.NotOwned notOwned:
                    skipped.Add($"{path}: {notOwned.Reason}");
                    break;
                case OwnershipDecision.Owned owned:
                    ownedAdditions.Add((path, owned));
                    break;
            }
        }

        if (fallbacks.Count > 0)
        {
            var (firstPath, firstReason) = fallbacks[0];
            return ReconcileResult.ForFallback(
                $"{firstPath}: {firstReason}",
                [.. fallbacks.Select(f => f.Path)]);
        }

        var result = solution;
        var retry = new List<string>();

        // Removals first, then additions: a rename that changes only the letter
        // case removes the old document before the new one is added.
        result = result.RemoveDocuments([.. removals.SelectMany(r => r.Ids)]);
        var removed = removals.Select(r => r.Path).ToList();

        var updated = new List<string>();
        foreach (var (path, ids) in updates)
        {
            var text = await TryReadTextAsync(path, retry, ct);
            // Guard against empty reads during concurrent writes (e.g. build tools flushing).
            if (text is null || text.Length == 0)
                continue;

            var current = await result.GetDocument(ids[0])!.GetTextAsync(ct);
            if (current.ContentEquals(text))
                continue;

            foreach (var id in ids)
                result = result.WithDocumentText(id, text);
            updated.Add(path);
        }

        var added = new List<AddedDocument>();
        foreach (var (path, owner) in ownedAdditions)
        {
            var text = await TryReadTextAsync(path, retry, ct);
            if (text is null)
                continue;

            var folders = GetFolders(owner.ProjectFilePath, path);
            foreach (var projectId in owner.ProjectIds)
            {
                result = result.AddDocument(
                    DocumentId.CreateNewId(projectId, debugName: path),
                    Path.GetFileName(path),
                    text,
                    folders,
                    path);
            }

            var projectNames = owner.ProjectIds.Select(id => result.GetProject(id)!.Name);
            added.Add(new AddedDocument(path, string.Join(", ", projectNames)));
        }

        var changed = removed.Count > 0 || updated.Count > 0 || added.Count > 0;
        return new ReconcileResult(
            changed ? ReconcileOutcome.Applied : ReconcileOutcome.NoChange,
            changed ? result : null,
            added,
            removed,
            updated,
            skipped,
            retry,
            FallbackReason: null,
            FallbackPaths: []);
    }

    /// <summary>
    /// True when a compiled document exists for the path. On a case-insensitive
    /// file system a document that differs only in letter case counts too, so
    /// such a file is never marked as excluded.
    /// </summary>
    public static bool ContainsDocument(Solution solution, string path, bool? caseInsensitiveFileSystem = null)
    {
        var caseInsensitive = caseInsensitiveFileSystem ?? FileSetPaths.IsCaseInsensitiveFileSystem;
        return FindDocuments(solution, path, disk: null, batchPaths: null, caseInsensitive).Count > 0;
    }

    /// <summary>
    /// File paths of the batch plus the expansion of each directory marker:
    /// documents of the solution under the directory and <c>.cs</c> files on
    /// disk under it. Expansion happens now, so a file written right after
    /// <c>mkdir</c> (before the watcher observes the new directory) is found.
    /// </summary>
    private SortedSet<string> CollectPaths(Solution solution, FileSetBatch batch)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in batch.Files)
            AddIfTracked(paths, file);

        var markers = CollapseToTopmost(batch.Directories
            .Where(directory => !FileSetPaths.IsIgnoredDirectory(rootDirectory, directory)));
        if (markers.Count == 0)
            return paths;

        // One pass over the documents: walk each document's parent directories
        // and look them up in the marker set (O(documents x depth)).
        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            if (document.FilePath is { } documentPath && HasAncestorIn(markers, documentPath))
                AddIfTracked(paths, documentPath);
        }

        foreach (var directory in markers)
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in EnumerateSourceFiles(directory))
                    AddIfTracked(paths, file);
            }
        }

        return paths;
    }

    /// <summary>Removes markers that are inside another marker of the same batch.</summary>
    private static HashSet<string> CollapseToTopmost(IEnumerable<string> directories)
    {
        var all = directories
            .Select(Path.TrimEndingDirectorySeparator)
            .ToHashSet(FileSetPaths.OsComparer);
        return all.Where(d => !HasAncestorIn(all, d)).ToHashSet(FileSetPaths.OsComparer);
    }

    private static bool HasAncestorIn(HashSet<string> directories, string path)
    {
        for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            if (directories.Contains(parent))
                return true;
        }

        return false;
    }

    private void AddIfTracked(ISet<string> paths, string path)
    {
        if (FileSetPaths.IsCSharpFile(path) && !FileSetPaths.IsIgnored(rootDirectory, path))
            paths.Add(path);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files, subdirectories;
            try
            {
                files = Directory.GetFiles(current, "*.cs", options);
                subdirectories = Directory.GetDirectories(current, "*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // removed or not readable while we enumerate
            }

            foreach (var file in files)
                yield return file;

            foreach (var subdirectory in subdirectories)
            {
                if (!FileSetPaths.IsSkippedDirectoryName(Path.GetFileName(subdirectory)))
                    pending.Push(subdirectory);
            }
        }
    }

    /// <summary>
    /// Documents with exactly this path. <see cref="Solution.GetDocumentIdsWithFilePath"/>
    /// ignores the letter case on every OS, so its result is filtered again.
    /// On a case-insensitive OS, when no exact document exists, a document that
    /// differs only in letter case is the same file (for example
    /// <c>&lt;Compile Include="foo.cs"/&gt;</c> with <c>Foo.cs</c> on disk), unless
    /// it is a case-only rename: its own path is then in the batch, or is still
    /// on disk under that exact name.
    /// </summary>
    private static IReadOnlyList<DocumentId> FindDocuments(
        Solution solution,
        string path,
        DiskFileNames? disk,
        ISet<string>? batchPaths,
        bool caseInsensitive)
    {
        var candidates = solution.GetDocumentIdsWithFilePath(path)
            .Select(id => (Id: id, DocumentPath: solution.GetDocument(id)?.FilePath))
            .Where(c => c.DocumentPath is not null)
            .ToList();

        var exact = candidates.Where(c => FileSetPaths.IsSameFile(c.DocumentPath!, path)).Select(c => c.Id).ToList();
        if (exact.Count > 0 || !caseInsensitive)
            return exact;

        return candidates
            .Where(c => batchPaths?.Contains(c.DocumentPath!) != true && disk?.Exists(c.DocumentPath!) != true)
            .Select(c => c.Id)
            .ToList();
    }

    private static IReadOnlyList<string> GetFolders(string projectFilePath, string filePath)
    {
        var relative = Path.GetRelativePath(Path.GetDirectoryName(projectFilePath)!, Path.GetDirectoryName(filePath)!);
        if (relative == ".")
            return [];

        return relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<SourceText?> TryReadTextAsync(string path, List<string> retry, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            // No explicit encoding: SourceText detects the BOM, so a later
            // TryApplyChanges (rename) writes the file back in the same encoding.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            return SourceText.From(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            retry.Add(path);
            return null;
        }
    }

    /// <summary>
    /// "Is on disk" by the real file name: a rename that changes only the letter
    /// case is a real change, also on case-insensitive file systems.
    /// </summary>
    private sealed class DiskFileNames
    {
        private readonly Dictionary<string, HashSet<string>> _namesByDirectory = new(FileSetPaths.OsComparer);

        public bool Exists(string path)
        {
            var directory = Path.GetDirectoryName(path)!;
            if (!_namesByDirectory.TryGetValue(directory, out var names))
            {
                names = ReadNames(directory);
                _namesByDirectory[directory] = names;
            }

            return names.Contains(Path.GetFileName(path));
        }

        private static HashSet<string> ReadNames(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .ToHashSet(StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }
}
