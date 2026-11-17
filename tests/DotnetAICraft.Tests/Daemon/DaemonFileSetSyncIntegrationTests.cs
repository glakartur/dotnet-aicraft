using System.Diagnostics;
using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using Xunit;
using Xunit.Abstractions;

namespace DotnetAICraft.Tests.Daemon;

/// <summary>
/// End-to-end file-set sync on a real MSBuild solution with live watchers.
/// Filter out with <c>--filter "Category!=FileWatcherIntegration"</c> where
/// MSBuild or file watching is not reliable (network drives, WSL2 /mnt).
/// </summary>
[Trait(LiveSampleSolution.TraitName, LiveSampleSolution.TraitValue)]
public sealed class DaemonFileSetSyncIntegrationTests(ITestOutputHelper output)
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public async Task NewFileWithExtractedClass_IsVisibleToSymbolsAndDiagnostics()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var stopwatch = Stopwatch.StartNew();

        live.WriteFile("src/Sample.Domain/Services/PriceCalculator.cs", """
            using Sample.Domain.Entities;

            namespace Sample.Domain.Services;

            public sealed class PriceCalculator
            {
                public decimal Total(Order order) => order.Total;
            }
            """);
        live.WriteFile("src/Sample.Domain/Services/PriceCalculatorUser.cs", """
            namespace Sample.Domain.Services;

            public static class PriceCalculatorUser
            {
                public static PriceCalculator Create() => new PriceCalculator();
            }
            """);

        var userFile = live.PathOf("src/Sample.Domain/Services/PriceCalculatorUser.cs");
        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(userFile).Count == 1);
        // Informational: sync latency only (not the first compilation of the query below).
        output.WriteLine($"New files in the solution after {stopwatch.ElapsedMilliseconds} ms (target < ~1 s).");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");

        Assert.NotEmpty(await live.SymbolsAsync("PriceCalculator"));

        var errors = await live.ResultJsonAsync("diagnostics", new { severity = "error" });
        Assert.Equal(0, errors.GetProperty("result").GetArrayLength());
        Assert.Equal(1, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task DeletedFile_SymbolsAndRefsNoLongerSeeIt()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var file = live.PathOf("src/Sample.Domain/Services/OrderExtensions.cs");
        Assert.NotEmpty(await live.SymbolsAsync("OrderExtensions"));

        File.Delete(file);

        await LiveSampleSolution.EventuallyAsync(async () => (await live.SymbolsAsync("OrderExtensions")).Count == 0);
        var refs = await live.ResultJsonAsync("refs", new { symbol = "Sample.Domain.Entities.Order" });
        Assert.DoesNotContain("OrderExtensions.cs", refs.GetRawText());
        Assert.Equal(1, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task RenamedFileInSameDirectory_HasOneDocumentWithNewPath()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var oldPath = live.PathOf("src/Sample.Domain/Services/OrderExtensions.cs");
        var newPath = live.PathOf("src/Sample.Domain/Services/OrderHelpers.cs");
        var documentsBefore = (await live.StatusAsync()).Documents;

        File.Move(oldPath, newPath);

        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(newPath).Count == 1);
        Assert.Empty(live.DocumentsAt(oldPath));
        Assert.Equal(documentsBefore, (await live.StatusAsync()).Documents);
    }

    [Fact]
    public async Task FileMovedBetweenProjects_IsOnlyInTargetProject()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var oldPath = live.PathOf("src/Sample.Domain/Services/OrderExtensions.cs");
        var newPath = live.PathOf("src/Sample.Infrastructure/OrderExtensions.cs");

        File.Move(oldPath, newPath);

        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(newPath).Count == 1);
        Assert.Empty(live.DocumentsAt(oldPath));
        Assert.Equal("Sample.Infrastructure", live.DocumentsAt(newPath)[0].Project.Name);
    }

    [Fact]
    public async Task DirectoryMovedInsideProject_AllDocumentsGetNewPathsWithoutFullReload()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var documentsBefore = (await live.StatusAsync()).Documents;

        Directory.Move(live.PathOf("src/Sample.Domain/Events"), live.PathOf("src/Sample.Domain/DomainEvents"));

        var newPath = live.PathOf("src/Sample.Domain/DomainEvents/OrderSubmittedEventArgs.cs");
        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(newPath).Count == 1);
        Assert.Empty(live.DocumentsAt(live.PathOf("src/Sample.Domain/Events/OrderSubmittedEventArgs.cs")));
        Assert.Equal(documentsBefore, (await live.StatusAsync()).Documents);
        Assert.Equal(1, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task TempFileRenamedToCs_IsAdded()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var temp = live.WriteFile("src/Sample.Domain/Services/Saved.tmp", "namespace Sample.Domain.Services; public sealed class SavedViaRename { }");
        var target = live.PathOf("src/Sample.Domain/Services/Saved.cs");

        File.Move(temp, target);

        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(target).Count == 1);
    }

    [Fact]
    public async Task CsFileRenamedToBackup_IsRemoved()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var file = live.PathOf("src/Sample.Domain/Services/OrderExtensions.cs");

        File.Move(file, file + ".bak");

        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(file).Count == 0);
    }

    [Fact]
    public async Task NewDirectoryWithFileWrittenImmediately_FileIsVisible()
    {
        await using var live = await LiveSampleSolution.StartAsync();

        live.WriteFile("src/Sample.Domain/Pricing/Rules/Discount.cs", "namespace Sample.Domain.Pricing; public sealed class Discount { }");

        await LiveSampleSolution.EventuallyAsync(async () => (await live.SymbolsAsync("Discount")).Count > 0);
    }

    [Fact]
    public async Task TwentyFilesInOneBatch_AllVisible_QueriesNeverSeePartialState()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var before = (await live.StatusAsync()).Documents;
        var observed = new HashSet<int>();
        using var stop = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var documents = (await live.StatusAsync()).Documents;
                lock (observed)
                    observed.Add(documents);
                await Task.Delay(5);
            }
        });

        for (var i = 0; i < 20; i++)
            live.WriteFile($"src/Sample.Domain/Generated/Batch{i}.cs", $"namespace Sample.Domain.Generated; public sealed class Batch{i} {{ }}");

        await LiveSampleSolution.EventuallyAsync(async () => (await live.StatusAsync()).Documents == before + 20);
        stop.Cancel();
        await poller;

        Assert.Equal(20, (await live.SymbolsAsync("Batch*")).Count);
        lock (observed)
            Assert.True(observed.IsSubsetOf([before, before + 20]), $"observed: {string.Join(", ", observed)}");
    }

    [Fact]
    public async Task FileOutsideAllProjects_NoChangeAndNoReload()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        var documentsBefore = (await live.StatusAsync()).Documents;

        live.WriteFile("scripts/Tool.cs", "public sealed class LooseTool { }");
        await WaitForSentinelAsync(live);

        Assert.Empty(await live.SymbolsAsync("LooseTool"));
        Assert.Equal(documentsBefore, (await live.StatusAsync()).Documents);
        Assert.Equal(1, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task FileInProjectWithCompileRemove_FallsBackToFullReload()
    {
        await using var live = await LiveSampleSolution.StartAsync(root =>
        {
            var project = Path.Combine(root, "src", "Sample.App", "Sample.App.csproj");
            var xml = File.ReadAllText(project).Replace(
                "</Project>",
                "  <ItemGroup><Compile Remove=\"Legacy/**\" /></ItemGroup>\n</Project>");
            File.WriteAllText(project, xml);
        });

        live.WriteFile("src/Sample.App/Feature.cs", "namespace Sample.App; public sealed class AppFeature { }");

        await live.WaitLoadedAsync(expectedAttempts: 2);
        await LiveSampleSolution.EventuallyAsync(async () => (await live.SymbolsAsync("AppFeature")).Count > 0);
    }

    [Fact]
    public async Task FileExcludedByCompileRemove_LaterSavesDoNotReloadAgain()
    {
        await using var live = await LiveSampleSolution.StartAsync(root =>
        {
            var project = Path.Combine(root, "src", "Sample.App", "Sample.App.csproj");
            var xml = File.ReadAllText(project).Replace(
                "</Project>",
                "  <ItemGroup><Compile Remove=\"Legacy/**\" /></ItemGroup>\n</Project>");
            File.WriteAllText(project, xml);
        });

        var excluded = live.WriteFile("src/Sample.App/Legacy/Old.cs", "namespace Sample.App; public sealed class OldOne { }");
        await live.WaitLoadedAsync(expectedAttempts: 2);
        Assert.Empty(live.DocumentsAt(excluded));

        File.AppendAllText(excluded, "// saved again\n");
        await WaitForSentinelAsync(live);

        Assert.Empty(live.DocumentsAt(excluded));
        Assert.Equal(2, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task FileInObjDirectory_NoChange()
    {
        await using var live = await LiveSampleSolution.StartAsync();

        live.WriteFile("src/Sample.Domain/obj/Scratch.cs", "public sealed class ObjOnly { }");
        await WaitForSentinelAsync(live);

        Assert.Empty(await live.SymbolsAsync("ObjOnly"));
        Assert.Equal(1, live.Server.LoadAttemptCount);
    }

    [Fact]
    public async Task Unused_AfterAddingFileThatUsesSymbol_SymbolIsNoLongerReported()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        Assert.Contains("\"Sample.Domain.Entities.Money\"", (await live.ResultJsonAsync("unused")).GetRawText());

        live.WriteFile("src/Sample.Domain/Entities/MoneyFactory.cs", """
            namespace Sample.Domain.Entities;

            public static class MoneyFactory
            {
                public static Money Zero(string currency) => new Money(0m, currency);
            }
            """);

        await LiveSampleSolution.EventuallyAsync(async () => (await live.SymbolsAsync("MoneyFactory")).Count > 0);
        var unused = (await live.ResultJsonAsync("unused")).GetRawText();
        Assert.DoesNotContain("\"Sample.Domain.Entities.Money\"", unused);
    }

    [Fact]
    public async Task WatcherErrorOnStoppedWatcher_RecreatesWatcherAndNextChangeIsDetected()
    {
        await using var live = await LiveSampleSolution.StartAsync();
        live.Server.GetWatcher(DaemonServer.WatcherKind.Source)!.EnableRaisingEvents = false;
        live.Server.GetWatcher(DaemonServer.WatcherKind.Tree)!.EnableRaisingEvents = false;

        live.Server.HandleWatcherError(DaemonServer.WatcherKind.Source, new IOException("watcher died"));
        live.Server.HandleWatcherError(DaemonServer.WatcherKind.Tree, new IOException("watcher died"));
        await live.WaitLoadedAsync(expectedAttempts: 2);

        live.WriteFile("src/Sample.Domain/AfterRecovery.cs", "namespace Sample.Domain; public sealed class AfterRecovery { }");
        await LiveSampleSolution.EventuallyAsync(async () => (await live.SymbolsAsync("AfterRecovery")).Count > 0);
    }

    /// <summary>
    /// Positive control for "nothing happens" tests: edits an existing document
    /// after the change under test and waits until the edit is applied, so the
    /// watcher and the queue have processed the earlier events too.
    /// </summary>
    private static async Task WaitForSentinelAsync(LiveSampleSolution live)
    {
        var sentinel = live.PathOf("src/Sample.Domain/Entities/Entity.cs");
        var marker = "// sentinel " + Guid.NewGuid().ToString("N");
        File.AppendAllText(sentinel, marker + "\n");
        await LiveSampleSolution.EventuallyAsync(async () =>
            (await live.DocumentsAt(sentinel)[0].GetTextAsync()).ToString().Contains(marker));
        await Task.Delay(QuietPeriod); // also let a scheduled full reload start, if one was (wrongly) scheduled
    }
}
