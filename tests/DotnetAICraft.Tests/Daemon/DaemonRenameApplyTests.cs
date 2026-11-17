using DotnetAICraft.Models;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

/// <summary>
/// rename keeps TryApplyChanges, but never on an in-memory document set that
/// differs from MSBuildWorkspace: then it reloads and returns SOLUTION_LOADING.
/// </summary>
[Trait(LiveSampleSolution.TraitName, LiveSampleSolution.TraitValue)]
public sealed class DaemonRenameApplyTests
{
    private const string RenamedSymbol = "Sample.Domain.Services.OrderExtensions.IsLarge";
    private const string ExtensionsFile = "src/Sample.Domain/Services/OrderExtensions.cs";

    [Fact]
    public async Task Rename_AfterFileAddedInMemory_ReturnsSolutionLoadingWithoutWritingAndReloads()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var added = live.WriteFile("src/Sample.Domain/Services/Added.cs", "namespace Sample.Domain.Services; public sealed class Added { }");
        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(added).Count == 1);
        var snapshot = SnapshotFiles(live.Root);

        var response = await live.SendAsync("rename", new { symbol = RenamedSymbol, to = "IsBig" });

        Assert.Equal(DaemonResponseStatus.Problem, response.Status);
        Assert.Equal("SOLUTION_LOADING", response.Error!.Code);
        Assert.Equal("loading", (await live.StatusAsync()).LoadState);
        Assert.Equal(snapshot, SnapshotFiles(live.Root));

        await live.WaitLoadedAsync(expectedAttempts: 2);
        var retry = await live.SendAsync("rename", new { symbol = RenamedSymbol, to = "IsBig" });

        Assert.Equal(DaemonResponseStatus.Ok, retry.Status);
        Assert.Contains("IsBig", File.ReadAllText(live.PathOf(ExtensionsFile)));
        Assert.Single(live.DocumentsAt(added));
        Assert.Equal(
            File.ReadAllText(live.PathOf("src/Sample.Domain/Sample.Domain.csproj")),
            snapshot[Path.Combine("src", "Sample.Domain", "Sample.Domain.csproj")]);
    }

    [Fact]
    public async Task Rename_AfterContentOnlyChange_AppliesAsBefore_AndKeepsFileWithoutBom()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var file = live.PathOf(ExtensionsFile);
        Assert.False(HasUtf8Bom(file));
        File.AppendAllText(file, "// edited\n");
        await LiveSampleSolution.EventuallyAsync(async () =>
            (await live.DocumentsAt(file)[0].GetTextAsync()).ToString().Contains("// edited"));

        var response = await live.SendAsync("rename", new { symbol = RenamedSymbol, to = "IsBig" });

        Assert.Equal(DaemonResponseStatus.Ok, response.Status);
        var text = File.ReadAllText(file);
        Assert.Contains("IsBig", text);
        Assert.Contains("// edited", text);
        Assert.False(HasUtf8Bom(file));
        Assert.Equal(1, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task RenameDryRun_WithDifferentDocumentSet_ReturnsPreviewWithoutWriteOrReload()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var added = live.WriteFile("src/Sample.Domain/Services/Added.cs", "namespace Sample.Domain.Services; public sealed class Added { }");
        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(added).Count == 1);
        var snapshot = SnapshotFiles(live.Root);

        var response = await live.SendAsync("rename", new { symbol = RenamedSymbol, to = "IsBig", dryRun = true });

        Assert.Equal(DaemonResponseStatus.Ok, response.Status);
        var result = Assert.IsType<RenameResult>(response.Result);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Changes);
        Assert.Equal(snapshot, SnapshotFiles(live.Root));
        Assert.Equal(1, live.Server.LoadAttemptCount);
        Assert.Equal("loaded", (await live.StatusAsync()).LoadState);
    }

    [Fact]
    public async Task Rename_WhenBatchAddsDocumentAfterPreview_SeesDifferenceAndReloads()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var snapshot = SnapshotFiles(live.Root);
        string? added = null;
        live.Server.BeforeRenameApplyForTesting = async () =>
        {
            added = live.WriteFile("src/Sample.Domain/Services/Late.cs", "namespace Sample.Domain.Services; public sealed class Late { }");
            await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(added).Count == 1);
        };

        var response = await live.SendAsync("rename", new { symbol = RenamedSymbol, to = "IsBig" });

        Assert.Equal("SOLUTION_LOADING", response.Error?.Code);
        snapshot[Path.GetRelativePath(live.Root, added!)] = File.ReadAllText(added!);
        Assert.Equal(snapshot, SnapshotFiles(live.Root));
    }

    private static bool HasUtf8Bom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }

    /// <summary>Source and project files (no obj/bin) by relative path.</summary>
    private static Dictionary<string, string> SnapshotFiles(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "obj" or "bin"))
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllText);
}
