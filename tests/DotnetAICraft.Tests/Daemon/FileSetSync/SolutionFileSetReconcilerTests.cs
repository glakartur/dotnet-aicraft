using System.Text;
using DotnetAICraft.Daemon.FileSetSync;
using DotnetAICraft.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotnetAICraft.Tests.Daemon.FileSetSync;

public sealed class SolutionFileSetReconcilerTests
{
    [Fact]
    public async Task Reconcile_NewFileInSdkProject_AddsDocumentAndCompilationSeesType()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var file = t.WriteFile("src/A/Models/Foo.cs", "namespace A.Models; public class Foo { }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        var document = Assert.Single(t.DocumentsAt(result.Solution!, file));
        Assert.Equal(projectId, document.Project.Id);
        Assert.Equal(["Models"], document.Folders);
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation!.GetTypeByMetadataName("A.Models.Foo"));
        Assert.Contains(result.Added, a => a.Path == file && a.ProjectName == "A");
    }

    [Fact]
    public async Task Reconcile_DeletedFile_RemovesDocumentFromEveryProject()
    {
        using var t = new FileSetTestSolution();
        var first = t.AddProject("src/A/A.csproj", name: "A(net8.0)");
        var second = t.AddProjectInstance(t.PathOf("src/A/A.csproj"), "A(net10.0)");
        var file = t.AddDocument("src/A/Foo.cs", "class Foo { }", first, second);
        File.Delete(file);

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Empty(t.DocumentsAt(result.Solution!, file));
        Assert.Equal([file], result.Removed);
    }

    [Fact]
    public async Task Reconcile_RenameInOneBatch_RemovesOldAndAddsNew()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var oldPath = t.AddDocument("src/A/Foo.cs", "class Foo { }", projectId);
        var newPath = t.PathOf("src/A/Bar.cs");
        File.Move(oldPath, newPath);

        var result = await Reconcile(t, files: [oldPath, newPath]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Empty(t.DocumentsAt(result.Solution!, oldPath));
        Assert.Single(t.DocumentsAt(result.Solution!, newPath));
    }

    [Fact]
    public async Task Reconcile_MoveBetweenProjects_DocumentOnlyInTargetProject()
    {
        using var t = new FileSetTestSolution();
        var a = t.AddProject("src/A/A.csproj");
        var b = t.AddProject("src/B/B.csproj");
        var oldPath = t.AddDocument("src/A/Foo.cs", "class Foo { }", a);
        var newPath = t.PathOf("src/B/Foo.cs");
        File.Move(oldPath, newPath);

        var result = await Reconcile(t, files: [oldPath, newPath]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        var document = Assert.Single(result.Solution!.Projects.SelectMany(p => p.Documents));
        Assert.Equal(b, document.Project.Id);
        Assert.Equal(newPath, document.FilePath);
    }

    [Fact]
    public async Task Reconcile_NewFileInMultiTargetProject_AddsToEveryInstance()
    {
        using var t = new FileSetTestSolution();
        var first = t.AddProject("src/A/A.csproj", name: "A(net8.0)");
        var second = t.AddProjectInstance(t.PathOf("src/A/A.csproj"), "A(net10.0)");
        var file = t.WriteFile("src/A/Foo.cs", "class Foo { }");

        var result = await Reconcile(t, files: [file]);

        var owners = t.DocumentsAt(result.Solution!, file).Select(d => d.Project.Id).ToHashSet();
        Assert.Equal(new HashSet<ProjectId> { first, second }, owners);
    }

    [Fact]
    public async Task Reconcile_ContentChangeInMultiTargetProject_UpdatesEveryInstance()
    {
        using var t = new FileSetTestSolution();
        var first = t.AddProject("src/A/A.csproj", name: "A(net8.0)");
        var second = t.AddProjectInstance(t.PathOf("src/A/A.csproj"), "A(net10.0)");
        var file = t.AddDocument("src/A/Foo.cs", "class Foo { }", first, second);
        File.WriteAllText(file, "class Foo { int X; }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Equal([file], result.Updated);
        foreach (var document in t.DocumentsAt(result.Solution!, file))
            Assert.Equal("class Foo { int X; }", (await document.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task Reconcile_FileOutsideProjectsWhenAllProjectsSafe_ReturnsNoChange()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var file = t.WriteFile("scripts/Tool.cs", "class Tool { }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public async Task Reconcile_FileInNestedProjects_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        t.AddProject("src/A/Sub/Sub.csproj");
        var file = t.WriteFile("src/A/Sub/Foo.cs", "class Foo { }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.Fallback, result.Outcome);
        Assert.Contains("overlapping projects", result.FallbackReason);
    }

    [Fact]
    public async Task Reconcile_FileSharedThroughExplicitCompileInclude_ReturnsFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><Compile Include="..\Shared\*.cs" /></ItemGroup>
            </Project>
            """);
        var file = t.WriteFile("src/Shared/Common.cs", "class Common { }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.Fallback, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_OneFallbackPathInBatch_DropsWholeBatch()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        t.AddProject("src/Legacy/Legacy.csproj", FileSetTestSolution.LegacyProject);
        var good = t.WriteFile("src/A/Good.cs", "class Good { }");
        var legacy = t.WriteFile("src/Legacy/New.cs", "class New { }");

        var result = await Reconcile(t, files: [good, legacy]);

        Assert.Equal(ReconcileOutcome.Fallback, result.Outcome);
        Assert.Null(result.Solution);
        // The legacy project may list any file, so both new files are undecided.
        Assert.Equal(new[] { good, legacy }.Order(StringComparer.Ordinal), result.FallbackPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reconcile_FileExcludedByLastLoad_IsSkippedWithoutFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/Legacy/Legacy.csproj", FileSetTestSolution.LegacyProject);
        var excluded = t.WriteFile("src/Legacy/NotListed.cs", "class NotListed { }");

        var result = await new SolutionFileSetReconciler(t.Root).ReconcileAsync(
            t.Solution,
            new FileSetBatch([excluded], []),
            CancellationToken.None,
            new HashSet<string> { excluded });

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
        Assert.Contains(result.Skipped, s => s.StartsWith(excluded, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reconcile_NestedDirectoryMarkers_ExpandOnce()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var old = t.AddDocument("src/A/Models/Deep/One.cs", "class One { }", projectId);
        Directory.Move(t.PathOf("src/A/Models"), t.PathOf("src/A/Entities"));

        var result = await Reconcile(t, directories:
        [
            t.PathOf("src/A/Models"),
            t.PathOf("src/A/Models/Deep"),
            t.PathOf("src/A/Entities") + Path.DirectorySeparatorChar,
            t.PathOf("src/A/Entities/Deep"),
        ]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Empty(t.DocumentsAt(result.Solution!, old));
        Assert.Single(t.DocumentsAt(result.Solution!, t.PathOf("src/A/Entities/Deep/One.cs")));
    }

    [Theory]
    [InlineData("src/A/obj/Debug/Foo.cs")]
    [InlineData("src/A/bin/Foo.cs")]
    [InlineData("src/A/Foo.g.cs")]
    [InlineData("src/A/Foo.g.i.cs")]
    [InlineData("src/A/.foo/Foo.cs")]
    [InlineData("src/A/node_modules/pkg/Foo.cs")]
    public async Task Reconcile_IgnoredPath_ReturnsNoChange(string relativePath)
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var file = t.WriteFile(relativePath, "class Foo { }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_OwnerIsNotCSharpProject_ReturnsNoChange()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/Vb/Vb.vbproj");
        var file = t.WriteFile("src/Vb/Foo.cs", "class Foo { }");

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_DeletedObjDirectoryMarker_KeepsGeneratedDocuments()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var generated = t.AddDocument("src/A/obj/Debug/net10.0/A.GlobalUsings.g.cs", "global using System;", projectId);
        Directory.Delete(t.PathOf("src/A/obj"), recursive: true);

        var result = await Reconcile(t, directories: [t.PathOf("src/A/obj")]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
        Assert.Single(t.DocumentsAt(t.Solution, generated));
    }

    [Fact]
    public async Task Reconcile_FileWithoutBom_KeepsNoBomEncoding_FileWithBom_KeepsBom()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var noBom = t.PathOf("src/A/NoBom.cs");
        var withBom = t.PathOf("src/A/WithBom.cs");
        File.WriteAllText(noBom, "class NoBom { }", new UTF8Encoding(false));
        File.WriteAllText(withBom, "class WithBom { }", new UTF8Encoding(true));

        var result = await Reconcile(t, files: [noBom, withBom]);

        var noBomText = await t.DocumentsAt(result.Solution!, noBom)[0].GetTextAsync();
        var withBomText = await t.DocumentsAt(result.Solution!, withBom)[0].GetTextAsync();
        Assert.Empty(noBomText.Encoding!.GetPreamble());
        Assert.NotEmpty(withBomText.Encoding!.GetPreamble());
        Assert.Equal("class NoBom { }", noBomText.ToString());
        Assert.Equal("class WithBom { }", withBomText.ToString());
    }

    [Fact]
    public async Task Reconcile_DirectoryMovedInsideProject_MovesAllDocumentsInOneApply()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var one = t.AddDocument("src/A/Models/One.cs", "class One { }", projectId);
        var two = t.AddDocument("src/A/Models/Deep/Two.cs", "class Two { }", projectId);
        Directory.Move(t.PathOf("src/A/Models"), t.PathOf("src/A/Entities"));

        var result = await Reconcile(t, directories: [t.PathOf("src/A/Models"), t.PathOf("src/A/Entities")]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Empty(t.DocumentsAt(result.Solution!, one));
        Assert.Empty(t.DocumentsAt(result.Solution!, two));
        Assert.Single(t.DocumentsAt(result.Solution!, t.PathOf("src/A/Entities/One.cs")));
        var moved = Assert.Single(t.DocumentsAt(result.Solution!, t.PathOf("src/A/Entities/Deep/Two.cs")));
        Assert.Equal(["Entities", "Deep"], moved.Folders);
    }

    [Fact]
    public async Task Reconcile_DirectoryWithDotInNameMoved_UsesNewPaths()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/Sample.Domain/Sample.Domain.csproj");
        var old = t.AddDocument("src/Sample.Domain/Models/Order.cs", "class Order { }", projectId);
        Directory.Move(t.PathOf("src/Sample.Domain/Models"), t.PathOf("src/Sample.Domain/Entities"));

        var result = await Reconcile(t, directories: [t.PathOf("src/Sample.Domain/Models"), t.PathOf("src/Sample.Domain/Entities")]);

        Assert.Empty(t.DocumentsAt(result.Solution!, old));
        Assert.Single(t.DocumentsAt(result.Solution!, t.PathOf("src/Sample.Domain/Entities/Order.cs")));
    }

    [Fact]
    public async Task Reconcile_NewDirectoryMarker_AddsFileThatHadNoFileEvent()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var file = t.WriteFile("src/A/Feature/Handler.cs", "class Handler { }");

        var result = await Reconcile(t, directories: [t.PathOf("src/A/Feature")]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Single(t.DocumentsAt(result.Solution!, file));
    }

    [Fact]
    public async Task Reconcile_DirectoryMarkerForMissingPathOrFile_ReturnsNoChange()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var json = t.WriteFile("src/A/notes.json", "{}");

        var result = await Reconcile(t, directories: [t.PathOf("src/A/gone"), json]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_DirectoryMarker_DoesNotTouchSiblingWithCommonPrefix()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var sibling = t.AddDocument("src/A/FooBar/Keep.cs", "class Keep { }", projectId);

        var result = await Reconcile(t, directories: [t.PathOf("src/A/Foo")]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
        Assert.Single(t.DocumentsAt(t.Solution, sibling));
    }

    [Fact]
    public async Task Reconcile_TemporaryFileCreatedAndDeletedInOneBatch_ReturnsNoChange()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var temp = t.PathOf("src/A/Temp.cs");

        var result = await Reconcile(t, files: [temp]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_UnchangedText_ReturnsNoChange()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var file = t.AddDocument("src/A/Foo.cs", "class Foo { }", projectId);

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_EmptyReadOfExistingDocument_KeepsOldText()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var file = t.AddDocument("src/A/Foo.cs", "class Foo { }", projectId);
        File.WriteAllText(file, string.Empty);

        var result = await Reconcile(t, files: [file]);

        Assert.Equal(ReconcileOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task Reconcile_CaseOnlyRename_LeavesOneDocumentWithNewName()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var oldPath = t.AddDocument("src/A/Foo.cs", "class Foo { }", projectId);
        var newPath = t.PathOf("src/A/foo.cs");
        // Two-step move so it also works on case-insensitive file systems.
        var temp = t.PathOf("src/A/tmp.move");
        File.Move(oldPath, temp);
        File.Move(temp, newPath);

        var result = await Reconcile(t, files: [oldPath, newPath]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        var document = Assert.Single(result.Solution!.Projects.SelectMany(p => p.Documents));
        Assert.Equal(newPath, document.FilePath);
    }

    [LinuxOnlyFact]
    public async Task Reconcile_OnLinux_FilesDifferingOnlyInCaseAreSeparateDocuments()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var upper = t.AddDocument("src/A/Foo.cs", "class Foo { }", projectId);
        var lower = t.AddDocument("src/A/foo.cs", "class foo { }", projectId);
        File.Delete(lower);

        var result = await Reconcile(t, files: [lower]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Single(t.DocumentsAt(result.Solution!, upper));
        Assert.Empty(t.DocumentsAt(result.Solution!, lower));
    }

    [Fact]
    public async Task Reconcile_AddsAndRemovesAboveThreshold_ReturnsMassChangeFallback()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var files = Enumerable.Range(0, 4)
            .Select(i => t.WriteFile($"src/A/F{i}.cs", $"class F{i} {{ }}"))
            .ToArray();

        var result = await Reconcile(t, files: files, threshold: 3);

        Assert.Equal(ReconcileOutcome.Fallback, result.Outcome);
        Assert.Contains("mass change", result.FallbackReason);
    }

    [Fact]
    public async Task Reconcile_ContentChangesAboveThreshold_AreApplied()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var files = Enumerable.Range(0, 4)
            .Select(i => t.AddDocument($"src/A/F{i}.cs", $"class F{i} {{ }}", projectId))
            .ToArray();
        foreach (var file in files)
            File.AppendAllText(file, "// formatted");

        var result = await Reconcile(t, files: files, threshold: 3);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Equal(4, result.Updated.Count);
    }

    [Fact]
    public async Task Reconcile_UnreadableFile_IsReturnedForRetryAndOtherChangesApply()
    {
        using var t = new FileSetTestSolution();
        t.AddProject("src/A/A.csproj");
        var good = t.WriteFile("src/A/Good.cs", "class Good { }");
        var locked = t.WriteFile("src/A/Locked.cs", "class Locked { }");

        using var lockHandle = MakeUnreadable(locked);
        if (lockHandle is null)
            return; // running as root: permissions do not block reads

        var result = await Reconcile(t, files: [good, locked]);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Single(t.DocumentsAt(result.Solution!, good));
        Assert.Empty(t.DocumentsAt(result.Solution!, locked));
        Assert.Equal([locked], result.Retry);
    }

    [Fact]
    public async Task Reconcile_CaseInsensitiveFs_DocumentWithDifferentCaseIsUpdatedNotAdded()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var documentPath = t.PathOf("src/A/foo.cs");
        t.Solution = t.Solution.AddDocument(
            DocumentId.CreateNewId(projectId), "foo.cs", SourceText.From("class Foo { }"), filePath: documentPath);
        var diskPath = t.WriteFile("src/A/Foo.cs", "class Foo { int X; }");

        var result = await new SolutionFileSetReconciler(t.Root, caseInsensitiveFileSystem: true).ReconcileAsync(
            t.Solution, new FileSetBatch([diskPath], []), CancellationToken.None);

        Assert.Equal(ReconcileOutcome.Applied, result.Outcome);
        Assert.Empty(result.Added);
        Assert.Equal([diskPath], result.Updated);
        Assert.True(SolutionFileSetReconciler.ContainsDocument(t.Solution, diskPath, caseInsensitiveFileSystem: true));
        Assert.False(SolutionFileSetReconciler.ContainsDocument(t.Solution, diskPath, caseInsensitiveFileSystem: false));
    }

    [Fact]
    public async Task Reconcile_CaseSensitiveFs_DocumentWithDifferentCaseIsNotTheSameFile()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        t.Solution = t.Solution.AddDocument(
            DocumentId.CreateNewId(projectId), "foo.cs", SourceText.From("class Foo { }"), filePath: t.PathOf("src/A/foo.cs"));
        var diskPath = t.WriteFile("src/A/Foo.cs", "class Foo2 { }");

        var result = await new SolutionFileSetReconciler(t.Root, caseInsensitiveFileSystem: false).ReconcileAsync(
            t.Solution, new FileSetBatch([diskPath], []), CancellationToken.None);

        Assert.Single(result.Added);
        Assert.Empty(result.Updated);
    }

    [Fact]
    public async Task Reconcile_CaseInsensitiveFs_CaseOnlyRenameStillRemovesOldAndAddsNew()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var oldPath = t.AddDocument("src/A/foo.cs", "class Foo { }", projectId);
        var newPath = t.PathOf("src/A/Foo.cs");
        File.Move(oldPath, newPath);

        var result = await new SolutionFileSetReconciler(t.Root, caseInsensitiveFileSystem: true).ReconcileAsync(
            t.Solution, new FileSetBatch([oldPath, newPath], []), CancellationToken.None);

        Assert.Equal([oldPath], result.Removed);
        Assert.Equal(newPath, Assert.Single(result.Added).Path);
    }

    // Two files that differ only in case can exist only on a case-sensitive disk.
    [LinuxOnlyFact]
    public async Task Reconcile_CaseInsensitiveFs_DocumentStillOnDiskUnderOwnName_IsNotReused()
    {
        using var t = new FileSetTestSolution();
        var projectId = t.AddProject("src/A/A.csproj");
        var documentPath = t.AddDocument("src/A/foo.cs", "class Foo { }", projectId);
        var newPath = t.WriteFile("src/A/Foo.cs", "class Foo2 { }");

        var result = await new SolutionFileSetReconciler(t.Root, caseInsensitiveFileSystem: true).ReconcileAsync(
            t.Solution, new FileSetBatch([newPath], []), CancellationToken.None);

        Assert.Equal(newPath, Assert.Single(result.Added).Path);
        Assert.Empty(result.Updated);
        Assert.Single(t.DocumentsAt(result.Solution!, documentPath));
    }

    private static async Task<ReconcileResult> Reconcile(
        FileSetTestSolution t,
        string[]? files = null,
        string[]? directories = null,
        int threshold = SolutionFileSetReconciler.DefaultMassChangeThreshold)
    {
        var reconciler = new SolutionFileSetReconciler(t.Root, threshold);
        return await reconciler.ReconcileAsync(
            t.Solution,
            new FileSetBatch(files ?? [], directories ?? []),
            CancellationToken.None);
    }

    private static IDisposable? MakeUnreadable(string path)
    {
        if (OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            using var probe = File.OpenRead(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return new RestoreMode(path);
        }
    }

    private sealed class RestoreMode(string path) : IDisposable
    {
        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
